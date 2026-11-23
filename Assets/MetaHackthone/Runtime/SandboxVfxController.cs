using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalAssembly
{
    [DisallowMultipleComponent]
    public sealed class SandboxVfxController : MonoBehaviour
    {
        // Glow sprites fade to nothing at their edge, so they are drawn larger than the old hard squares.
        public const float GlowScale=2.4f;
        public SandboxVfxSettings Settings=new();
        public int ActiveParticles { get; private set; }
        public int ActiveRings { get { int count=0; foreach(var ring in rings) if(ring.Active) count++; return count; } }
        public int PresentedCount { get; private set; }
        // Mote: drifting sparkles. Impact: debris that falls back. Splash: tall droplets. Fountain: staggered rising column.
        enum Style { Mote, Impact, Splash, Fountain }
        sealed class Burst {
            public SandboxFragments Fragments; public Vector3 Origin,Normal; public Quaternion Rotation;
            public float Age,Duration,Size,Heat; public Color Color; public Style Style; public bool Flash; public int Seed; public Object Source;
        }
        sealed class Ring {
            public LineRenderer Line; public bool Active,Contract; public Vector3 Origin; public Quaternion Rotation;
            public float Age,Duration,Radius; public Color Color; public Object Source;
        }
        readonly List<Burst> bursts=new(8);
        readonly List<Ring> rings=new(8);
        SandboxPresentation presentation;
        Material ringMaterial;
        SandboxVictoryCelebration celebration;
        int seed;
        readonly Dictionary<AssemblyPart,float> grabs=new();
        readonly List<AssemblyPart> expired=new();
        public void Initialize(SandboxPresentation source,SandboxVfxSettings settings)
        {
            Unsubscribe(); Clear(); presentation=source; Settings=settings??new SandboxVfxSettings(); Subscribe();
        }
        void Subscribe() { if(presentation) { presentation.Presented+=Present; presentation.Cleared+=Clear; SandboxFragments.PriorityRequested+=ClearTransient; } }
        void Unsubscribe() { SandboxFragments.PriorityRequested-=ClearTransient; if(presentation) { presentation.Presented-=Present; presentation.Cleared-=Clear; } }
        void OnEnable() => Subscribe();
        void OnDisable() { Unsubscribe(); Clear(); }
        void OnDestroy() { if(ringMaterial) Destroy(ringMaterial); }
        public float GrabPulse(AssemblyPart part) => part && grabs.TryGetValue(part,out var end)?Mathf.Clamp01((end-Time.unscaledTime)/Mathf.Max(.01f,Settings.GrabSeconds)):0;
        // Direct entry point for tools such as the VFX showcase, which have no level to raise events.
        public void Play(SandboxFeedbackEvent e) => Present(e);
        void Present(SandboxFeedbackEvent e)
        {
            if(!isActiveAndEnabled || !Settings.Enabled) return;
            if(e.Cue==SandboxSoundCue.Reject && !(presentation && presentation.Level && presentation.Level.Workshop)) return;
            switch(e.Cue) {
                case SandboxSoundCue.Grab:
                case SandboxSoundCue.GrabWheel:
                case SandboxSoundCue.GrabDrill:
                    if(e.Source is AssemblyPart part) grabs[part]=Time.unscaledTime+Settings.GrabSeconds;
                    break;
                case SandboxSoundCue.Reject:
                    presentation.Level.Workshop.PulseRejectedPreview(e.Source as AssemblyPart,Settings.RejectSeconds);
                    break;
                case SandboxSoundCue.Attach:
                    AddRing(e,Settings.Gold,Settings.AttachSeconds);
                    AddBurst(e,Settings.Gold,Settings.AttachSeconds*1.6f,Mathf.Clamp(Settings.AttachMotes,1,8),Style.Mote,.6f,true); break;
                case SandboxSoundCue.Detach:
                    AddRing(e,Settings.Gold,Settings.AttachSeconds,true);
                    AddBurst(e,Settings.Gold,Settings.AttachSeconds*1.4f,Mathf.Clamp(Settings.DetachMotes,0,8),Style.Mote,.25f); break;
                case SandboxSoundCue.WoodImpact:
                case SandboxSoundCue.MetalImpact:
                case SandboxSoundCue.RubberImpact:
                    if(e.Intensity<Settings.MinimumImpactIntensity) return;
                    bool metal=e.Material==SandboxSurfaceMaterial.Metal, rubber=e.Material==SandboxSurfaceMaterial.Rubber;
                    AddBurst(e,rubber?Settings.Rubber:metal?Settings.Metal:Settings.Wood,Settings.ImpactSeconds,
                        Mathf.Clamp(Mathf.RoundToInt(e.Intensity*10),3,10),Style.Impact,metal?.8f:rubber?.05f:.2f); break;
                case SandboxSoundCue.Drill:
                    if(e.Silent) AddBurst(e,Settings.Sandstone,.22f,Mathf.Clamp(Settings.DrillMotes,1,8),Style.Impact,.25f); break;
                case SandboxSoundCue.Splash:
                    AddBurst(e,Settings.Water,Settings.SplashSeconds,Mathf.Clamp(Settings.SplashDroplets,1,32),Style.Splash,.5f);
                    AddRing(e,Settings.Water,Settings.SplashSeconds,false,1.3f);
                    AddRing(e,Settings.Water,Settings.SplashSeconds,false,.85f,-.08f); break;
                case SandboxSoundCue.Arrive:
                    AddRing(e,Settings.Gold,Settings.AttachSeconds,false,1.15f);
                    AddBurst(e,Settings.Gold,Settings.AttachSeconds*1.6f,6,Style.Mote,.5f); break;
                case SandboxSoundCue.Success:
                    if(!celebration)celebration=gameObject.AddComponent<SandboxVictoryCelebration>();
                    celebration.Play(presentation?presentation.Level:null,e.Position);
                    // Shockwave, echo ring, then a rising fountain that outlives both.
                    AddRing(e,Settings.Gold,Settings.RewardSeconds,false,1.5f);
                    AddRing(e,Settings.Gold,Settings.RewardSeconds*1.5f,false,2.3f,-.12f);
                    AddBurst(e,Settings.Gold,Settings.RewardSeconds*3,Mathf.Clamp(Settings.SuccessSparkles,1,48),Style.Fountain,.7f,true); break;
                case SandboxSoundCue.Reveal:
                    AddBurst(e,Settings.Gold,Settings.RewardSeconds,Mathf.Clamp(Settings.RewardSparkles,1,32),Style.Mote,.7f,true); break;
                case SandboxSoundCue.Claim:
                    AddRing(e,Settings.Gold,Settings.RewardSeconds,true,1.4f);
                    AddBurst(e,Settings.Gold,Settings.RewardSeconds*1.5f,Mathf.Clamp(Settings.RewardSparkles,1,32),Style.Mote,.7f,true); break;
                default: return;
            }
            PresentedCount++;
        }
        // count is the number of motes; a flash adds one large soft particle at the origin.
        void AddBurst(SandboxFeedbackEvent e,Color color,float duration,int count,Style style,float heat,bool flash=false)
        {
            if(count<=0) return;
            if(flash) count++;
            count=Mathf.Min(count,Mathf.Clamp(Settings.MaxParticles,0,64)-ActiveParticles,SandboxFragments.Budget-SandboxFragments.ActiveCount);
            if(count<=0 || bursts.Count>=16) return;
            var fragments=new SandboxFragments(transform,count,true);
            bursts.Add(new Burst {Fragments=fragments,Origin=e.Position,Normal=e.Normal,Rotation=Quaternion.FromToRotation(Vector3.up,e.Normal),
                Duration=Mathf.Max(.01f,duration),Size=e.Size,Color=color,Style=style,Heat=heat,Flash=flash,Seed=seed++,Source=e.Source});
            ActiveParticles+=fragments.Points.Length;
        }
        void AddRing(SandboxFeedbackEvent e,Color color,float duration,bool contract=false,float radius=1,float age=0)
        {
            Ring available=null;
            foreach(var ring in rings) if(!ring.Active) {available=ring;break;}
            if(available==null) {
                if(rings.Count>=Mathf.Clamp(Settings.MaxRings,0,8)) return;
                if(!ringMaterial) {
                    // Rings share the feedback glow but shape it across the line instead of around a sprite.
                    var shared=Resources.Load<Material>("SandboxFeedback");
                    ringMaterial=shared?new Material(shared):new Material(Resources.Load<Shader>("SandboxBoundary"));
                    ringMaterial.name="Sandbox feedback ring"; if(ringMaterial.HasProperty("_Line")) ringMaterial.SetFloat("_Line",1);
                }
                var go=new GameObject("Sandbox feedback ring"); go.transform.SetParent(transform,false);
                var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=ringMaterial; line.loop=true; line.positionCount=32; line.useWorldSpace=true;
                line.numCornerVertices=2; line.textureMode=LineTextureMode.Stretch;
                line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false;
                available=new Ring {Line=line}; rings.Add(available);
            }
            available.Active=true; available.Line.enabled=age>=0; available.Age=age; available.Duration=Mathf.Max(.01f,duration);
            available.Origin=e.Position+e.Normal*Mathf.Max(.0005f,e.Size*.008f); available.Rotation=Quaternion.FromToRotation(Vector3.up,e.Normal);
            available.Radius=e.Size*Settings.RingRadiusInPartSizes*radius; available.Contract=contract; available.Color=color;
            available.Source=e.Source;
        }
        // Stable per-particle variation: the same particle keeps its direction and speed for its whole life.
        static float Hash(int index,int seed,int channel) => Mathf.Repeat(Mathf.Sin(index*12.9898f+seed*78.233f+channel*37.719f)*43758.5453f,1);
        void LateUpdate() => Advance(Time.unscaledDeltaTime);
        // Public so the VFX showcase can step the effects in the editor, where LateUpdate does not run.
        public void Advance(float dt)
        {
            if(!Settings.Enabled || Time.timeScale<=0) {Clear();return;}
            for(int b=bursts.Count-1;b>=0;b--) {
                var burst=bursts[b]; burst.Age+=dt; float t=burst.Age/burst.Duration;
                if(t>=1) {ActiveParticles-=burst.Fragments.Points.Length;burst.Fragments.Release();bursts.RemoveAt(b);continue;}
                float k=1-t, ease=1-k*k*k, baseSize=burst.Size*Settings.ParticleSizeInPartSizes*GlowScale;
                int first=0;
                if(burst.Flash) {
                    float f=Mathf.Clamp01(burst.Age/Mathf.Min(burst.Duration,.16f));
                    var flash=Color.Lerp(Color.white,burst.Color,.35f); flash.a*=(1-f)*(1-f)*.85f;
                    burst.Fragments.Set(0,burst.Origin+burst.Normal*(burst.Size*.01f),burst.Size*Settings.RingRadiusInPartSizes*(1.4f+1.6f*f),flash);
                    first=1;
                }
                for(int i=first;i<burst.Fragments.Points.Length;i++) {
                    float r1=Hash(i,burst.Seed,1), r2=Hash(i,burst.Seed,2), r3=Hash(i,burst.Seed,3);
                    float angle=i*2.399963f+r1*.9f, radial, height, alpha, scale, life=t;
                    switch(burst.Style) {
                        case Style.Impact:
                            radial=(.10f+.22f*r2)*(1-k*k); height=Mathf.Max(0,(.5f+.7f*r3)*t-1.3f*t*t);
                            alpha=1-t*t; scale=(.5f+.7f*r2)*(1-.5f*t); break;
                        case Style.Splash:
                            radial=(.08f+.24f*r2)*ease; height=(1+1.8f*r3)*t*k;
                            alpha=1-t*t*t; scale=(.7f+.8f*r2)*(1-.35f*t); break;
                        case Style.Fountain:
                            float delay=r1*.4f; life=Mathf.Clamp01((t-delay)/(1-delay));
                            angle+=life*2.2f; radial=(.05f+.20f*r2)*Mathf.Sqrt(life); height=(.45f+.9f*r3)*(1-(1-life)*(1-life));
                            alpha=Mathf.Sin(life*Mathf.PI)*(.55f+.45f*Mathf.Sin(burst.Age*24+r1*6.283f)); scale=(.6f+.9f*r2)*(1-.4f*life); break;
                        default:
                            radial=(.10f+.14f*r2)*ease; height=(.06f+.20f*r3)*ease+.05f*t;
                            alpha=Mathf.Pow(k,1.5f)*(.6f+.4f*Mathf.Sin(burst.Age*28+r1*6.283f)); scale=(.6f+.8f*r2)*(1-.5f*t); break;
                    }
                    var local=new Vector3(Mathf.Cos(angle)*radial,height,Mathf.Sin(angle)*radial)*(burst.Size*Settings.Spread);
                    // Motes start white-hot and cool to their own colour.
                    var color=Color.Lerp(burst.Color,Color.white,burst.Heat*(1-life)*(1-life)); color.a*=Mathf.Clamp01(alpha);
                    burst.Fragments.Set(i,burst.Origin+burst.Rotation*local,baseSize*scale,color);
                }
                burst.Fragments.Draw();
            }
            foreach(var ring in rings) if(ring.Active) {
                ring.Age+=dt; float t=ring.Age/ring.Duration;
                if(t>=1) {ring.Active=false;ring.Line.enabled=false;continue;}
                ring.Line.enabled=t>=0; if(t<0)continue;
                float k=1-t;
                // Expanding rings snap outward and coast; contracting rings gather speed as they close.
                float radius=ring.Radius*(ring.Contract?1-.85f*t*t:.2f+.8f*(1-k*k*k));
                ring.Line.startWidth=ring.Line.endWidth=ring.Radius*Settings.RingWidthInRadii*(1-.55f*t);
                var color=ring.Color;color.a*=ring.Contract?Mathf.Clamp01(t*6)*(1-t*t):k*k;ring.Line.startColor=ring.Line.endColor=color;
                for(int i=0;i<32;i++) {float a=i*Mathf.PI/16;ring.Line.SetPosition(i,ring.Origin+ring.Rotation*new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}
            }
            expired.Clear();foreach(var item in grabs)if(!item.Key || Time.unscaledTime>=item.Value)expired.Add(item.Key);
            foreach(var part in expired)grabs.Remove(part);
        }
        public void ClearSource(Object source)
        {
            for(int i=bursts.Count-1;i>=0;i--) if(bursts[i].Source==source) {
                ActiveParticles-=bursts[i].Fragments.Points.Length;bursts[i].Fragments.Release();bursts.RemoveAt(i);
            }
            foreach(var ring in rings) if(ring.Source==source) {ring.Active=false;if(ring.Line)ring.Line.enabled=false;}
        }
        public void Clear()
        {
            if(celebration)celebration.Stop();
            ClearTransient();
        }
        // Transfer fragments reclaim the ordinary particle pool; the bounded celebration uses its own batches.
        void ClearTransient()
        {
            foreach(var burst in bursts)burst.Fragments.Release();bursts.Clear();ActiveParticles=0;
            foreach(var ring in rings) {ring.Active=false;if(ring.Line)ring.Line.enabled=false;}
            grabs.Clear();
            if(presentation && presentation.Level && presentation.Level.Workshop) presentation.Level.Workshop.ClearRejectedPreview();
        }
    }
}
