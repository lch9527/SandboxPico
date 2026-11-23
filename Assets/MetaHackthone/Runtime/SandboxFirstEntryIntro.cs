using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAssembly
{
    // First entry into Level 1. The room goes dark, a blue spark becomes the building core, and the core learns to
    // build: it pulls parts out of the dark, makes two cars that cannot work (a nonsense stack, then a near miss
    // with both wheels on one side), watches each fail on the real track without ever touching it,
    // then returns to the player, assembles the level's starter car and shows the rotation control.
    // The player's kit is hidden and untouched until the end, the tracked head is never moved, and everything the
    // intro makes lives under one root, so an interruption only has to destroy that root and undo the lock.
    [DisallowMultipleComponent]
    public sealed class SandboxFirstEntryIntro : MonoBehaviour
    {
        public const string SeenKey="Sandbox.Intro.Core.v1";
        // The words that hand the level over to the player at the end; also the name of the object that shows them.
        public const string TitleText="Your turn";
        public enum Beat { Waiting, Darken, Orb, CoreBirth, FirstCar, Launch, FirstRun, Return, SecondCar, SecondRun, Home, StarterCar, Turn, Title, Done }
        // How a run ended: the car stopped by itself, left the level's test bounds (the game's own failure), or
        // was still going when its time ran out.
        public enum RunEnd { None, Rest, Left, Timeout }
        // The longest each car is watched. Nothing stops a car but the track: a run normally ends sooner, once the
        // car has come to rest on its own.
        public static readonly float[] RunSeconds={6f,6f};
        // A car has stopped when its core has stayed under these speeds (core edges and radians a second) this long.
        const float RestSpeed=.2f, RestSpin=.35f, RestSeconds=.45f, MinRun=.7f;
        // This is a seated experience: the player takes in about 100 degrees without turning. Whatever flies near them
        // stays within this many degrees either side of straight ahead (the line from the player to the core), which
        // leaves room for its glow inside that cone.
        public const float ViewHalfAngle=40;
        // The closing flourish on the rotation disc: two full turns, quick, spinning up and settling.
        public const float TurnDegrees=720, TurnSeconds=1.8f;
        // The core builds on a beat, 180 to the minute: a pulse, three closing rings, then one part lands on every
        // beat after. PartFlight is how long each part is in the air, so flights overlap.
        public const float BuildBeat=60f/180, PartFlight=.6f;
        // The pull: how long the core's tether takes to reach out to a part before it starts to reel it in, and
        // its thickness at the core in part widths (thinner beside the player, thicker at the track to be seen there).
        public const float TetherReach=BuildBeat*.75f, NearTetherUnits=.14f, FarTetherUnits=.3f;
        // Parts left adrift in the dark for the spark to light up and push aside, and their size next to track-size parts.
        public const int AdriftParts=12;
        public const float AdriftScale=.7f;
        public static bool Playing { get; private set; }
        // Test fixtures set this so that levels load straight into gameplay.
        public static bool Suppressed;
        // Multiplies the intro's own clock (tests). The two physics runs always take real physics time.
        public static float Speed=1;
        public static SandboxFirstEntryIntro Current { get; private set; }
        public Beat Stage { get; private set; }
        public SandboxIntroCar[] Cars { get; }=new SandboxIntroCar[SandboxIntroCars.Count];
        public float[] RunDistance { get; }=new float[SandboxIntroCars.Count];
        public float[] RunTime { get; }=new float[SandboxIntroCars.Count];
        public RunEnd[] RunEnded { get; }=new RunEnd[SandboxIntroCars.Count];
        public int Failures { get; private set; }
        public SandboxIntroEffects Effects => fx;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetState() { Playing=false; Suppressed=false; Speed=1; Current=null; }

        sealed class Job { public float Start,Duration; public Action<float> Step; }
        WorkshopBootstrap workshop; DownhillController level;
        Camera camera; Transform view;
        bool xr, locked, restored;
        GameObject root, arena, building, title;
        bool arenaActive, buildingActive;
        Vector3 savedPosition; Quaternion savedRotation;
        readonly List<Behaviour> paused=new();
        readonly List<GameObject> hidden=new();
        readonly List<Job> jobs=new();
        readonly List<(SandboxReturnEffect effect,Action done)> transfers=new();
        AssemblyWorld world; SandboxIntroEffects fx;
        Vector3 eye, facing, home, focus, startUp, startSide; Quaternion homeRotation;
        float side, clock, delta;
        Transform kitFrame; SandboxIntroGhost homeCore; List<SandboxIntroGhost> kitGhosts;
        (AssemblyBody body,Vector3 position,Quaternion rotation)[] turned; Vector3 turnPivot, turnAxis;

        public static bool Eligible(bool campaign,bool design,SandboxCourseId course,bool seen) => campaign && !design && course==SandboxCourseId.BeautifulCorner && !seen;
        public static void TryBegin(WorkshopBootstrap owner)
        {
            var level=owner?owner.Downhill:null;
            if(Suppressed || Playing || !level || owner.GetComponent<SandboxFirstEntryIntro>()) return;
            if(!Eligible(owner.DownhillSettings.Campaign,DesignModeSession.Enabled,SandboxCampaignSession.Current,PlayerPrefs.GetInt(SeenKey,0)!=0)) return;
            // The intro ends by handing over the level's untouched starter car; without one there is nothing to hand over.
            if(!level.FactoryLevel || !level.FactoryLevel.Start || !level.ActiveLayout || !level.Core || !level.StarterCarBuilt) { Debug.LogWarning("Sandbox intro skipped: the Level 1 starter car is unavailable."); return; }
            var intro=owner.gameObject.AddComponent<SandboxFirstEntryIntro>(); intro.workshop=owner; intro.level=level;
            Playing=true; Current=intro; intro.StartCoroutine(intro.Play());
        }

        IEnumerator Play()
        {
            yield return null; // let the desktop camera and XR placement initialise first
            camera=Camera.main; if(!camera) { Abort("no camera"); yield break; }
            // In XR the level is aligned to the headset on its first tracked frame, and every position below comes from it.
            if(camera.stereoEnabled) { float limit=Time.unscaledTime+5; while(!workshop.AreaAligned && Time.unscaledTime<limit) yield return null; yield return null; }
            if(!Lock()) { Abort("the intro cars could not be built"); yield break; }
            yield return Sequence();
            PlayerPrefs.SetInt(SeenKey,1); PlayerPrefs.Save(); Stage=Beat.Done; Restore();
            // No post-intro palm guidance for the stationary menu.
            Destroy(this);
        }
        void Abort(string reason) { Debug.LogWarning("Sandbox intro skipped: "+reason+"."); Restore(); Destroy(this); }

        bool Lock()
        {
            locked=true; view=camera.transform; xr=camera.stereoEnabled; savedPosition=view.position; savedRotation=view.rotation;
            var layout=level.ActiveLayout; var start=level.FactoryLevel.Start;
            arena=workshop.Arena.gameObject; building=layout.BuildingRoot.gameObject; arenaActive=arena.activeSelf; buildingActive=building.activeSelf;
            foreach(var b in workshop.GetComponents<Behaviour>()) if(b!=this && b.enabled) { paused.Add(b); b.enabled=false; }
            root=new GameObject("Sandbox intro"); if(root.scene!=gameObject.scene) SceneManager.MoveGameObjectToScene(root,gameObject.scene);
            world=new GameObject("Intro cars").AddComponent<AssemblyWorld>(); world.transform.SetParent(root.transform,false);
            fx=new SandboxIntroEffects(root,camera,level);
            home=level.Core.MainBody.transform.position; homeRotation=level.Core.MainBody.transform.rotation;
            var player=layout.PlayerStartPose(workshop.DownhillSettings.PlayerDistance);
            eye=xr?view.position:player.position;
            // "In front" is toward the core from where the player stands, wherever their head happens to be turned.
            facing=Vector3.ProjectOnPlane(home-eye,Vector3.up).normalized;
            if(facing.sqrMagnitude<.5f) facing=Vector3.ProjectOnPlane(xr?view.forward:player.rotation*Vector3.forward,Vector3.up).normalized;
            if(facing.sqrMagnitude<.5f) facing=Vector3.forward;
            startUp=start.rotation*Vector3.up; startSide=start.rotation*Vector3.right; side=Vector3.Dot(eye-start.position,startSide)>=0?1:-1;
            focus=InView(0,-10,2);
            if(!xr) view.SetPositionAndRotation(eye,Quaternion.LookRotation(focus-eye,Vector3.up));
            // Copy the kit's looks while it is still on the bench, then put the real thing away until the end.
            kitFrame=Frame("Intro kit",Vector3.zero,Quaternion.identity);
            kitGhosts=Ghosts(kitFrame,level.Kit.Where(p=>p && p.gameObject.activeSelf)); homeCore=kitGhosts.First(g=>g.Part==level.Core);
            foreach(var part in workshop.World.Parts.Values) if(part && part.gameObject.activeSelf) { hidden.Add(part.gameObject); part.gameObject.SetActive(false); }
            arena.SetActive(false);
            try {
                // Both cars are built up front, out of sight above the track, so no beat depends on a build succeeding later.
                for(int i=0;i<Cars.Length;i++) {
                    Cars[i]=SandboxIntroCars.Build(world,level.TestScale,new Pose(start.position+Vector3.up*(3+2*i),start.rotation),i,side);
                    foreach(var part in Cars[i].Parts) SandboxIntroCars.Show(part,false);
                }
                world.EditingEnabled=false;
            } catch(Exception error) { Debug.LogError("Sandbox intro cars failed to build: "+error.Message); return false; }
            return true;
        }
        Transform Frame(string name,Vector3 position,Quaternion rotation)
        {
            var frame=new GameObject(name).transform; frame.SetParent(root.transform,false); frame.SetPositionAndRotation(position,rotation); return frame;
        }
        static List<SandboxIntroGhost> Ghosts(Transform frame,IEnumerable<AssemblyPart> parts)
        {
            var ghosts=new List<SandboxIntroGhost>();
            foreach(var part in parts) { var ghost=SandboxIntroGhost.Copy(part,frame); ghost.Root.gameObject.SetActive(false); ghosts.Add(ghost); }
            return ghosts;
        }

        // The intro's own clock: unscaled, clamped so a hitch cannot skip a beat, and faster under test.
        void Update()
        {
            if(!locked || restored) return;
            delta=Mathf.Min(Time.unscaledDeltaTime,.05f)*Mathf.Max(.01f,Speed); clock+=delta;
            for(int i=0;i<jobs.Count;i++) {
                var job=jobs[i]; float t=(clock-job.Start)/job.Duration; if(t<0) continue;
                job.Step(Mathf.Clamp01(t)); if(t>=1) { jobs.Remove(job); i--; }
            }
            for(int i=transfers.Count-1;i>=0;i--) {
                var item=transfers[i]; if(item.effect && !item.effect.Advance(delta)) continue;
                transfers.RemoveAt(i); item.done?.Invoke();
                if(item.effect) { item.effect.Restore(); Destroy(item.effect.gameObject); }
            }
            // A desktop viewer has no head to turn, so the camera looks where the eye is being led.
            if(!xr && camera && (focus-view.position).sqrMagnitude>.01f)
                view.rotation=Quaternion.Slerp(view.rotation,Quaternion.LookRotation(focus-view.position,Vector3.up),1-Mathf.Exp(-delta*2.5f));
        }
        void LateUpdate() { if(locked && !restored && fx!=null) fx.Tick(delta); }
        void Run(float duration,Action<float> step,float delay=0) => jobs.Add(new Job {Start=clock+delay,Duration=Mathf.Max(.0001f,duration),Step=step});
        void After(float delay,Action action) => Run(.0001f,t=>{ if(t>=1) action(); },delay);
        IEnumerator Wait(float seconds) { float end=clock+seconds; while(clock<end) yield return null; }
        IEnumerator WaitTransfers() { while(transfers.Count>0) yield return null; }
        SandboxReturnEffect Effect(string name,bool blue,float moteScale)
        {
            var effect=new GameObject(name).AddComponent<SandboxReturnEffect>(); effect.transform.SetParent(root.transform,false); effect.MoteScale=moteScale;
            // The core travels as its own blue; anything else moves in the game's gold, exactly as a launch does.
            if(blue) { effect.MoteColor=SandboxIntroEffects.Blue; effect.Tinted=true; effect.BodyColor=new Color(.10f,.56f,.76f); effect.EdgeColor=new Color(.75f,.95f,1f); }
            return effect;
        }

        // A point as the seated player sees it: degrees right of straight ahead, degrees above eye level, metres away.
        Vector3 InView(float azimuth,float elevation,float distance)
        {
            var flat=Quaternion.AngleAxis(azimuth,Vector3.up)*facing; float rise=elevation*Mathf.Deg2Rad;
            return eye+(flat*Mathf.Cos(rise)+Vector3.up*Mathf.Sin(rise))*distance;
        }
        // The pull, in the manner of a construction blueprint: a see-through copy of the build stands on the core,
        // then the core reaches into the dark for each part with a tether of light and reels it into its place
        // there, one per beat. The blueprint fades in over the first beats and is gone once the last part is home.
        void Pull(Vector3 centre,bool far,int count,IEnumerable<SandboxIntroGhost> parts)
        {
            fx.Play(SandboxSoundCue.Gather,centre,1,1,far); fx.Blueprint(parts);
            Run(BuildBeat*2,t=>fx.BlueprintAlpha=Mathf.SmoothStep(0,1,t));
            After(Landing(Mathf.Max(0,count-1))+.5f,fx.ClearBlueprint);
        }
        // When the part with this index lands, counted from the pull: the first one on the fourth beat.
        static float Landing(int index) => BuildBeat*(4+index);
        static float Hash(int index,int channel) => Mathf.Repeat(Mathf.Sin(index*12.9898f+channel*78.233f)*43758.5453f,1);
        // Flies a ghost in from a point in the dark along a curve, gathering speed, and lands it on its rest pose.
        // streak 0 leaves no glowing trail (a part that is meant to come out of the dark unannounced); bend is how
        // far the path swings sideways, as a fraction of its length.
        // core, when given, is the building core pulling the part in: its tether reaches out to the part where it
        // waits, the part lights up as it is caught, and the tether reels it in to its place in the blueprint.
        // tether is the tether's thickness at the core.
        void Attract(SandboxIntroGhost ghost,Vector3 from,float delay,float duration,float streak,Action landed,float bend=.3f,Vector3? core=null,float tether=0)
        {
            var frame=ghost.Root.parent; var start=frame.InverseTransformPoint(from); var end=ghost.Position; var up=frame.InverseTransformDirection(Vector3.up);
            var control=(start+end)*.5f+Vector3.Cross(end-start,up).normalized*((end-start).magnitude*bend)+up*((end-start).magnitude*bend*.6f);
            var tumble=ghost.Rotation*Quaternion.Euler(140,220,80);
            SandboxIntroTether line=null;
            if(core.HasValue) {
                // The tether reaches out into the dark; the part is caught and lights up just before it is pulled.
                float reach=Mathf.Min(TetherReach,delay);
                Run(reach,t=>{
                    if(!ghost.Root) return;
                    if(line==null) line=fx.Tether();
                    if(!ghost.Root.gameObject.activeSelf) { ghost.Root.gameObject.SetActive(true); ghost.Root.localPosition=start; ghost.Root.localRotation=tumble; ghost.Root.localScale=Vector3.one*.25f; }
                    if(t>=.6f) fx.Hold(ghost.Renderers);
                    line.Draw(core.Value,ghost.Root.position,Mathf.SmoothStep(0,1,t),tether,1,clock);
                },delay-reach);
            }
            Run(duration,t=>{
                if(!ghost.Root) return;
                if(!ghost.Root.gameObject.activeSelf) ghost.Root.gameObject.SetActive(true);
                float e=Mathf.Pow(t,1.7f), k=1-e;
                ghost.Root.localPosition=k*k*start+2*k*e*control+e*e*end;
                ghost.Root.localRotation=Quaternion.Slerp(tumble,ghost.Rotation,Mathf.SmoothStep(0,1,t));
                ghost.Root.localScale=Vector3.one*Mathf.Lerp(.25f,1,Mathf.Clamp01(t/.5f));
                if(core.HasValue) {
                    if(line==null) line=fx.Tether();
                    fx.Hold(ghost.Renderers);
                    line.Draw(core.Value,ghost.Root.position,1,tether,t>=1?0:1,clock);
                    if(t>=1) { fx.Release(ghost.Renderers); fx.FillSlot(ghost); fx.Cut(line); line=null; }
                }
                if(t>=1) landed?.Invoke(); else if(streak>0) fx.Trail(ghost.Root.position,streak);
            },delay);
        }
        void Snap(Vector3 position,Vector3 centre,float unit,bool far,int index)
        {
            var normal=position-centre; if(normal.sqrMagnitude<1e-8f) normal=Vector3.up;
            fx.Feedback(SandboxSoundCue.Attach,position,normal,unit*3.3f); fx.Play(SandboxSoundCue.Attach,position,1,1+.04f*index,far);
        }
        // A real physics run of one intro car from the level's start. The intro never touches the car while it
        // runs and never stops it: the run is over when the car has come to rest by itself (or, as in the game,
        // when it leaves the level's test bounds), and it stays in physics afterwards.
        IEnumerator RunCar(int index,float unit)
        {
            var car=Cars[index]; var core=car.Core.MainBody.Rigidbody; var from=core.position; var bounds=level.FactoryLevel.TestBounds;
            foreach(var body in car.Bodies) if(!body.GetComponent<SandboxIntroImpact>()) body.gameObject.AddComponent<SandboxIntroImpact>().Hit=fx.Knock;
            fx.Play(SandboxSoundCue.Go,from,1,1,true);
            SandboxIntroCars.Release(car.Parts); level.SetTestClock();
            float began=Time.time, still=0; var ended=RunEnd.None;
            while(ended==RunEnd.None) {
                yield return null;
                float age=Time.time-began;
                fx.Core=core.position; focus=core.position; RunDistance[index]=Vector3.Distance(from,core.position); RunTime[index]=age;
                still=core.linearVelocity.magnitude<RestSpeed*unit && core.angularVelocity.magnitude<RestSpin?still+Time.deltaTime:0;
                if(age>=MinRun && still>=RestSeconds) ended=RunEnd.Rest;
                else if(bounds && !SandboxFactoryLevel.Contains(bounds,core.position)) ended=RunEnd.Left;
                else if(age>=RunSeconds[index]) ended=RunEnd.Timeout;
            }
            RunEnded[index]=ended; Failures++;
            // A car that has left the level is held where it went out, as the game does; otherwise it would go on
            // falling through the room below the level.
            if(ended==RunEnd.Left) SandboxIntroCars.Freeze(car.Parts);
            fx.Play(SandboxSoundCue.Failure,core.position,1,1,true);
        }
        // The core lets go. Nothing pushes what it leaves behind: the parts stay where the car stopped, settle
        // under their own weight and burn away.
        void Abandon(SandboxIntroCar car,float unit)
        {
            var core=car.Core.MainBody.Rigidbody.position; var dropped=SandboxIntroCars.DetachCore(world,car);
            fx.Core=core; fx.Play(SandboxSoundCue.Detach,core,1,1,true); fx.Feedback(SandboxSoundCue.Detach,core,startUp,unit*3.3f);
            After(.6f,()=>{
                foreach(var part in dropped) {
                    if(!part || !part.gameObject.activeSelf) continue;
                    var gone=part; var effect=Effect("Intro leftover "+part.Kind,false,4); effect.Vanish=true; effect.VanishRise=.3f;
                    effect.Initialize(part,root.transform,part.MainBody.Rigidbody.position,part.MainBody.Rigidbody.rotation,.25f,.4f,.2f,40);
                    transfers.Add((effect,()=>{ if(gone) gone.gameObject.SetActive(false); }));
                }
            });
        }
        void ApplyTurn(float angle)
        {
            if(turned==null) return;
            var turn=Quaternion.AngleAxis(angle,turnAxis);
            foreach(var item in turned) {
                if(!item.body) continue;
                var position=angle==0?item.position:turnPivot+turn*(item.position-turnPivot); var rotation=angle==0?item.rotation:turn*item.rotation;
                item.body.Rigidbody.position=position; item.body.Rigidbody.rotation=rotation; item.body.transform.SetPositionAndRotation(position,rotation);
            }
            Physics.SyncTransforms();
            if(level && level.Presentation && level.Core) level.Presentation.FollowCore(level.Core);
        }

        IEnumerator Sequence()
        {
            var up=Vector3.up; var start=level.FactoryLevel.Start;
            float near=level.Core.GeometryScale*BuildingGeometry.Unit, far=level.TestScale*BuildingGeometry.Unit; // a core's edge: beside the player, on the track

            // 1. The room goes dark.
            Stage=Beat.Darken;
            Run(2,t=>fx.SetRoom(Mathf.Lerp(1,SandboxIntroEffects.Dark,Mathf.SmoothStep(0,1,t))));
            yield return Wait(1.5f);

            // 2. A blue spark loops across the player's view, drawing in, and settles where the core belongs.
            // It never leaves the cone a seated player can see without turning their head.
            Stage=Beat.Orb;
            var offset=home-eye; float endDistance=offset.magnitude, endElevation=Mathf.Asin(Mathf.Clamp(offset.y/Mathf.Max(.001f,endDistance),-1,1))*Mathf.Rad2Deg;
            Vector3 OrbAt(float s)
            {
                // One and a half shrinking loops around the line of sight: wide and far at first, closing on the core.
                float e=1-Mathf.Pow(1-s,1.3f), wide=1-e, phase=Mathf.PI*(1+3*e);
                return InView(ViewHalfAngle*wide*Mathf.Cos(phase),Mathf.Lerp(10,endElevation,e)+13*wide*Mathf.Sin(phase),Mathf.Lerp(2.6f,endDistance,e));
            }
            // Parts hang adrift in the dark along its way. Unlit they cannot be seen at all: the spark's light finds
            // them as it passes, and its wake pushes aside the ones it flies straight through. They are the parts
            // the core will later call out of that darkness.
            var adrift=Frame("Intro parts adrift",Vector3.zero,Quaternion.identity);
            var kinds=Cars[0].Attracted.Concat(Cars[1].Attracted).ToArray();
            for(int i=0;i<AdriftParts;i++) {
                var on=OrbAt(Mathf.Lerp(.05f,.66f,i/(AdriftParts-1f))); var away=(on-eye).normalized; var across=Vector3.Cross(up,away).normalized;
                // Every third one sits right in the spark's path; the rest hang a little behind it and to the side.
                var place=i%3==0?on+away*.04f+up*(.06f*(Hash(i,1)-.5f)):on+away*(.18f+.3f*Hash(i,1))+up*(.34f*(Hash(i,2)-.5f))+across*(.3f*(Hash(i,3)-.5f));
                var drifter=SandboxIntroGhost.Copy(kinds[i%kinds.Length],adrift);
                drifter.Root.SetPositionAndRotation(place,Quaternion.Euler(Hash(i,4)*360,Hash(i,5)*360,Hash(i,6)*360)); drifter.Root.localScale=Vector3.one*AdriftScale;
                fx.Darken(drifter.Renderers); fx.AddDrifter(drifter.Root,i);
            }
            fx.Orb=OrbAt(0); fx.Light=fx.Orb; fx.LightReach=1; fx.FogSize=.9f; fx.Play(SandboxSoundCue.Reveal,fx.Orb);
            var whoosh=fx.Play(SandboxSoundCue.Travel,fx.Orb,1,.8f);
            Run(.7f,t=>{ fx.OrbGlow=t; fx.Fog=t; fx.LightStrength=t; });
            // The desktop review camera holds straight ahead here, as a seated player's head would: the spark has to
            // stay in frame on its own.
            focus=InView(0,-10,2);
            Run(7,t=>{ fx.Orb=OrbAt(t); fx.Light=fx.Orb; fx.FogSize=Mathf.Lerp(.9f,.34f,t); fx.Trail(fx.Orb,.07f); if(whoosh) whoosh.transform.position=fx.Orb; });
            yield return Wait(3.5f); whoosh=fx.Play(SandboxSoundCue.Travel,fx.Orb,1,1); yield return Wait(3.5f);

            // 3. The core swells out of the spark, overshoots and settles; the fog draws in and clears.
            Stage=Beat.CoreBirth; focus=home;
            fx.Core=home; fx.CoreSize=near; fx.Play(SandboxSoundCue.Arrive,home); fx.Wave(home,up,.22f);
            homeCore.Root.localScale=Vector3.zero; homeCore.Root.gameObject.SetActive(true);
            // The light now belongs to the core, and reaches less far: the dark closes in behind it.
            fx.Light=home; Run(1f,t=>fx.LightReach=Mathf.Lerp(1,.75f,t));
            Run(.5f,t=>fx.FogSize=Mathf.Lerp(.34f,.14f,t*t));
            Run(1f,t=>{
                float s=t<.45f?Mathf.Lerp(0,1.45f,1-Mathf.Pow(1-t/.45f,3)):t<.75f?Mathf.Lerp(1.45f,.9f,Mathf.SmoothStep(0,1,(t-.45f)/.3f)):Mathf.Lerp(.9f,1,Mathf.SmoothStep(0,1,(t-.75f)/.25f));
                homeCore.Root.localScale=Vector3.one*s;
            });
            Run(1.4f,t=>{ fx.Fog=1-t; fx.OrbGlow=1-Mathf.Clamp01(t*1.6f); fx.CoreGlow=Mathf.Clamp01(t*1.6f); },.4f);
            yield return Wait(2);

            // 4. The core pulls a short block, a long block and two wheels out of the dark and stacks them into
            // something that makes no sense at all: car one.
            Stage=Beat.FirstCar;
            var first=Cars[0]; var firstStart=SandboxIntroCars.PlaceAtStart(level,first);
            var carFrame=Frame("Intro first car",firstStart.position,firstStart.rotation); var firstGhosts=Ghosts(carFrame,first.Attracted);
            carFrame.SetPositionAndRotation(home,homeRotation); carFrame.localScale=Vector3.one*(near/far);
            // The disc faces the player, centred on the core, and stays well short of their head.
            Pull(home,false,firstGhosts.Count,firstGhosts);
            for(int i=0;i<firstGhosts.Count;i++) {
                var ghost=firstGhosts[i]; int k=i;
                // Out of the dark behind the core: further along the player's line of sight, close to it, never in
                // from the edge of the view. Each part is unseen until the core's light reaches it, and takes its
                // own colours as it lands.
                var from=InView(-12+8*i,endElevation+6+5*(i%2),endDistance+.9f+.12f*i);
                fx.Darken(ghost.Renderers);
                Attract(ghost,from,Landing(i)-PartFlight,PartFlight,0,()=>{ fx.Undarken(ghost.Renderers); Snap(ghost.Root.position,home,near,false,k); },.1f,home,near*NearTetherUnits);
            }
            yield return Wait(Landing(firstGhosts.Count)+.3f);

            // 5. The track comes out of the darkness and the car is sent to its start, as a launch sends it.
            Stage=Beat.Launch;
            fx.ClearDrifters(); Run(.5f,t=>fx.LightStrength=1-t);
            building.SetActive(false); arena.SetActive(arenaActive); if(level.Course) level.Course.ConfigurePhysics();
            fx.DimTrack(arena.transform); fx.SetTrack(0);
            Run(1.4f,t=>{ fx.SetTrack(Mathf.SmoothStep(0,1,t)); if(t>=1) fx.RestoreTrack(); });
            focus=firstStart.position; fx.Beacon=start.position; Run(.6f,t=>fx.BeaconGlow=t);
            fx.Play(SandboxSoundCue.Reveal,start.position,1,1,true); fx.Wave(start.position,startUp,.9f);
            yield return Wait(1.3f);
            fx.Play(SandboxSoundCue.Gather,home); Run(.3f,t=>fx.CoreGlow=1-t);
            var sending=new List<SandboxIntroGhost> {homeCore}; sending.AddRange(firstGhosts);
            foreach(var ghost in sending) {
                var source=ghost; var part=ghost==homeCore?first.Core:ghost.Part; var body=part.MainBody.transform;
                var effect=Effect("Intro launch "+part.Kind,false,1);
                effect.Initialize(ghost.Renderers,SandboxIntroGhost.Visuals(part),ghost.Root.position,ghost.Root.rotation,root.transform,body.position,body.rotation,.2f,1.25f,.3f,90);
                // Motes this small vanish at seven metres; they grow as they leave the player.
                Run(1.45f,t=>{ if(effect) effect.MoteScale=Mathf.Lerp(1,4.5f,t); });
                transfers.Add((effect,()=>{ source.Root.gameObject.SetActive(false); SandboxIntroCars.Show(part,true); }));
            }
            var travel=fx.Play(SandboxSoundCue.Travel,home,1,.7f,true);
            Run(1.45f,t=>{ if(travel) travel.transform.position=Vector3.Lerp(home,firstStart.position,Mathf.SmoothStep(0,1,t)); });
            yield return WaitTransfers();
            fx.Core=firstStart.position; fx.CoreSize=far; fx.CoreGlow=1; Run(.6f,t=>fx.BeaconGlow=1-t);
            fx.Play(SandboxSoundCue.Arrive,firstStart.position,1,1,true); fx.Feedback(SandboxSoundCue.Arrive,firstStart.position,startUp,far*4);

            // 6. It topples over and slides until it stops.
            Stage=Beat.FirstRun; yield return Wait(.4f);
            yield return RunCar(0,far);

            // 7. The core lets go of what it built, where it lies, and returns to the start on its own.
            Stage=Beat.Return; yield return Wait(.5f);
            Abandon(first,far); After(.6f,()=>focus=firstStart.position);
            var second=Cars[1]; Pose secondStart=default;
            var back=Effect("Intro core return",true,4.5f);
            back.Initialize(first.Core,root.transform,firstStart.position,firstStart.rotation,.15f,.75f,.25f,96);
            transfers.Add((back,()=>{
                first.Core.gameObject.SetActive(false);
                secondStart=SandboxIntroCars.PlaceAtStart(level,second); SandboxIntroCars.Show(second.Core,true);
                fx.Core=secondStart.position; fx.Play(SandboxSoundCue.Arrive,secondStart.position,1,1,true); fx.Wave(secondStart.position,startUp,.5f);
            }));
            Run(1.15f,t=>fx.Core=Vector3.Lerp(fx.Core,firstStart.position,t));
            yield return WaitTransfers(); level.RestoreClock();

            // 8. It tries again, at the start this time, and gets close: a long block and two wheels, like the
            // starter car, but with both wheels on the same side.
            Stage=Beat.SecondCar; yield return Wait(.3f);
            var secondFrame=Frame("Intro second car",secondStart.position,secondStart.rotation); var secondGhosts=Ghosts(secondFrame,second.Attracted);
            Pull(secondStart.position,true,secondGhosts.Count,secondGhosts);
            var toward=(eye-secondStart.position).normalized;
            for(int i=0;i<secondGhosts.Count;i++) {
                var ghost=secondGhosts[i]; int k=i;
                var from=secondStart.position+toward*(1.6f+.3f*i)+startSide*((i%2==0?1:-1)*(.8f+.2f*i))+up*(.9f+.25f*i);
                Attract(ghost,from,Landing(i)-PartFlight,PartFlight,.22f,()=>{ var landed=ghost.Root.position; ghost.Root.gameObject.SetActive(false); SandboxIntroCars.Show(ghost.Part,true); Snap(landed,secondStart.position,far,true,k); },.3f,secondStart.position,far*FarTetherUnits);
            }
            yield return Wait(Landing(secondGhosts.Count)+.3f);

            // 9. It rolls this time, on one side only, and fails too.
            Stage=Beat.SecondRun;
            yield return RunCar(1,far);

            // 10. The core comes back to the player; the building area appears and the room returns.
            Stage=Beat.Home; yield return Wait(.5f);
            var stopped=second.Core.MainBody.Rigidbody.position;
            Abandon(second,far);
            var homing=Effect("Intro core home",true,4.5f);
            homing.Initialize(second.Core,level.Core,root.transform,home,homeRotation,.2f,1.5f,.35f,128);
            Run(2.05f,t=>{ if(homing) homing.MoteScale=Mathf.Lerp(4.5f,1,t*t); });
            transfers.Add((homing,()=>{
                second.Core.gameObject.SetActive(false);
                homeCore.Root.localScale=Vector3.one; homeCore.Root.gameObject.SetActive(true);
                fx.Core=home; fx.CoreSize=near; fx.CoreGlow=1; fx.Play(SandboxSoundCue.Arrive,home); fx.Wave(home,up,.24f);
            }));
            Run(.3f,t=>fx.CoreGlow=1-t);
            var returning=fx.Play(SandboxSoundCue.Travel,stopped,1,.7f,true);
            Run(1.7f,t=>{ if(returning) returning.transform.position=Vector3.Lerp(stopped,home,Mathf.SmoothStep(0,1,t)); },.2f);
            After(.8f,()=>focus=home);
            After(1f,()=>building.SetActive(buildingActive));
            Run(2.2f,t=>fx.SetRoom(Mathf.Lerp(SandboxIntroEffects.Dark,1,Mathf.SmoothStep(0,1,t))),.5f);
            yield return WaitTransfers(); level.RestoreClock();
            yield return Wait(.9f);

            // 11. One last pull, and this time it is the starter car: the kit the player is about to finish.
            Stage=Beat.StarterCar;
            var joined=workshop.World.GetConnectedParts(level.Core.InstanceId);
            var attracted=kitGhosts.Where(g=>g!=homeCore && joined.Contains(g.Part)).ToList(); var loose=kitGhosts.Where(g=>g!=homeCore && !joined.Contains(g.Part)).ToList();
            Pull(home,false,attracted.Count,attracted);
            for(int i=0;i<attracted.Count;i++) {
                var ghost=attracted[i]; int k=i;
                // From behind the core again, as with the first car; the room is lit now, so they are simply seen coming.
                Attract(ghost,InView(-10+10*i,endElevation+8,endDistance+.8f+.1f*i),Landing(i)-PartFlight,PartFlight,.05f,()=>Snap(ghost.Root.position,home,near,false,k),.1f,home,near*NearTetherUnits);
            }
            // The spare parts simply settle onto the bench, on the half-beats.
            for(int i=0;i<loose.Count;i++) { var spare=loose[i]; Attract(spare,kitFrame.TransformPoint(spare.Position)+up*.45f+facing*.25f,BuildBeat*(4+.5f*i),.7f,.04f,null); }
            yield return Wait(Mathf.Max(Landing(attracted.Count),BuildBeat*(4+.5f*loose.Count)+.7f)+.3f);
            // Swap the copies for the real kit, which never moved.
            foreach(var part in hidden) if(part) part.SetActive(true); hidden.Clear();
            foreach(var ghost in kitGhosts) if(ghost.Root) Destroy(ghost.Root.gameObject);
            Physics.SyncTransforms(); level.Presentation.FollowCore(level.Core);

            // 12. The rotation disc whirls the build twice around, in the game's own steps, and ends where it began.
            Stage=Beat.Turn; yield return Wait(.3f);
            turned=joined.SelectMany(p=>p.Bodies).Select(b=>(b,b.Rigidbody.position,b.Rigidbody.rotation)).ToArray();
            turnPivot=level.Core.MainBody.Rigidbody.position; turnAxis=level.Course.WorkbenchFrame.up;
            float step=Mathf.Max(.1f,level.WheelStepDegrees); int steps=Mathf.RoundToInt(TurnDegrees/step), last=0;
            Run(TurnSeconds,t=>{
                // Gentle at both ends, fast in the middle: it spins up, whirls and settles.
                float eased=t*t*t*(t*(6*t-15)+10);
                int index=t>=1?steps:Mathf.Min(steps,Mathf.FloorToInt(eased*TurnDegrees/step)); if(index==last) return;
                // At this speed a tick for every step would be a buzz; one for each eighth of a turn reads as a ratchet.
                if(index/9!=last/9) fx.Play(SandboxSoundCue.WheelTick,level.RotationWheel?level.RotationWheel.transform.position:home);
                last=index; ApplyTurn(index>=steps?0:index*step);
            });
            yield return Wait(TurnSeconds+.1f);
            ApplyTurn(0); turned=null;

            // 13. Over to the player.
            Stage=Beat.Title;
            Run(1,t=>fx.CoreGlow=1-t);
            // The title floats just above the car, turned to the player. Fixed to the head at a distance it ended up
            // behind the car the player is looking down at, half hidden by it.
            var titleAt=home+up*.13f;
            title=new GameObject(TitleText); title.transform.SetParent(root.transform,false); title.transform.SetPositionAndRotation(titleAt,Quaternion.LookRotation(titleAt-view.position,up));
            var text=title.AddComponent<TextMesh>(); text.text=TitleText; text.anchor=TextAnchor.MiddleCenter; text.alignment=TextAlignment.Center; text.fontSize=64; text.characterSize=.0075f; text.color=new Color(1,1,1,0);
            fx.Play(SandboxSoundCue.Reveal,titleAt);
            Run(2.4f,t=>{ if(text) text.color=new Color(1,1,1,Mathf.Clamp01(t*2.4f/.4f)*Mathf.Clamp01((1-t)*2.4f/.5f)); });
            yield return Wait(2.5f);
        }

        // Safe at any moment, any number of times. Leaves the level exactly as a fresh Level 1 start.
        void Restore()
        {
            if(restored) return; restored=true; Playing=false; if(Current==this) Current=null;
            if(!locked) return;
            if(level) level.RestoreClock();
            ApplyTurn(0); turned=null;
            fx?.Dispose();
            if(camera && !xr) view.SetPositionAndRotation(savedPosition,savedRotation);
            if(building) building.SetActive(buildingActive); if(arena) arena.SetActive(arenaActive);
            foreach(var part in hidden) if(part) part.SetActive(true); hidden.Clear();
            foreach(var b in paused) if(b) b.enabled=true; paused.Clear();
            if(title) Destroy(title);
            if(root) { root.SetActive(false); Destroy(root); }
            jobs.Clear(); transfers.Clear();
        }
        void OnDisable() { StopAllCoroutines(); Restore(); }
        void OnDestroy() { Restore(); }
    }
}
