using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Profiling;

namespace PhysicalAssembly
{
    // Heading/centering only. Wheel contacts determine pitch, roll and vertical motion.
    [DefaultExecutionOrder(-50)]
    public sealed class SandboxFactoryPathAssist : MonoBehaviour
    {
        public DownhillController Level;
        SandboxTrackGenerator track;
        int segment;
        AssemblyBody[] bodies;
        AssemblyBody[] wheels;
        readonly RaycastHit[] hits = new RaycastHit[32];
        readonly List<Collider> surfaces = new();
        readonly List<PuzzleSection> puzzleSections = new();
        static readonly ProfilerMarker GuideMarker = new("Sandbox.PathAssist");
        static readonly ProfilerMarker ClearanceMarker = new("Sandbox.PuzzleClearance");
        public bool Guiding { get; private set; }
        // 1 for a left/right symmetric car, falling to 0 as mass or wheel size becomes lopsided.
        public float Balance { get; private set; }
        // Time constant of the roll/yaw spin damping for a fully balanced car, in seconds.
        public const float StabilizeTime = .08f;
        // Tilt beyond which the car is left to recover through contacts alone.
        public const float StabilizeMinimumUp = .5f;
        // Pitch spin (rad/s) a balanced car may keep. Ramp transitions and normal bump hops stay below it.
        public const float PitchRateLimit = 4f;

        void OnEnable() { track=null; bodies=null; wheels=null; Guiding=false; Balance=0; surfaces.Clear(); puzzleSections.Clear(); }

        // Lateral symmetry about the core's forward axis: centre of mass offset relative to the
        // wheel track, and the difference in total wheel radius between the two sides.
        public static float MeasureBalance(AssemblyBody[] vehicle,Rigidbody core)
        {
            var right=core.rotation*Vector3.right; float mass=0,moment=0,halfTrack=0,left=0,rightSide=0;
            foreach(var body in vehicle) {
                var rb=body?body.Rigidbody:null; if(!rb) continue;
                float x=Vector3.Dot(rb.worldCenterOfMass-core.position,right);
                mass+=rb.mass; moment+=rb.mass*x;
                var wheel=body.GetComponent<PassiveWheel>(); if(!wheel) continue;
                halfTrack=Mathf.Max(halfTrack,Mathf.Abs(x));
                if(x>0) rightSide+=wheel.Radius; else left+=wheel.Radius;
            }
            if(mass<=0 || halfTrack<=1e-5f || left+rightSide<=0) return 0;
            float offset=Mathf.Abs(moment/mass)/halfTrack;           // 0 centred, 1 over one wheel line
            float wheels=Mathf.Abs(left-rightSide)/(left+rightSide);   // 0 matched, 1 one side only
            return Mathf.Clamp01(1-offset/.2f)*Mathf.Clamp01(1-wheels/.25f);
        }

        // Yaw rate (rad/s about world up) that follows the road's curvature at this velocity.
        public static float TrackYawRate(SandboxTrackGenerator track,int segment,Vector3 velocity)
        {
            var samples=track.Samples; int n=samples.Length; if(n<3) return 0;
            int a=Mathf.Clamp(segment-1,0,n-2), b=Mathf.Clamp(segment+1,0,n-2); if(a==b) return 0;
            Vector3 P(int i)=>track.transform.TransformPoint(samples[i]);
            var ta=Vector3.ProjectOnPlane(P(a+1)-P(a),Vector3.up); var tb=Vector3.ProjectOnPlane(P(b+1)-P(b),Vector3.up);
            float length=Vector3.Distance((P(a)+P(a+1))*.5f,(P(b)+P(b+1))*.5f);
            if(length<1e-5f || ta.sqrMagnitude<1e-10f || tb.sqrMagnitude<1e-10f) return 0;
            float curvature=Vector3.SignedAngle(ta,tb,Vector3.up)*Mathf.Deg2Rad/length;
            return curvature*Vector3.Dot(velocity,ta.normalized);
        }

        // Damps the assembly's yaw (beyond what the road's curve needs) and roll spin, and clips violent
        // pitch spin, as one rigid correction so joints are not strained. Rates only: no pose is set.
        // Scaled by balance, so a lopsided car still veers and tips physically.
        public static void Stabilize(AssemblyBody[] vehicle,Rigidbody core,float balance,float targetYawRate=0,bool supported=true)
        {
            if(balance<=0) return;
            var up=core.rotation*Vector3.up;
            if(Vector3.Dot(up,Vector3.up)<StabilizeMinimumUp) return;
            var forward=core.rotation*Vector3.forward;
            var spin=core.angularVelocity;
            // Yaw and roll are damped only while a wheel is on the road.
            var change=supported
                ? (Vector3.up*(Vector3.Dot(spin,Vector3.up)-targetYawRate)+forward*Vector3.Dot(spin,forward))*(balance*(1-Mathf.Exp(-Time.fixedDeltaTime/StabilizeTime)))
                : Vector3.zero;
            // Pitch stays free for ramps, bumps and unequal wheels; only a violent kick beyond that
            // (a wheel striking the roller) is clipped at once, on the road or in the air, so a
            // balanced car cannot flip into a wheelie.
            var side=core.rotation*Vector3.right; float pitch=Vector3.Dot(spin,side);
            change+=side*(Mathf.Sign(pitch)*Mathf.Max(0,Mathf.Abs(pitch)-PitchRateLimit)*balance);
            if(change.sqrMagnitude<1e-10f) return;
            Vector3 com=Vector3.zero; float mass=0;
            foreach(var body in vehicle) { var rb=body?body.Rigidbody:null; if(!rb || rb.isKinematic) continue; com+=rb.worldCenterOfMass*rb.mass; mass+=rb.mass; }
            if(mass<=0) return; com/=mass;
            foreach(var body in vehicle) {
                var rb=body?body.Rigidbody:null; if(!rb || rb.isKinematic) continue;
                rb.angularVelocity-=change;
                rb.linearVelocity-=Vector3.Cross(change,rb.worldCenterOfMass-com);
            }
        }

        void FixedUpdate()
        {
            using(GuideMarker.Auto()) Tick();
        }

        void Tick()
        {
            Guiding=false;
            if(!Level || Level.State!=DownhillState.Running || !Level.FactoryLevel ||
                !Level.FactoryLevel.CenterPathAssist || !Level.SimulationCore || !Level.SimulationWorld) return;
            if(bodies==null) {
                bodies=Level.SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies).ToArray();
                wheels=bodies.Where(b=>b.GetComponent<PassiveWheel>()).ToArray();
                Balance=MeasureBalance(bodies,Level.SimulationCore.MainBody.Rigidbody);
                foreach(var candidate in Level.FactoryLevel.Tracks)
                    if(candidate && candidate.Generated) surfaces.AddRange(candidate.Generated.GetComponentsInChildren<MeshCollider>()
                        .Where(c=>c.name=="Road"));
                foreach(var puzzle in Level.FactoryLevel.Puzzles) {
                    if(puzzle is SandboxDropBridge bridge && bridge.Deck) surfaces.AddRange(bridge.Deck.GetComponentsInChildren<Collider>());
                    if(puzzle.Track) puzzleSections.Add(new PuzzleSection(puzzle,Level.FactoryLevel.AssistPuzzleClearance));
                }
            }
            var core=Level.SimulationCore.MainBody.Rigidbody;
            if(core.isKinematic) return;
            Vector3 center=Vector3.zero, forward=Vector3.forward;
            float best=float.PositiveInfinity;
            var nearestTrack=track;
            int nearestSegment=segment;
            foreach(var candidate in Level.FactoryLevel.Tracks) {
                if(!candidate || candidate.Samples.Length<2 || (track && candidate!=track)) continue;
                int first=track?Mathf.Max(0,segment-24):0;
                int last=track?Mathf.Min(candidate.Samples.Length-2,segment+24):candidate.Samples.Length-2;
                for(int i=first;i<=last;i++) {
                    Vector3 a=candidate.transform.TransformPoint(candidate.Samples[i]);
                    Vector3 b=candidate.transform.TransformPoint(candidate.Samples[i+1]);
                    Vector3 delta=b-a;
                    float u=Mathf.Clamp01(Vector3.Dot(core.position-a,delta)/Mathf.Max(delta.sqrMagnitude,.000001f));
                    Vector3 point=a+u*delta;
                    float distance=(core.position-point).sqrMagnitude;
                    if(distance>=best) continue;
                    best=distance; center=point; forward=delta.normalized;
                    nearestTrack=candidate; nearestSegment=i;
                }
            }
            track=nearestTrack; segment=nearestSegment;
            if(!track || float.IsInfinity(best)) return;
            float scale=track.transform.lossyScale.x;
            var right=Vector3.Cross(Vector3.up,forward).normalized;
            // Never snap a fallen/off-course car back onto the road.
            if(Mathf.Abs(Vector3.Dot(core.position-center,right))>track.DefaultWidth*scale) return;
            if(core.position.y<center.y-.5f*scale) return;
            int supported=0;
            foreach(var wheel in wheels) {
                if(!wheel || !wheel.Rigidbody) continue;
                var bounds=new Bounds(wheel.Rigidbody.worldCenterOfMass,Vector3.zero);
                foreach(var collider in wheel.Colliders) if(collider && collider.enabled) bounds.Encapsulate(collider.bounds);
                float reach=bounds.extents.y+.12f*scale;
                int count=Physics.RaycastNonAlloc(bounds.center,Vector3.down,hits,reach,~0,QueryTriggerInteraction.Ignore);
                bool support=false;
                for(int i=0;i<count;i++) if(hits[i].normal.y>.3f && surfaces.Contains(hits[i].collider)) { support=true; break; }
                if(support) supported++;
            }
            // Rate damping only: no pose is set, so contacts and pitch stay physical.
            if(!(Level.FactoryLevel.LimitedPathAssist && InPuzzleSection(track,bodies)))
                Stabilize(bodies,core,Balance,TrackYawRate(track,segment,core.linearVelocity),supported>0);
            // Never correct an airborne assembly. Even rotating about the core moves
            // its wheels vertically and can drive them into the road at the next step.
            if(supported<2) return;
            if(Level.FactoryLevel.LimitedPathAssist) {
                if(InPuzzleSection(track,bodies)) return;
                ApplyLimitedGuide(bodies,core,center,forward);
            } else ApplyHorizontalGuide(bodies,core,center,forward);
            Guiding=true;
        }

        bool InPuzzleSection(SandboxTrackGenerator track,AssemblyBody[] vehicle)
        {
            using(ClearanceMarker.Auto()) {
                foreach(var section in puzzleSections)
                    if(section.Puzzle && section.Puzzle.isActiveAndEnabled && section.Puzzle.Track==track && section.Intersects(vehicle)) return true;
                return false;
            }
        }

        // Cache only the segments within each puzzle's clearance region. The old
        // query rescanned/transformed the complete track for every vehicle collider
        // at every physics step. Retry rebuilds this cache after puzzles are reset.
        internal sealed class PuzzleSection
        {
            public readonly SandboxPuzzle Puzzle;
            readonly float clearance;
            readonly Vector3[] localA,localB,worldA,directions;
            readonly float[] lengths;
            Matrix4x4 matrix;
            bool transformed;
            public PuzzleSection(SandboxPuzzle puzzle,float margin)
            {
                Puzzle=puzzle; clearance=margin;
                float start=puzzle.Distance-puzzle.Span*.5f-margin;
                float end=puzzle.Distance+puzzle.Span*.5f+margin;
                if(puzzle is SandboxDropBridge bridge) start=Mathf.Min(start,bridge.TargetTrackDistance-margin);
                var a=new List<Vector3>(); var b=new List<Vector3>(); float distance=0;
                var samples=puzzle.Track.Samples;
                for(int i=0;i<samples.Length-1;i++) {
                    float length=Vector3.Distance(samples[i],samples[i+1]);
                    if(distance+length>=start && distance<=end) { a.Add(samples[i]); b.Add(samples[i+1]); }
                    distance+=length;
                    if(distance>end) break;
                }
                localA=a.ToArray(); localB=b.ToArray(); worldA=new Vector3[a.Count]; directions=new Vector3[a.Count]; lengths=new float[a.Count];
            }
            public bool Intersects(AssemblyBody[] vehicle)
            {
                var track=Puzzle.Track; var current=track.transform.localToWorldMatrix;
                if(!transformed || matrix!=current) {
                    matrix=current; transformed=true;
                    for(int i=0;i<worldA.Length;i++) {
                        worldA[i]=matrix.MultiplyPoint3x4(localA[i]);
                        var delta=matrix.MultiplyPoint3x4(localB[i])-worldA[i];
                        lengths[i]=delta.magnitude; directions[i]=delta.normalized;
                    }
                }
                foreach(var body in vehicle) if(body) foreach(var collider in body.Colliders) {
                    if(!collider || !collider.enabled) continue;
                    var bounds=collider.bounds; bounds.Expand(clearance*track.transform.lossyScale.x);
                    for(int i=0;i<worldA.Length;i++)
                        if(bounds.Contains(worldA[i]) || (bounds.IntersectRay(new Ray(worldA[i],directions[i]),out var hit) && hit<=lengths[i])) return true;
                }
                return false;
            }
        }

        public static void ApplyLimitedGuide(AssemblyBody[] bodies,Rigidbody core,Vector3 center,Vector3 tangent)
        {
            // Tipped vehicles must recover through contacts, never through assist.
            if(Vector3.Dot(core.rotation*Vector3.up,Vector3.up)<.75f) return;
            var forward=Vector3.ProjectOnPlane(tangent,Vector3.up).normalized;
            var heading=Vector3.ProjectOnPlane(core.rotation*Vector3.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.5f || heading.sqrMagnitude<.5f) return;
            var yaw=Quaternion.AngleAxis(Vector3.SignedAngle(heading,forward,Vector3.up),Vector3.up);
            var right=Vector3.Cross(Vector3.up,forward);
            var pivot=core.position;
            var shift=right*Vector3.Dot(center-pivot,right);
            foreach(var body in bodies) {
                var rb=body ? body.Rigidbody : null;
                if(!rb || rb.isKinematic) continue;
                rb.position=pivot+yaw*(rb.position-pivot)+shift;
                rb.rotation=yaw*rb.rotation;
                var velocity=yaw*rb.linearVelocity;
                rb.linearVelocity=velocity-right*Vector3.Dot(velocity,right);
                rb.angularVelocity=yaw*rb.angularVelocity;
            }
        }

        public static void ApplyHorizontalGuide(AssemblyBody[] bodies,Rigidbody core,Vector3 center,Vector3 tangent,bool supported=true)
        {
            // Compatibility for older authored courses: never reset pitch/roll.
            // Unequal wheel radii need a chassis pitch relative to the road; forcing
            // it to the tangent repeatedly penetrates tires and injects contact energy.
            if(supported) ApplyLimitedGuide(bodies,core,center,tangent);
        }
    }
}
