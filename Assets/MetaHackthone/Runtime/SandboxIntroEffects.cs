using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalAssembly
{
    // Reports a car body's knocks against the track so the intro can play the game's impact feedback.
    public sealed class SandboxIntroImpact : MonoBehaviour
    {
        public System.Action<Vector3,Vector3,float> Hit;
        void OnCollisionEnter(Collision collision)
        {
            if(collision.contactCount==0) return;
            var contact=collision.GetContact(0); Hit?.Invoke(contact.point,contact.normal,collision.relativeVelocity.magnitude);
        }
    }

    // Everything the first-entry intro shows or plays apart from the parts themselves: the darkened room, the
    // blue orb and its fog, the core's glow, the twist of space when it pulls, arrival rings, streaks behind
    // flying parts and positioned sounds.
    // The player's head is never moved, so these are what lead the eye. Self-contained; Dispose undoes all of it.
    // A tube of light from the building core to a part it is pulling in: a real mesh, rebuilt each frame along
    // a gentle curve, thick at the core and thinner at the part (Resources/SandboxTether.shader).
    public sealed class SandboxIntroTether : System.IDisposable
    {
        const int Rings=20, Sides=8;
        readonly GameObject go; readonly Mesh mesh;
        readonly Vector3[] positions=new Vector3[(Rings+1)*(Sides+1)], normals=new Vector3[(Rings+1)*(Sides+1)];
        readonly Vector2[] uvs=new Vector2[(Rings+1)*(Sides+1)];
        readonly MaterialPropertyBlock block=new();
        public SandboxIntroTether(Transform parent,Material material)
        {
            go=new GameObject("Intro tether"); go.transform.SetParent(parent,false); go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); go.transform.localScale=Vector3.one;
            mesh=new Mesh {name="Intro tether"}; mesh.MarkDynamic();
            var triangles=new int[Rings*Sides*6]; int t=0;
            for(int r=0;r<Rings;r++) for(int k=0;k<Sides;k++) {
                int a=r*(Sides+1)+k, b=a+Sides+1;
                triangles[t++]=a; triangles[t++]=b; triangles[t++]=a+1; triangles[t++]=a+1; triangles[t++]=b; triangles[t++]=b+1;
            }
            mesh.vertices=positions; mesh.normals=normals; mesh.uv=uvs; mesh.triangles=triangles;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            go.SetActive(false);
        }
        // from: the core; to: the part. reach (0 to 1) is how far the tube has reached out from the core; radius is
        // its thickness at the core; strength fades it in and out.
        public void Draw(Vector3 from,Vector3 to,float reach,float radius,float strength,float time)
        {
            if(!go) return;
            if(strength<=.001f || reach<=.001f) { go.SetActive(false); return; }
            go.SetActive(true);
            var line=to-from; float length=line.magnitude; if(length<1e-4f) { go.SetActive(false); return; }
            var direction=line/length; var side=Vector3.Cross(direction,Vector3.up); if(side.sqrMagnitude<1e-4f) side=Vector3.Cross(direction,Vector3.right); side.Normalize();
            var lift=Vector3.Cross(side,direction);
            // A slight sag and a slow writhe, so it reads as something alive rather than a rod.
            var control=(from+to)*.5f+lift*(length*.12f)+side*(length*.05f*Mathf.Sin(time*3.1f));
            Vector3 At(float u) { float k=1-u; return k*k*from+2*k*u*control+u*u*to; }
            for(int r=0;r<=Rings;r++) {
                float u=reach*r/Rings; var centre=At(u); var ahead=At(Mathf.Min(1,u+.02f))-At(Mathf.Max(0,u-.02f));
                var tangent=ahead.sqrMagnitude>1e-10f?ahead.normalized:direction;
                var across=Vector3.Cross(tangent,lift); if(across.sqrMagnitude<1e-6f) across=side; across.Normalize(); var up=Vector3.Cross(across,tangent);
                float thick=radius*Mathf.Lerp(1,.45f,u)*(1+.12f*Mathf.Sin(u*18-time*14));
                for(int k=0;k<=Sides;k++) {
                    float angle=k*Mathf.PI*2/Sides; var normal=across*Mathf.Cos(angle)+up*Mathf.Sin(angle);
                    int index=r*(Sides+1)+k; positions[index]=centre+normal*thick; normals[index]=normal; uvs[index]=new Vector2(reach>0?u/reach:0,(float)k/Sides);
                }
            }
            mesh.vertices=positions; mesh.normals=normals; mesh.uv=uvs; mesh.RecalculateBounds();
            var renderer=go.GetComponent<MeshRenderer>(); block.SetFloat("_Alpha",strength); renderer.SetPropertyBlock(block);
        }
        public void Dispose() { if(mesh) Object.Destroy(mesh); if(go) Object.Destroy(go); }
    }

    public sealed class SandboxIntroEffects
    {
        public static readonly Color Blue=new(.10f,.55f,1f), Pale=new(.62f,.9f,1f), Deep=new(.03f,.2f,.8f);
        public const float Dark=.12f; // room brightness while the intro plays
        // Written by the sequencer; drawn each Tick.
        public Vector3 Orb, Core, Beacon;
        public float OrbGlow, Fog, FogSize=.5f, CoreGlow, CoreSize=.035f, BeaconGlow;
        // The one light that parts in the dark are seen by (SandboxIntroLit): where it is, how far it reaches, how strong.
        public Vector3 Light;
        public float LightReach=1, LightStrength;
        // Parts adrift in the dark: how many there are, and how many the spark has pushed aside so far.
        public int Drifters => drifters.Count;
        public int Pushed { get; private set; }
        public float PushRadius=.34f;
        public float Room { get; private set; }=1;
        public float DarkestRoom { get; private set; }=1;
        public int CuesPlayed { get; private set; }
        public int Waves { get; private set; }
        // The pull, in the manner of a construction blueprint: a see-through copy of the build stands on the core
        // (Blueprint), and each part out in the dark is grabbed by a tube of light from the core (Tether) and
        // glows while it is held (Hold). All of it is real 3D geometry; nothing is drawn over the whole view.
        public int Pulls { get; private set; }
        public int BlueprintSlots => slots.Count;
        public int Held => held.Count;
        public int Tethers => tethers.Count;
        public int TethersDrawn { get; private set; }
        public float BlueprintAlpha;
        readonly Dictionary<Transform,GameObject> slots=new();
        readonly HashSet<Renderer> held=new();
        readonly List<SandboxIntroTether> tethers=new();
        readonly Material blueprintMaterial, holdMaterial, tetherMaterial;
        static readonly int AlphaId=Shader.PropertyToID("_Alpha");
        readonly GameObject root;
        readonly Camera camera;
        readonly SandboxSoundBank bank;
        readonly ParticleSystem glow, fog;
        readonly ParticleSystem.Particle[] points=new ParticleSystem.Particle[192], haze=new ParticleSystem.Particle[6];
        readonly Material glowMaterial, fogMaterial;
        readonly SandboxVfxController gold, blue;
        readonly List<(Vector3 position,float born,float size)> trail=new();
        readonly CameraClearFlags savedFlags; readonly Color savedBackground;
        readonly List<(Renderer renderer,Material[] originals)> dimmed=new();
        readonly Dictionary<Material,Material> copies=new();
        readonly List<(Material material,int property,Color color)> colors=new();
        static readonly string[] colorNames={"_BaseColor","_Color","_EmissionColor","_SpecColor","_DeepColor","_ShallowColor","_WaterColor","_FoamColor","_Tint","_ShadowTint","_SunTint"};
        readonly List<(Material material,int property,float value)> levels=new(); // shaders with a brightness scalar instead of a colour
        float time, nextKnock;
        sealed class Drifter { public Transform Root; public Vector3 Position, Velocity, Spin, Idle; public float Phase; public bool Pushed; }
        readonly List<Drifter> drifters=new();
        readonly Shader litShader;
        readonly Dictionary<Material,Material> darks=new();
        readonly Dictionary<Renderer,Material[]> looks=new();
        static readonly int LightPoint=Shader.PropertyToID("_SandboxIntroLight"), LightTint=Shader.PropertyToID("_SandboxIntroLightTint");
        Vector3 lastOrb; bool orbSeen;

        public SandboxIntroEffects(GameObject owner,Camera view,DownhillController level)
        {
            root=owner; camera=view; bank=Resources.Load<SandboxSoundBank>("SandboxSoundBank");
            litShader=Resources.Load<Shader>("SandboxIntroLit");
            if(camera) { savedFlags=camera.clearFlags; savedBackground=camera.backgroundColor; }
            var sprite=Resources.Load<Shader>("SandboxGlow"); var soft=Resources.Load<Shader>("SandboxFog");
            if(sprite) { glowMaterial=new Material(sprite) {renderQueue=3010}; glow=Particles("Intro glow",glowMaterial,points.Length); }
            // The fog draws first so the orb and the core always sit in front of their own haze.
            if(soft) { fogMaterial=new Material(soft) {renderQueue=3000}; fog=Particles("Intro fog",fogMaterial,haze.Length); }
            // Attach and impact feedback is the game's own, with the level's tuning. The waves reuse its rings in blue.
            gold=Controller("Intro part feedback",level && level.Vfx?level.Vfx.Settings:new SandboxVfxSettings());
            blue=Controller("Intro waves",new SandboxVfxSettings {Gold=Blue,AttachSeconds=.6f,RewardSeconds=.7f,RingWidthInRadii=.07f,MaxParticles=0});
            var hologram=Resources.Load<Shader>("SandboxHologram"); var tether=Resources.Load<Shader>("SandboxTether");
            if(hologram) {
                blueprintMaterial=new Material(hologram) {name="Intro blueprint"}; blueprintMaterial.SetFloat("_Fill",.1f); blueprintMaterial.SetColor("_Glow",new Color(.22f,.62f,1f));
                holdMaterial=new Material(hologram) {name="Intro held part"}; holdMaterial.SetFloat("_Fill",0); holdMaterial.SetColor("_Glow",new Color(.35f,.8f,1f)); holdMaterial.SetFloat("_Edge",1.6f);
            }
            if(tether) tetherMaterial=new Material(tether) {name="Intro tether"};
        }
        ParticleSystem Particles(string name,Material material,int capacity)
        {
            var system=new GameObject(name).AddComponent<ParticleSystem>(); system.transform.SetParent(root.transform,false);
            system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer=system.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=material; renderer.shadowCastingMode=ShadowCastingMode.Off;
            var main=system.main; main.loop=false; main.playOnAwake=false; main.maxParticles=capacity; main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=system.emission; emission.enabled=false; system.Play(); return system;
        }
        SandboxVfxController Controller(string name,SandboxVfxSettings settings)
        {
            var controller=new GameObject(name).AddComponent<SandboxVfxController>(); controller.transform.SetParent(root.transform,false);
            controller.Settings=settings; return controller;
        }

        // 1 is the untouched room. In XR this scales the passthrough layer; on a desktop it darkens the backdrop.
        public void SetRoom(float brightness)
        {
            Room=Mathf.Clamp01(brightness); DarkestRoom=Mathf.Min(DarkestRoom,Room);
            SandboxRewardPresentation.RequestRoomBrightness(root,Room);
            if(!camera || camera.stereoEnabled) return;
            if(Room>=1) { camera.clearFlags=savedFlags; camera.backgroundColor=savedBackground; }
            else { camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.Lerp(new Color(.004f,.006f,.012f),new Color(.22f,.25f,.3f),Room); }
        }
        // The track comes out of the dark by scaling private copies of its materials; shared assets are untouched.
        public void DimTrack(Transform environment)
        {
            RestoreTrack();
            foreach(var renderer in environment.GetComponentsInChildren<Renderer>(false)) {
                if(!renderer.enabled || renderer is ParticleSystemRenderer) continue;
                var originals=renderer.sharedMaterials; var replacements=new Material[originals.Length];
                for(int i=0;i<originals.Length;i++) {
                    var source=originals[i]; if(!source) continue;
                    if(!copies.TryGetValue(source,out var copy)) {
                        copy=new Material(source) {name=source.name+" (intro reveal)"}; copies.Add(source,copy);
                        foreach(var name in colorNames) if(copy.HasProperty(name)) { int id=Shader.PropertyToID(name); colors.Add((copy,id,copy.GetColor(id))); }
                        if(copy.HasProperty("_Brightness")) { int id=Shader.PropertyToID("_Brightness"); levels.Add((copy,id,copy.GetFloat(id))); }
                    }
                    replacements[i]=copy;
                }
                dimmed.Add((renderer,originals)); renderer.sharedMaterials=replacements;
            }
        }
        public void SetTrack(float brightness)
        {
            foreach(var item in colors) { var c=item.color; c.r*=brightness; c.g*=brightness; c.b*=brightness; item.material.SetColor(item.property,c); }
            foreach(var item in levels) item.material.SetFloat(item.property,item.value*brightness);
        }
        public void RestoreTrack()
        {
            foreach(var entry in dimmed) if(entry.renderer) entry.renderer.sharedMaterials=entry.originals;
            dimmed.Clear(); foreach(var copy in copies.Values) if(copy) Object.Destroy(copy); copies.Clear(); colors.Clear(); levels.Clear();
        }

        // Gives renderers a look that exists only in the intro's light: out of its reach they are not drawn at all.
        public void Darken(IEnumerable<Renderer> renderers)
        {
            if(!litShader) return;
            foreach(var renderer in renderers) {
                if(!renderer || looks.ContainsKey(renderer)) continue;
                var originals=renderer.sharedMaterials; var replacements=new Material[originals.Length];
                for(int i=0;i<originals.Length;i++) {
                    var source=originals[i]; if(!source) continue;
                    if(!darks.TryGetValue(source,out var dark)) {
                        dark=new Material(litShader) {name=source.name+" (seen by the intro's light)"};
                        dark.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.color:Color.white);
                        darks.Add(source,dark);
                    }
                    replacements[i]=dark;
                }
                looks.Add(renderer,originals); renderer.sharedMaterials=replacements;
            }
        }
        // Back to the part's own materials, as it looks in the game.
        public void Undarken(IEnumerable<Renderer> renderers)
        {
            foreach(var renderer in renderers) if(renderer && looks.Remove(renderer,out var originals)) renderer.sharedMaterials=originals;
        }
        // A part left hanging in the dark. It turns slowly where it is until the spark's wake reaches it.
        public void AddDrifter(Transform part,int seed)
        {
            float a=Mathf.Repeat(Mathf.Sin(seed*12.9898f)*43758.5453f,1), b=Mathf.Repeat(Mathf.Sin(seed*78.233f)*43758.5453f,1), c=Mathf.Repeat(Mathf.Sin(seed*37.719f)*43758.5453f,1);
            var idle=new Vector3(a-.5f,b-.5f,c-.5f)*36;
            drifters.Add(new Drifter {Root=part,Position=part.position,Idle=idle,Spin=idle,Phase=a*6.283f});
        }
        public void ClearDrifters()
        {
            foreach(var drifter in drifters) if(drifter.Root) Object.Destroy(drifter.Root.gameObject);
            drifters.Clear();
        }
        // The spark shoves aside whatever it passes close to. Measured against the whole stretch it moved this
        // frame, not only where it ended up, so a fast pass or a slow frame cannot jump over a part.
        void Drift(float delta)
        {
            var from=orbSeen?lastOrb:Orb; var step=Orb-from; lastOrb=Orb; orbSeen=true;
            var wake=delta>1e-5f?Vector3.ClampMagnitude(step/delta,6):Vector3.zero;
            foreach(var drifter in drifters) {
                if(!drifter.Root) continue;
                if(OrbGlow>.05f) {
                    float along=step.sqrMagnitude>1e-8f?Mathf.Clamp01(Vector3.Dot(drifter.Position-from,step)/step.sqrMagnitude):1;
                    var away=drifter.Position-(from+step*along); float distance=away.magnitude;
                    if(distance<PushRadius) {
                        float k=1-distance/PushRadius; var direction=distance>1e-4f?away/distance:Vector3.up;
                        // Mostly out of the way, partly swept along; the closer the pass, the harder the shove and the spin.
                        drifter.Velocity+=(direction*9f+wake*1.4f)*(k*delta);
                        drifter.Spin+=Vector3.Cross(direction,wake.sqrMagnitude>1e-4f?wake.normalized:Vector3.up)*(2200*k*delta);
                        drifter.Velocity=Vector3.ClampMagnitude(drifter.Velocity,1.6f); drifter.Spin=Vector3.ClampMagnitude(drifter.Spin,420);
                        if(!drifter.Pushed) { drifter.Pushed=true; Pushed++; }
                    }
                }
                drifter.Velocity*=Mathf.Exp(-1.2f*delta); drifter.Spin=Vector3.Lerp(drifter.Spin,drifter.Idle,1-Mathf.Exp(-.9f*delta));
                drifter.Position+=drifter.Velocity*delta;
                drifter.Root.SetPositionAndRotation(drifter.Position+Vector3.up*(Mathf.Sin(time*.7f+drifter.Phase)*.012f),Quaternion.Euler(drifter.Spin*delta)*drifter.Root.rotation);
            }
        }

        // A pull begins: a see-through copy of each part stands where the part will go. The copies are made from
        // the parts' stand-ins at their rest poses, so they are exactly the build that is about to happen.
        public void Blueprint(IEnumerable<SandboxIntroGhost> parts)
        {
            ClearBlueprint(); Pulls++; BlueprintAlpha=0;
            if(!blueprintMaterial) return;
            foreach(var ghost in parts) {
                if(!ghost?.Root) continue;
                var copy=Object.Instantiate(ghost.Root.gameObject,ghost.Root.parent,false); copy.name="Blueprint "+ghost.Part.Kind;
                copy.transform.localPosition=ghost.Position; copy.transform.localRotation=ghost.Rotation; copy.transform.localScale=Vector3.one;
                foreach(var renderer in copy.GetComponentsInChildren<Renderer>(true)) { var materials=new Material[renderer.sharedMaterials.Length]; for(int i=0;i<materials.Length;i++) materials[i]=blueprintMaterial; renderer.sharedMaterials=materials; }
                copy.SetActive(true); slots[ghost.Root]=copy;
            }
        }
        // The part has arrived: its place in the blueprint is filled by the part itself.
        public void FillSlot(SandboxIntroGhost ghost)
        {
            if(ghost?.Root && slots.Remove(ghost.Root,out var copy) && copy) Object.Destroy(copy);
        }
        public void ClearBlueprint()
        {
            foreach(var copy in slots.Values) if(copy) Object.Destroy(copy);
            slots.Clear();
        }
        // The part glows at its edges while the core holds it, over its own look.
        public void Hold(IEnumerable<Renderer> renderers)
        {
            if(!holdMaterial) return;
            foreach(var renderer in renderers) {
                if(!renderer || held.Contains(renderer)) continue;
                var materials=new List<Material>(renderer.sharedMaterials) {holdMaterial}; renderer.sharedMaterials=materials.ToArray(); held.Add(renderer);
            }
        }
        public void Release(IEnumerable<Renderer> renderers)
        {
            foreach(var renderer in renderers) {
                if(!renderer || !held.Remove(renderer)) continue;
                var materials=new List<Material>(renderer.sharedMaterials); materials.RemoveAll(m=>m==holdMaterial); renderer.sharedMaterials=materials.ToArray();
            }
        }
        // A tube of light from the core to a part. The caller moves it each frame (SandboxIntroTether.Draw).
        public SandboxIntroTether Tether()
        {
            var tether=new SandboxIntroTether(root.transform,tetherMaterial); tethers.Add(tether); TethersDrawn++; return tether;
        }
        public void Cut(SandboxIntroTether tether) { if(tether==null) return; tethers.Remove(tether); tether.Dispose(); }
        // A blue ring on the plane facing normal: sent out from the core, or (contract) closing in on it.
        public void Wave(Vector3 position,Vector3 normal,float radius,bool contract=false)
        {
            if(!blue) return;
            float size=radius/(Mathf.Max(.01f,blue.Settings.RingRadiusInPartSizes)*(contract?1:1.15f));
            blue.Play(new SandboxFeedbackEvent(contract?SandboxSoundCue.Detach:SandboxSoundCue.Arrive,position,normal,1,size,SandboxSurfaceMaterial.Wood,null)); Waves++;
        }
        // The game's own feedback for a part snapping on, something arriving, something let go.
        public void Feedback(SandboxSoundCue cue,Vector3 position,Vector3 normal,float size,float intensity=1)
        {
            if(gold) gold.Play(new SandboxFeedbackEvent(cue,position,normal,intensity,size,SandboxSurfaceMaterial.Wood,null));
        }
        public void Knock(Vector3 point,Vector3 normal,float speed)
        {
            if(time<nextKnock || speed<.12f) return;
            nextKnock=time+.08f; float intensity=Mathf.Lerp(.18f,1,Mathf.InverseLerp(.12f,1.5f,speed));
            Feedback(SandboxSoundCue.WoodImpact,point,normal,.12f,intensity); Play(SandboxSoundCue.WoodImpact,point,intensity,1,true);
        }
        // Leaves a fading streak behind anything that calls this while it moves.
        public void Trail(Vector3 position,float size)
        {
            if(trail.Count<96) trail.Add((position,time,size));
        }

        // far: at the track, several metres beyond the tabletop range the sound bank is tuned for.
        public AudioSource Play(SandboxSoundCue cue,Vector3 position,float gain=1,float pitch=1,bool far=false)
        {
            var entry=bank?bank.Find(cue):null; AudioClip clip=null;
            if(entry?.Clips!=null) for(int i=0;i<entry.Clips.Length && !clip;i++) clip=entry.Clips[(CuesPlayed+i)%entry.Clips.Length];
            if(!clip) return null;
            var go=new GameObject("Intro cue "+cue); go.transform.SetParent(root.transform,false); go.transform.position=position;
            var source=go.AddComponent<AudioSource>(); source.playOnAwake=false; source.dopplerLevel=0;
            source.outputAudioMixerGroup=bank.Group(entry.Bus); source.spatialBlend=1; source.spatialize=true;
            source.rolloffMode=AudioRolloffMode.Linear; source.minDistance=far?2:.4f; source.maxDistance=far?30:12;
            source.clip=clip; source.volume=Mathf.Clamp01(entry.Volume*(far?3:1.5f)*gain); source.pitch=pitch; source.Play();
            Object.Destroy(go,clip.length/Mathf.Max(.1f,pitch)+.2f); CuesPlayed++; return source;
        }

        void Mote(ref int count,Vector3 position,float size,Color color,float alpha)
        {
            if(count>=points.Length || alpha<=.002f) return;
            color.a=Mathf.Clamp01(alpha); points[count].position=position; points[count].startColor=color; points[count].startSize=size;
            points[count].startLifetime=2; points[count].remainingLifetime=2; count++;
        }
        public void Tick(float delta)
        {
            time+=delta; int count=0;
            Shader.SetGlobalVector(LightPoint,new Vector4(Light.x,Light.y,Light.z,Mathf.Max(.001f,LightReach)));
            Shader.SetGlobalVector(LightTint,new Vector4(Pale.r,Pale.g,Pale.b,Mathf.Clamp01(LightStrength)));
            Drift(delta);
            if(OrbGlow>.002f) {
                // A white-hot point inside a blue body, with a few sparks circling it.
                Mote(ref count,Orb,.14f*(.92f+.08f*Mathf.Sin(time*9)),Blue,OrbGlow*.9f); Mote(ref count,Orb,.05f,Color.white,OrbGlow);
                for(int i=0;i<10;i++) {
                    float angle=i*.62832f+time*(2.2f+.15f*i), radius=.05f+.02f*Mathf.Sin(time*3+i*1.7f);
                    Mote(ref count,Orb+new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle*1.3f+i)*radius*.7f,Mathf.Sin(angle)*radius),.02f,Pale,OrbGlow*(.5f+.5f*Mathf.Sin(time*11+i*2.1f)));
                }
            }
            if(CoreGlow>.002f) {
                float breathe=.85f+.15f*Mathf.Sin(time*3.2f);
                Mote(ref count,Core,CoreSize*5*breathe,Blue,CoreGlow*.45f); Mote(ref count,Core,CoreSize*2.2f,Pale,CoreGlow*.3f);
            }
            if(BeaconGlow>.002f) {
                // Marks the start of the track before anything is there: a soft pool of light with motes rising through it.
                Mote(ref count,Beacon,.7f,Blue,BeaconGlow*(.3f+.08f*Mathf.Sin(time*5)));
                for(int i=0;i<6;i++) { float rise=Mathf.Repeat(i/6f+time*.7f,1); Mote(ref count,Beacon+Vector3.up*(rise*1.1f),.1f,Pale,BeaconGlow*.85f*(1-rise)); }
            }
            for(int i=trail.Count-1;i>=0;i--) {
                float age=(time-trail[i].born)/.55f; if(age>=1) { trail.RemoveAt(i); continue; }
                float k=1-age; Mote(ref count,trail[i].position,trail[i].size*(.4f+.6f*k),Color.Lerp(Blue,Pale,k*k),k*k*.7f);
            }
            if(blueprintMaterial) blueprintMaterial.SetFloat(AlphaId,BlueprintAlpha*(.85f+.15f*Mathf.Sin(time*7)));
            if(glow) glow.SetParticles(points,count);
            int hazeCount=0;
            if(fog && Fog>.002f) {
                // Never larger than the gap to the eye, or the haze would fill the whole view up close.
                float size=camera?Mathf.Min(FogSize,Mathf.Max(.06f,Vector3.Distance(camera.transform.position,Orb)*.7f)):FogSize;
                for(int i=0;i<haze.Length;i++) {
                    float phase=time*(.35f+.07f*i)+i*1.9f;
                    var offset=new Vector3(Mathf.Cos(phase),Mathf.Sin(phase*1.3f)*.6f,Mathf.Sin(phase*.8f))*(size*.16f);
                    var color=Color.Lerp(Deep,Blue,(i%3)*.5f); color.a=Mathf.Clamp01(Fog*(.4f+.1f*Mathf.Sin(phase*2)));
                    haze[i].position=Orb+offset; haze[i].startColor=color; haze[i].startSize=size*(.75f+.12f*i);
                    haze[i].startLifetime=2; haze[i].remainingLifetime=2; hazeCount++;
                }
            }
            if(fog) fog.SetParticles(haze,hazeCount);
        }
        public void Dispose()
        {
            SetRoom(1); RestoreTrack(); ClearBlueprint(); Release(new List<Renderer>(held));
            foreach(var tether in tethers) tether.Dispose(); tethers.Clear();
            if(blueprintMaterial) Object.Destroy(blueprintMaterial); if(holdMaterial) Object.Destroy(holdMaterial); if(tetherMaterial) Object.Destroy(tetherMaterial);
            Shader.SetGlobalVector(LightTint,Vector4.zero);
            foreach(var entry in looks) if(entry.Key) entry.Key.sharedMaterials=entry.Value;
            looks.Clear(); foreach(var dark in darks.Values) if(dark) Object.Destroy(dark); darks.Clear();
            drifters.Clear();
            if(glowMaterial) Object.Destroy(glowMaterial); if(fogMaterial) Object.Destroy(fogMaterial);
        }
    }
}
