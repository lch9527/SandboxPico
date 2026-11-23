using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAssembly
{
    // Self-contained review scene: every sandbox effect loops on a flat plane next to the object it
    // applies to. Effects run through the same controller, shaders and helpers the game uses, so
    // tuning them here changes the game too. No level, physics or XR rig is involved.
    // It also runs in the editor without pressing Play: the stations are built as unsaved objects and
    // stepped from the editor loop, so opening the scene is enough to watch them in the Scene view.
    [ExecuteAlways]
    public sealed class VfxShowcase : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Every part is scaled so its largest side is this long (metres).")]
        public float PartSize=.12f;
        public float Spacing=.42f;
        public int Columns=6;
        public Color Ground=new(.22f,.24f,.28f);
        public SandboxVfxSettings Vfx=new();

        sealed class Station
        {
            public string Name; public Vector3 Origin; public Transform Root; public float Period=3, Clock; public int Fired;
            public Action<Station,float> Tick;
            // Fires once per loop when the station clock passes the given time.
            public bool At(float time,int slot) { int bit=1<<slot; if(Clock<time || (Fired&bit)!=0) return false; Fired|=bit; return true; }
        }
        readonly List<Station> stations=new();
        readonly List<SandboxFragments> fragments=new();
        readonly List<Material> materials=new();
        readonly List<(Renderer proxy,MaterialPropertyBlock block)> outlines=new();
        SandboxVfxController vfx;
        readonly List<SandboxReturnEffect> returning=new();
        Camera view;
        Transform container;
        float outlinePulse, now;
        double lastEditorTime;
        bool built;
        // Reward rays face whoever is looking: the game camera in Play mode, the Scene view otherwise.
        Quaternion Facing { get {
#if UNITY_EDITOR
            if(!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView) return UnityEditor.SceneView.lastActiveSceneView.camera.transform.rotation;
#endif
            return view?view.transform.rotation:Quaternion.identity;
        } }
        static void Kill(UnityEngine.Object item) { if(!item) return; if(Application.isPlaying) Destroy(item); else DestroyImmediate(item); }

        void OnEnable()
        {
            Teardown();
            container=new GameObject("Showcase contents (not saved)").transform; container.SetParent(transform,false);
            container.gameObject.hideFlags=HideFlags.DontSave;
            BuildStage(); BuildTemplates();
            vfx=container.gameObject.AddComponent<SandboxVfxController>(); vfx.Settings=Vfx;
            now=0; built=true;
#if UNITY_EDITOR
            if(!Application.isPlaying) { lastEditorTime=UnityEditor.EditorApplication.timeSinceStartup; UnityEditor.EditorApplication.update+=EditorStep; }
#endif
            float s=PartSize; var up=Vector3.up;

            // ---- Build feedback -------------------------------------------------------------
            Add("Attach",2.6f,(st,dt)=>{},st=>{
                Part(PartKind.DownhillCube,st.Root,Vector3.zero); var moving=Part(PartKind.DownhillCube,st.Root,Vector3.right*s*1.7f);
                var seam=st.Origin+new Vector3(s*.5f,s*.5f,0);
                st.Tick=(x,dt)=>{
                    moving.localPosition=Vector3.right*Mathf.Lerp(s*1.7f,s,Mathf.SmoothStep(0,1,x.Clock/.45f));
                    if(x.At(.45f,0)) Cue(SandboxSoundCue.Attach,seam,Vector3.right);
                };
            });
            Add("Detach",2.6f,(st,dt)=>{},st=>{
                Part(PartKind.DownhillCube,st.Root,Vector3.zero); var moving=Part(PartKind.DownhillCube,st.Root,Vector3.right*s);
                var seam=st.Origin+new Vector3(s*.5f,s*.5f,0);
                st.Tick=(x,dt)=>{
                    moving.localPosition=Vector3.right*Mathf.Lerp(s,s*1.7f,Mathf.SmoothStep(0,1,(x.Clock-.5f)/.45f));
                    if(x.At(.5f,0)) Cue(SandboxSoundCue.Detach,seam,Vector3.right);
                };
            });
            Hop("Wood impact",PartKind.DownhillCube,SandboxSoundCue.WoodImpact,SandboxSurfaceMaterial.Wood);
            Hop("Metal impact",PartKind.DownhillCore,SandboxSoundCue.MetalImpact,SandboxSurfaceMaterial.Metal);
            Hop("Rubber impact",PartKind.DownhillSmallWheel,SandboxSoundCue.RubberImpact,SandboxSurfaceMaterial.Rubber);
            Add("Drill dust",.25f,(st,dt)=>{},st=>{
                Part(PartKind.DownhillDrill,st.Root,Vector3.left*s*.55f);
                Block(st.Root,new Vector3(s*.55f,s*.5f,0),Vector3.one*s,Vfx.Sandstone);
                var contact=st.Origin+new Vector3(0,s*.5f,0);
                st.Tick=(x,dt)=>{ if(x.At(0,0)) Cue(SandboxSoundCue.Drill,contact,Vector3.left,SandboxSurfaceMaterial.Wood,true); };
            });
            Add("Water splash",2.4f,(st,dt)=>{},st=>{
                Disc(st.Root,Vector3.up*.002f,s*2.6f,new Color(.2f,.55f,.8f));
                var falling=Part(PartKind.DownhillCube,st.Root,Vector3.up*s*2.5f);
                st.Tick=(x,dt)=>{
                    float y=Mathf.Lerp(s*2.5f,-s*1.1f,Mathf.Clamp01(x.Clock/.9f)); falling.localPosition=Vector3.up*y;
                    falling.gameObject.SetActive(x.Clock<.9f);
                    if(x.At(.62f,0)) Cue(SandboxSoundCue.Splash,st.Origin+up*.004f,up);
                };
            });
            Add("Arrive",2.4f,(st,dt)=>{},st=>{
                Part(PartKind.DownhillCore,st.Root,Vector3.zero);
                st.Tick=(x,dt)=>{ if(x.At(.3f,0)) Cue(SandboxSoundCue.Arrive,st.Origin+up*s*.6f,up); };
            });
            Add("Success",3.2f,(st,dt)=>{},st=>{
                Part(PartKind.DownhillCore,st.Root,Vector3.zero);
                st.Tick=(x,dt)=>{ if(x.At(.3f,0)) Cue(SandboxSoundCue.Success,st.Origin+up*s*.6f,up); };
            });

            // ---- Part states ----------------------------------------------------------------
            Add("Loose part outline\n+ grab pulse",2.4f,(st,dt)=>{},st=>{
                var part=Part(PartKind.DownhillLargeWheel,st.Root,Vector3.zero);
                foreach(var renderer in part.GetComponentsInChildren<MeshRenderer>()) {
                    var go=new GameObject("Stencil silhouette"); go.transform.SetParent(renderer.transform,false);
                    go.AddComponent<MeshFilter>().sharedMesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    var proxy=go.AddComponent<MeshRenderer>(); proxy.enabled=false; outlines.Add((proxy,new MaterialPropertyBlock()));
                }
                st.Tick=(x,dt)=>{ outlinePulse=Mathf.Clamp01(1-(x.Clock-.4f)/.35f)*(x.Clock>=.4f?1:0); };
            });
            Add("Grab outline\nhold, then release",2.6f,(st,dt)=>{},st=>{
                var part=Part(PartKind.DownhillCube,st.Root,Vector3.zero);
                var material=Own(new Material(Resources.Load<Shader>("GrabOutline")));
                var block=new MaterialPropertyBlock(); var shells=new List<Renderer>();
                foreach(var renderer in part.GetComponentsInChildren<MeshRenderer>()) {
                    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    var go=new GameObject("Grab outline"); go.transform.SetParent(renderer.transform,false); go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var shell=go.AddComponent<MeshRenderer>(); var mats=new Material[mesh.subMeshCount]; for(int i=0;i<mats.Length;i++) mats[i]=material;
                    shell.sharedMaterials=mats; shell.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; shells.Add(shell);
                }
                st.Tick=(x,dt)=>{
                    // Same colour timing as SandboxGrabOutline: orange while held, flashing green on release, then fading.
                    bool held=x.Clock>=.3f && x.Clock<1.5f; float t=x.Clock-1.5f;
                    var c=Color.Lerp(new Color(1,.4f,0),Color.green,held?0:Mathf.Clamp01(t/.12f)); c.a=held?1:1-Mathf.Clamp01((t-.12f)/.25f);
                    block.SetColor("_Color",c); block.SetFloat("_Width",.002f*s/.06f);
                    foreach(var shell in shells) { shell.enabled=held || t>=0 && t<.37f; shell.SetPropertyBlock(block); }
                    part.localPosition=Vector3.up*(held?s*.35f:0);
                };
            });
            Add("Attachment ghost\nvalid, blocked, reject",3.6f,(st,dt)=>{},st=>{
                Part(PartKind.DownhillCube,st.Root,Vector3.zero);
                var ghost=Part(PartKind.DownhillCube,st.Root,Vector3.right*s);
                var material=Own(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
                material.SetFloat("_Surface",1); material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite",0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue=3000;
                foreach(var renderer in ghost.GetComponentsInChildren<MeshRenderer>()) {
                    var mats=new Material[renderer.sharedMaterials.Length]; for(int i=0;i<mats.Length;i++) mats[i]=material;
                    renderer.sharedMaterials=mats; renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                st.Tick=(x,dt)=>{
                    // Colours copied from AttachmentGhost: green when valid, red when blocked, a red pulse on reject.
                    float left=3.2f-x.Clock;
                    var c=x.Clock<1.4f?new Color(.15f,1,.55f,.3f):x.Clock<2.8f?new Color(1,.15f,.1f,.35f):
                        new Color(1,.12f,.06f,left>0?.15f+.4f*Mathf.Sin(left/.4f*Mathf.PI):0);
                    material.SetColor("_BaseColor",c);
                };
            });

            // ---- Transfer and reward ----------------------------------------------------------
            Add("Part return\ndissolve, fly, recompose",3f,(st,dt)=>{},st=>{
                var part=Part(PartKind.DownhillSmallWheel,st.Root,Vector3.left*s*1.2f);
                SandboxReturnEffect effect=null; bool atLeft=true;
                st.Tick=(x,dt)=>{
                    if(x.At(.5f,0)) {
                        var destination=new Vector3(atLeft?s*1.2f:-s*1.2f,0,0);
                        effect=new GameObject("Showcase return").AddComponent<SandboxReturnEffect>(); effect.transform.SetParent(st.Root,false);
                        effect.Initialize(part.GetComponentsInChildren<Renderer>(),null,part.position,part.rotation,st.Root,destination,Quaternion.identity,.25f,.9f,.35f,128);
                        returning.Add(effect);
                    }
                    if(effect && effect.Advance(dt)) {
                        effect.Restore(); returning.Remove(effect);
                        // In the editor the effect's own OnDestroy never runs, so remove its dissolve proxies here.
                        if(!Application.isPlaying) for(int i=st.Root.childCount-1;i>=0;i--) if(st.Root.GetChild(i).name=="Decomposing surface") DestroyImmediate(st.Root.GetChild(i).gameObject);
                        Kill(effect.gameObject); effect=null;
                        part.localPosition=new Vector3(atLeft?s*1.2f:-s*1.2f,0,0); atLeft=!atLeft;
                    }
                };
            });
            Add("Reward reveal",3f,(st,dt)=>{},st=>{
                var part=Part(PartKind.DownhillDrill,st.Root,Vector3.zero);
                var dissolve=Own(new Material(Resources.Load<Shader>("SandboxDissolve")));
                var renderers=part.GetComponentsInChildren<MeshRenderer>(); var originals=new Material[renderers.Length][]; var swapped=new Material[renderers.Length][];
                for(int i=0;i<renderers.Length;i++) { originals[i]=renderers[i].sharedMaterials; swapped[i]=new Material[originals[i].Length]; for(int m=0;m<swapped[i].Length;m++) swapped[i][m]=dissolve; }
                st.Tick=(x,dt)=>{
                    float t=Mathf.Clamp01((x.Clock-.4f)/.6f); bool forming=x.Clock<1f;
                    dissolve.SetFloat("_Dissolve",x.Clock<.4f?1:1-t);
                    for(int i=0;i<renderers.Length;i++) renderers[i].sharedMaterials=forming?swapped[i]:originals[i];
                    if(x.At(.4f,0)) Cue(SandboxSoundCue.Reveal,st.Origin+up*s*.5f,up);
                };
            });
            Add("Reward rise\nto the reveal point",3.4f,(st,dt)=>{},st=>{
                // The game poses the preview by its centre, so the part hangs half its size below this pivot.
                var centre=new GameObject("Reward centre").transform; centre.SetParent(st.Root,false);
                var part=Part(PartKind.DownhillDrill,centre,Vector3.down*s*.5f);
                var dissolve=Own(new Material(Resources.Load<Shader>("SandboxDissolve")));
                var renderers=part.GetComponentsInChildren<MeshRenderer>(); var originals=new Material[renderers.Length][]; var swapped=new Material[renderers.Length][];
                for(int i=0;i<renderers.Length;i++) { originals[i]=renderers[i].sharedMaterials; swapped[i]=new Material[originals[i].Length]; for(int m=0;m<swapped[i].Length;m++) swapped[i][m]=dissolve; }
                var sparkles=Fragments(96); var rays=Fragments(49);
                var goal=new Pose(st.Origin+up*s*1.7f,Quaternion.identity); float rise=s*1.1f, reach=s*.6f;
                var samples=new List<Vector3>(); for(int i=0;i<16;i++) samples.Add(Quaternion.Euler(i*47,i*83,0)*Vector3.forward*s*.35f);
                st.Tick=(x,dt)=>{
                    // Same timing as the game: 1.1 s reveal, then the halo pops as the part lands.
                    float t=Mathf.Clamp01((x.Clock-.3f)/1.1f), pop=t<1?0:Mathf.Clamp01(1-(x.Clock-1.4f)/.35f), radius=s*1.35f*(1+.45f*pop); bool forming=t<1;
                    var pose=SandboxRewardCoordinator.RevealPose(goal,rise,t); centre.SetPositionAndRotation(pose.position,pose.rotation);
                    dissolve.SetFloat("_Dissolve",SandboxRewardCoordinator.RevealDissolve(t));
                    for(int i=0;i<renderers.Length;i++) renderers[i].sharedMaterials=forming?swapped[i]:originals[i];
                    if(forming) SandboxRewardCoordinator.DrawReveal(sparkles,goal,centre,samples,reach,rise,t,x.Clock); else sparkles.Clear();
                    SandboxRewardCoordinator.DrawRays(rays,centre.position,Facing,radius,s*.13f*(1+.45f*pop),now,forming?Mathf.SmoothStep(0,1,t):1+1.5f*pop);
                    if(x.At(.3f,0)) Cue(SandboxSoundCue.Reveal,goal.position,up);
                };
            });
            Add("Reward halo\nand spokes",1f,(st,dt)=>{},st=>{
                var part=Part(PartKind.DownhillDrill,st.Root,Vector3.up*s*.5f); var rays=Fragments(49);
                st.Tick=(x,dt)=>{
                    part.Rotate(Vector3.up,180*dt,Space.World);
                    SandboxRewardCoordinator.DrawRays(rays,st.Origin+up*s,Facing,s*1.35f,s*.13f,now);
                };
            });
            Add("Reward sparkle stream",1f,(st,dt)=>{},st=>{
                Part(PartKind.DownhillDrill,st.Root,Vector3.left*s*1.3f); Ball(st.Root,new Vector3(s*1.3f,s*.4f,0),s*.45f,new Color(.85f,.68f,.55f));
                var stream=Fragments(48); float age=0;
                st.Tick=(x,dt)=>{ age+=dt; SandboxRewardCoordinator.DrawStream(stream,st.Origin+new Vector3(-s*1.3f,s*.5f,0),st.Origin+new Vector3(s*1.3f,s*.5f,0),age/.65f); };
            });
            Add("Reward to fixed menu",1.6f,(st,dt)=>{},st=>{
                Part(PartKind.DownhillDrill,st.Root,Vector3.left*s*1.3f);
                var target=Part(PartKind.DownhillLongBlock,st.Root,Vector3.right*s*1.3f);
                var stream=Fragments(48);
                st.Tick=(x,dt)=>SandboxRewardCoordinator.DrawStream(stream,st.Origin+Vector3.left*s*1.3f,target.position,x.Clock/.45f);
            });
            Add("Reward claim",2.6f,(st,dt)=>{},st=>{
                var part=Part(PartKind.DownhillDrill,st.Root,Vector3.zero);
                st.Tick=(x,dt)=>{
                    part.gameObject.SetActive(x.Clock<.5f || x.Clock>1.6f);
                    if(x.At(.5f,0)) Cue(SandboxSoundCue.Claim,st.Origin+up*s*.5f,up);
                };
            });

            // ---- Hands, aiming and lines ------------------------------------------------------
            Add("Mode gesture rings\n(shown 2.5x)",3f,(st,dt)=>{},st=>{
                var hand=new GameObject("Fingers").transform; hand.SetParent(st.Root,false); hand.localScale=Vector3.one*2.5f;
                var material=Own(PartFactory.Material(new Color(.1f,1f,.35f))); var mesh=ChallengeGestureFeedback.RingMesh(); meshes.Add(mesh);
                var rings=new Transform[3]; var sparks=new ParticleSystem[3];
                for(int f=0;f<3;f++) {
                    float x0=(f-1)*.03f;
                    Prop(PrimitiveType.Capsule,hand,new Vector3(x0,.02f,0),new Vector3(.016f,.02f,.016f),new Color(.85f,.68f,.55f));
                    var go=new GameObject("Mode ring "+f); go.transform.SetParent(hand,false);
                    go.transform.localPosition=new Vector3(x0,.03f,0); go.transform.localRotation=Quaternion.Euler(90,0,0);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterial=material; rings[f]=go.transform;
                    // Same sparkle emitter as ChallengeGestureFeedback.
                    var fx=new GameObject("Ring sparkle"); fx.transform.SetParent(go.transform,false);
                    var ps=fx.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main=ps.main; main.playOnAwake=false; main.loop=true; main.startLifetime=.18f; main.startSpeed=.012f; main.startSize=.002f; main.maxParticles=8;
                    main.startColor=new Color(.2f,1,.4f); main.scalingMode=ParticleSystemScalingMode.Hierarchy;
                    var emission=ps.emission; emission.rateOverTime=12;
                    var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Circle; shape.radius=.011f;
                    fx.GetComponent<ParticleSystemRenderer>().sharedMaterial=material; sparks[f]=ps; go.SetActive(false);
                }
                st.Tick=(x,dt)=>{
                    float pop=x.Clock-1.7f;
                    for(int f=0;f<3;f++) {
                        bool lit=x.Clock>=.3f+f*.4f && x.Clock<2.3f;
                        rings[f].gameObject.SetActive(lit);
                        rings[f].localScale=Vector3.one*(pop>=0 && pop<.35f?1+1.5f*Mathf.Sin(pop/.35f*Mathf.PI):1);
                        if(lit) { if(!sparks[f].isPlaying) sparks[f].Play(); if(!Application.isPlaying) sparks[f].Simulate(dt,true,false); }
                        else if(sparks[f].isPlaying) sparks[f].Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                };
            });
            Add("Trajectory guide",4f,(st,dt)=>{},st=>{
                Block(st.Root,new Vector3(-s*1.2f,s*.15f,0),new Vector3(s*.6f,s*.3f,s*.6f),new Color(.45f,.3f,.2f));
                var shader=Shader.Find("PhysicalAssembly/Trajectory"); if(!shader) shader=Shader.Find("Universal Render Pipeline/Unlit");
                var line=Line(st.Root,Own(new Material(shader)),.004f,101); line.numCornerVertices=2; line.numCapVertices=2;
                var cream=new Color(1,.90f,.66f);
                line.colorGradient=new Gradient { colorKeys=new[]{new GradientColorKey(cream,0),new GradientColorKey(cream,1)},
                    alphaKeys=new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,.5f),new GradientAlphaKey(0,1)} };
                st.Tick=(x,dt)=>{
                    float angle=Mathf.Lerp(48,70,.5f+.5f*Mathf.Sin(x.Clock/4f*Mathf.PI*2))*Mathf.Deg2Rad;
                    var v=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0)*1.75f; var o=new Vector3(-s*1.2f,s*.3f,0);
                    float flight=(v.y+Mathf.Sqrt(v.y*v.y+2*9.81f*o.y))/9.81f;
                    for(int i=0;i<101;i++) { float t=flight*i/100f; line.SetPosition(i,o+v*t+Vector3.down*(4.905f*t*t)); }
                };
            });
            Add("Dock laser\nvalid, invalid",2.4f,(st,dt)=>{},st=>{
                Block(st.Root,new Vector3(-s*1.2f,s*.1f,0),new Vector3(s*.7f,s*.2f,s*.7f),new Color(.45f,.3f,.2f));
                var block=Part(PartKind.DownhillCube,st.Root,Vector3.zero);
                var material=Own(PartFactory.Material(Color.green)); var line=Line(st.Root,material,.003f,2); line.endWidth=.0015f;
                st.Tick=(x,dt)=>{
                    bool valid=x.Clock<1.2f; block.localPosition=new Vector3(s*.9f,s*(valid?.8f:1.3f),valid?0:s*.6f);
                    line.SetPosition(0,new Vector3(-s*1.2f,s*.2f,0)); line.SetPosition(1,block.localPosition+Vector3.up*s*.5f);
                    var c=valid?Color.green:Color.red; line.startColor=line.endColor=c; material.color=c;
                };
            });
            Add("Attachment preview line",2f,(st,dt)=>{},st=>{
                Part(PartKind.DownhillCube,st.Root,Vector3.zero); var held=Part(PartKind.DownhillCube,st.Root,Vector3.zero);
                var line=Line(st.Root,Resources.Load<Material>("AssemblyPreview"),.005f,2);
                st.Tick=(x,dt)=>{
                    held.localPosition=new Vector3(s*1.7f,s*(.5f+.3f*Mathf.Sin(x.Clock*Mathf.PI)),0);
                    line.SetPosition(0,held.localPosition+new Vector3(-s*.5f,s*.5f,0)); line.SetPosition(1,new Vector3(s*.5f,s*.5f,0));
                };
            });
            Add("Locked connection marker",1f,(st,dt)=>{},st=>{
                Part(PartKind.DownhillCube,st.Root,Vector3.zero);
                var line=Line(st.Root,Own(PartFactory.Material(new Color(.25f,1,.5f))),s*.05f,32,true);
                for(int i=0;i<32;i++) { float a=i*Mathf.PI/16; line.SetPosition(i,new Vector3(Mathf.Cos(a)*s*.37f,s*.5f+Mathf.Sin(a)*s*.37f,-s*.5f-.001f)); }
            });
            Add("Chain links\nand spring",2f,(st,dt)=>{},st=>{
                var timber=Own(PartFactory.Material(new Color(.45f,.3f,.18f)));
                for(int l=0;l<5;l++) {
                    var link=Line(st.Root,timber,.006f,12,true); link.numCornerVertices=2;
                    for(int i=0;i<12;i++) {
                        float a=i*Mathf.PI/6, side=Mathf.Cos(a)*.012f, y=.03f+l*.03f+Mathf.Sin(a)*.02f;
                        link.SetPosition(i,new Vector3(-s+(l%2==0?side:0),y,l%2==0?0:side));
                    }
                }
                var spring=Line(st.Root,Own(PartFactory.Material(new Color(.7f,.73f,.77f))),.003f,33); spring.numCornerVertices=2;
                st.Tick=(x,dt)=>{
                    float height=.11f*(1+.25f*Mathf.Sin(x.Clock*Mathf.PI));
                    for(int i=0;i<33;i++) { float t=i/32f; spring.SetPosition(i,new Vector3(s*.8f+Mathf.Cos(t*Mathf.PI*8)*.02f,.01f+t*height,Mathf.Sin(t*Mathf.PI*8)*.02f)); }
                };
            });

            // ---- Environment and interface ----------------------------------------------------
            Add("Sandbox water",1f,(st,dt)=>{},st=>Water(st.Root,"SandboxWater"));
            Add("Valley water",1f,(st,dt)=>{},st=>Water(st.Root,"ValleyWater"));
            Add("Build boundary",1f,(st,dt)=>{},st=>{
                var prefab=Resources.Load<GameObject>(SandboxBuildVolume.ArtworkResource); if(!prefab) return;
                var go=Instantiate(prefab,st.Root);
                foreach(var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled=false;
                foreach(var collider in go.GetComponentsInChildren<Collider>(true)) Kill(collider);
                Fit(go.transform,s*2.6f);
            });
            Add("Steering emblem",1f,(st,dt)=>{},st=>{
                var mesh=Resources.Load<Mesh>("SteeringEmblemDisc"); var material=Resources.Load<Material>("SteeringEmblem"); if(!mesh || !material) return;
                var go=new GameObject("Emblem"); go.transform.SetParent(st.Root,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterial=material;
                // Lay the disc flat if it was modelled upright.
                var size=mesh.bounds.size; if(size.z<size.y) go.transform.localRotation=Quaternion.Euler(90,0,0);
                Fit(go.transform,s*2.4f); go.transform.position+=up*.003f;
                st.Tick=(x,dt)=>go.transform.Rotate(Vector3.up,25*dt,Space.World);
            });
            Add("Start tile\n+ reward pulse",4f,(st,dt)=>{},st=>{
                try {
                    var tuning=ScriptableObject.CreateInstance<DownhillSettings>(); objects.Add(tuning);
                    var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name="START"; go.transform.SetParent(st.Root,false);
                    go.transform.localPosition=up*s*1.4f; go.transform.localScale=new Vector3(.34f,.115f,.025f);
                    go.GetComponent<Collider>().isTrigger=true; go.AddComponent<WorkshopButton>().Command="Showcase";
                    var tile=go.AddComponent<FloatingStartButton>(); tile.Configure(tuning);
                    st.Tick=(x,dt)=>tile.RewardPulse=x.Clock>2f;
                } catch(Exception e) { Debug.LogWarning("VFX showcase could not build the start tile: "+e.Message); }
            });
            Add("Placement outlines\narena, floor",1f,(st,dt)=>{},st=>{
                // Real materials and widths; the rectangles stand in for the room-sized shapes.
                var arena=Line(st.Root,Resources.Load<Material>("AssemblyPreview"),.012f,4,true);
                var floor=Line(st.Root,Own(PartFactory.Material(new Color(.25f,1,.78f))),.012f,4,true);
                for(int i=0;i<4;i++) {
                    var corner=new Vector3(i==0||i==3?-1:1,0,i<2?-1:1);
                    arena.SetPosition(i,Vector3.Scale(corner,new Vector3(s*1.5f,0,s))+up*.006f);
                    floor.SetPosition(i,Vector3.Scale(corner,new Vector3(s*.7f,0,s*.45f))+up*.006f);
                }
            });
        }
        readonly List<Mesh> meshes=new();
        readonly List<UnityEngine.Object> objects=new();
        LineRenderer Line(Transform parent,Material material,float width,int count,bool loop=false)
        {
            var line=new GameObject("Line").AddComponent<LineRenderer>(); line.transform.SetParent(parent,false);
            line.useWorldSpace=false; line.sharedMaterial=material; line.startWidth=line.endWidth=width; line.positionCount=count; line.loop=loop;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; return line;
        }
        void Water(Transform parent,string shader)
        {
            var quad=GameObject.CreatePrimitive(PrimitiveType.Quad); quad.name=shader; Kill(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent,false); quad.transform.localPosition=Vector3.up*.003f; quad.transform.localRotation=Quaternion.Euler(90,0,0);
            quad.transform.localScale=new Vector3(PartSize*2.8f,PartSize*2.8f,1);
            var renderer=quad.GetComponent<Renderer>(); renderer.sharedMaterial=Own(new Material(Resources.Load<Shader>(shader)));
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        // Scales an object so its largest side is the given length, centred on its station and resting on the ground.
        static void Fit(Transform target,float length)
        {
            var renderers=target.GetComponentsInChildren<Renderer>(); if(renderers.Length==0) return;
            var bounds=renderers[0].bounds; foreach(var r in renderers) bounds.Encapsulate(r.bounds);
            target.localScale*=length/Mathf.Max(.0001f,Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z));
            bounds=renderers[0].bounds; foreach(var r in renderers) bounds.Encapsulate(r.bounds);
            var origin=target.parent?target.parent.position:Vector3.zero;
            target.position+=new Vector3(origin.x-bounds.center.x,origin.y-bounds.min.y,origin.z-bounds.center.z);
        }

        void Hop(string label,PartKind prefab,SandboxSoundCue cue,SandboxSurfaceMaterial material)
        {
            float s=PartSize;
            Add(label,1.5f,(st,dt)=>{},st=>{
                var part=Part(prefab,st.Root,Vector3.zero);
                st.Tick=(x,dt)=>{
                    float p=Mathf.Clamp01(x.Clock/1.1f); part.localPosition=Vector3.up*(s*2.2f*4*p*(1-p));
                    if(x.At(1.1f,0)) Cue(cue,st.Origin+Vector3.up*.002f,Vector3.up,material);
                };
            });
        }
        void Add(string label,float period,Action<Station,float> placeholder,Action<Station> build)
        {
            int index=stations.Count, column=index%Columns, row=index/Columns;
            var station=new Station {Name=label,Period=period,Tick=placeholder,
                Origin=transform.position+new Vector3((column-(Columns-1)*.5f)*Spacing,0,row*Spacing*1.35f)};
            station.Root=new GameObject(label.Replace('\n',' ')).transform; station.Root.SetParent(container,false); station.Root.position=station.Origin;
            // Stagger the loops so the shared particle budget is never asked for everything at once.
            station.Clock=-(index%7)*.33f;
            var text=new GameObject("Label").AddComponent<TextMesh>(); text.transform.SetParent(station.Root,false);
            text.transform.localPosition=new Vector3(0,.004f,-PartSize*1.5f); text.transform.localRotation=Quaternion.Euler(55,0,0);
            text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.GetComponent<Renderer>().sharedMaterial=text.font.material;
            text.text=label; text.fontSize=64; text.characterSize=.0042f; text.anchor=TextAnchor.UpperCenter; text.alignment=TextAlignment.Center; text.color=Color.white;
            stations.Add(station); build(station);
        }
        void Cue(SandboxSoundCue cue,Vector3 position,Vector3 normal,SandboxSurfaceMaterial material=SandboxSurfaceMaterial.Wood,bool silent=false)
            => vfx.Play(new SandboxFeedbackEvent(cue,position,normal,1,PartSize,material,this,silent));
        SandboxFragments Fragments(int count) { var f=new SandboxFragments(container,count); fragments.Add(f); return f; }
        Material Own(Material material) { materials.Add(material); return material; }

#if UNITY_EDITOR
        void EditorStep()
        {
            if(!this || Application.isPlaying || !built) { UnityEditor.EditorApplication.update-=EditorStep; return; }
            double time=UnityEditor.EditorApplication.timeSinceStartup; float dt=Mathf.Clamp((float)(time-lastEditorTime),0,.1f); lastEditorTime=time;
            // Outside Play mode nothing calls Update, so step the stations and the shared effect controller by hand.
            Step(dt); if(vfx) vfx.Advance(dt); LateStep();
            UnityEditor.SceneView.RepaintAll();
        }
#endif
        void Update() { if(Application.isPlaying && built) Step(Time.unscaledDeltaTime); }
        void LateUpdate() { if(Application.isPlaying && built) LateStep(); }
        void Step(float dt)
        {
            now+=dt;
            foreach(var station in stations) {
                station.Clock+=dt;
                if(station.Clock>=station.Period) { station.Clock-=station.Period; station.Fired=0; }
                if(station.Clock>=0) station.Tick(station,dt);
            }
        }
        void LateStep()
        {
            // Feed the stencil outline pass the same way SandboxPartHighlights does in a level.
            SandboxPartHighlights.Draws.Clear();
            foreach(var entry in outlines) {
                if(!entry.proxy) continue;
                entry.block.SetColor("_Color",Color.Lerp(new Color(1,.72f,.02f,1),new Color(1,.95f,.55f,1),outlinePulse));
                entry.block.SetFloat("_Width",.002f*PartSize/.06f*(1+.3f*outlinePulse));
                entry.proxy.SetPropertyBlock(entry.block);
                SandboxPartHighlights.Draws.Add(new SandboxPartHighlights.Draw {mesh=entry.proxy.GetComponent<MeshFilter>().sharedMesh,renderer=entry.proxy,matrix=entry.proxy.localToWorldMatrix,properties=entry.block});
            }
        }
        void OnDisable() => Teardown();
        void Teardown()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update-=EditorStep;
#endif
            if(!built && !container) return;
            built=false;
            SandboxPartHighlights.Draws.Clear();
            if(vfx) vfx.Clear();
            foreach(var effect in returning) if(effect) effect.Restore();
            foreach(var f in fragments) f.Release();
            foreach(var material in materials) Kill(material);
            foreach(var mesh in meshes) Kill(mesh);
            foreach(var item in objects) Kill(item);
            returning.Clear(); fragments.Clear(); materials.Clear(); meshes.Clear(); objects.Clear(); stations.Clear(); outlines.Clear(); templates.Clear();
            if(container) Kill(container.gameObject);
            container=null; vfx=null; view=null;
        }

        // Real part geometry is generated by the game's own factory. The templates stay alive but inactive
        // for the whole session, because each part owns the meshes and materials its copies share.
        readonly Dictionary<PartKind,GameObject> templates=new();
        GameObject templateHost;
        void BuildTemplates()
        {
            templateHost=new GameObject("Part templates"); templateHost.transform.SetParent(container,false);
            var world=templateHost.AddComponent<AssemblyWorld>();
            foreach(var kind in new[]{PartKind.DownhillCube,PartKind.DownhillLongBlock,PartKind.DownhillSmallWheel,PartKind.DownhillLargeWheel,PartKind.DownhillCore,PartKind.DownhillDrill}) {
                try {
                    var part=DownhillParts.Create(world,kind,new Pose(Vector3.zero,Quaternion.identity),1,gridVersion:DownhillParts.DesignerGridVersion);
                    world.UnregisterPart(part); part.World=null; part.gameObject.SetActive(false);
                    part.transform.SetParent(templateHost.transform,false); templates[kind]=part.gameObject;
                } catch(Exception e) { Debug.LogWarning("VFX showcase could not build "+kind+": "+e.Message); }
            }
            templateHost.SetActive(false);
        }

        // ---- Stage and props ------------------------------------------------------------------
        void BuildStage()
        {
            int rows=Mathf.CeilToInt(30f/Columns); float width=Columns*Spacing+.6f, depth=rows*Spacing*1.35f+.8f;
            var centre=transform.position+new Vector3(0,0,(rows-1)*Spacing*1.35f*.5f);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name="Flat ground"; ground.transform.SetParent(container,false);
            ground.transform.position=centre; ground.transform.localScale=new Vector3(width/10,1,depth/10);
            Kill(ground.GetComponent<Collider>()); ground.GetComponent<Renderer>().sharedMaterial=Own(PartFactory.Material(Ground));
            view=Camera.main;
            // The Scene view is the camera outside Play mode, so none is created there.
            if(!view && Application.isPlaying) {
                view=new GameObject("Showcase camera").AddComponent<Camera>(); view.tag="MainCamera"; view.transform.SetParent(container,false);
                view.transform.position=centre+new Vector3(0,1.9f,-depth*.5f-.7f); view.transform.LookAt(centre+Vector3.back*.15f);
                view.fieldOfView=50; view.nearClipPlane=.02f; view.clearFlags=CameraClearFlags.SolidColor; view.backgroundColor=new Color(.10f,.11f,.14f);
            }
            if(!FindFirstObjectByType<Light>()) {
                var sun=new GameObject("Showcase light").AddComponent<Light>(); sun.type=LightType.Directional; sun.intensity=1.1f; sun.transform.SetParent(container,false);
                sun.transform.rotation=Quaternion.Euler(50,-30,0);
                RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight=new Color(.45f,.47f,.5f);
            }
        }
        // A visual-only copy of a part prefab: meshes and materials, no bodies, colliders or sockets.
        // It is scaled so its largest side is PartSize and it rests on the ground at the given spot.
        Transform Part(PartKind kind,Transform parent,Vector3 localPosition)
        {
            templates.TryGetValue(kind,out var prefab);
            var root=new GameObject(prefab?prefab.name:"Placeholder").transform; root.SetParent(parent,false); root.localPosition=localPosition;
            var holder=new GameObject("Meshes").transform; holder.SetParent(root,false);
            var bounds=new Bounds(); bool any=false;
            if(prefab) foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true)) {
                var source=filter.GetComponent<MeshRenderer>();
                if(!source || !source.enabled || !filter.sharedMesh || filter.GetComponentInParent<AssemblySocket>()) continue;
                var child=new GameObject(filter.name).transform; child.SetParent(holder,false);
                child.localPosition=prefab.transform.InverseTransformPoint(filter.transform.position);
                child.localRotation=Quaternion.Inverse(prefab.transform.rotation)*filter.transform.rotation; child.localScale=filter.transform.lossyScale;
                child.gameObject.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
                child.gameObject.AddComponent<MeshRenderer>().sharedMaterials=source.sharedMaterials;
                var b=filter.sharedMesh.bounds;
                for(int i=0;i<8;i++) {
                    var corner=child.localPosition+child.localRotation*Vector3.Scale(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)),child.localScale);
                    if(!any) { bounds=new Bounds(corner,Vector3.zero); any=true; } else bounds.Encapsulate(corner);
                }
            }
            if(!any) {
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(cube.GetComponent<Collider>()); cube.transform.SetParent(holder,false);
                cube.GetComponent<Renderer>().sharedMaterial=Own(PartFactory.Material(new Color(.9f,.55f,.2f)));
                bounds=new Bounds(Vector3.zero,Vector3.one);
            }
            float scale=PartSize/Mathf.Max(.0001f,Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z));
            holder.localScale=Vector3.one*scale; holder.localPosition=-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale;
            return root;
        }
        Transform Prop(PrimitiveType type,Transform parent,Vector3 localPosition,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(type); Kill(go.GetComponent<Collider>()); go.transform.SetParent(parent,false);
            go.transform.localPosition=localPosition; go.transform.localScale=scale; go.GetComponent<Renderer>().sharedMaterial=Own(PartFactory.Material(color));
            return go.transform;
        }
        void Block(Transform parent,Vector3 position,Vector3 size,Color color) => Prop(PrimitiveType.Cube,parent,position,size,color);
        void Disc(Transform parent,Vector3 position,float diameter,Color color) => Prop(PrimitiveType.Cylinder,parent,position,new Vector3(diameter,.002f,diameter),color);
        void Ball(Transform parent,Vector3 position,float diameter,Color color) => Prop(PrimitiveType.Sphere,parent,position,Vector3.one*diameter,color);
    }
}
