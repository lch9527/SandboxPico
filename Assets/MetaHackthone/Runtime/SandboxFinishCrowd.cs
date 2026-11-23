using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalAssembly
{
    // Flat cut-out spectators at a level's finish. While the player builds they mill about: strolling,
    // chatting, waving, sitting down, stretching. When a run starts they hurry back to their places to
    // watch, and groan if it fails. When the car wins they swarm it: one helper leaves with every part,
    // and whoever is left lifts the core and cheers.
    // Keep the marker under the level's Track so it moves with the course. Its local XZ plane is the
    // ground the people stand on, and its blue axis is the way the front row faces.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SandboxFinishCrowd : MonoBehaviour
    {
        public const string MarkerName="Finish Crowd";
        public const int MaxCount=24;
        [Range(0,MaxCount), Tooltip("People waiting at the finish. One leaves with each part of the winning car.")]
        public int Count=10;
        [Tooltip("Standing area on this marker's ground: width (red axis) and depth (blue axis), in the marker's own units. People wander inside it while you build.")]
        public Vector2 Area=new(5,2);
        [Min(.05f), Tooltip("Height of an adult, in the marker's own units.")]
        public float PersonHeight=1;
        [Min(0), Tooltip("People who never leave with a part, so somebody is always left to carry the core.")]
        public int AlwaysStay=2;
        [Range(0,1), Tooltip("How busy the crowd is while you build: 0 mostly stands about, 1 keeps people strolling, chatting and hopping.")]
        public float Liveliness=.7f;
        [Tooltip("Changes who stands where and what each person does.")]
        public int Seed=7;
        [Tooltip("Seconds to run to the car, hold its parts, lift the core, keep cheering, and walk back.")]
        public float RushSeconds=.9f, HoldSeconds=1f, LiftSeconds=1.1f, CheerSeconds=6f, ReturnSeconds=1.2f;

        public enum CrowdPhase { Waiting, Rush, Hold, Lift, Cheer, Return }
        // While waiting: milling about as you build, hurrying back when you press START, watching the run,
        // or groaning when it fails.
        public enum CrowdMood { Idle, Ready, Watch, Groan }
        public CrowdPhase Phase { get; private set; }
        public CrowdMood Mood { get; private set; }
        public int Total { get { EnsurePeople(); return people.Count; } }
        // People still on screen: everyone except the helpers who left with a part.
        public int Remaining { get { EnsurePeople(); int n=0; foreach(var p in people) if(!p.Away) n++; return n; } }
        // Time the winning car should rest before its parts dissolve, so the crowd reaches it first.
        public float SwarmSeconds => RushSeconds+HoldSeconds;
        // Helpers that leave with a part: one per part, but never the people who always stay.
        public static int HelperCount(int parts,int crowd,int alwaysStay) => Mathf.Clamp(parts,0,Mathf.Max(0,crowd-Mathf.Min(alwaysStay,crowd)));
        // Where person i is now, and where their own place is, in marker units.
        public Vector3 PersonPosition(int index) { EnsurePeople(); return people[index].Position; }
        public Vector3 StandingSpot(int index) { EnsurePeople(); return people[index].Home; }

        enum Act { Stand, Stroll, Look, Point, Wave, Chat, Stretch, Sit, Hop, TapFoot, Jacks, Groan, Shrug }
        enum Joy { Jump, Flag, Clap, Pump }
        enum Role { Crowd, Helper, Carrier }
        // A pose in the figure's own flat space: x towards the way it faces, y up, 1 = an adult's height.
        struct Sketch
        {
            public Vector2 FootB, FootF, Hip, Chest, Head, ElbowB, HandB, ElbowF, HandF;
            public float Lift;
            public static Sketch Lerp(in Sketch a,in Sketch b,float t) => new Sketch {
                FootB=Vector2.Lerp(a.FootB,b.FootB,t), FootF=Vector2.Lerp(a.FootF,b.FootF,t), Hip=Vector2.Lerp(a.Hip,b.Hip,t),
                Chest=Vector2.Lerp(a.Chest,b.Chest,t), Head=Vector2.Lerp(a.Head,b.Head,t), ElbowB=Vector2.Lerp(a.ElbowB,b.ElbowB,t),
                HandB=Vector2.Lerp(a.HandB,b.HandB,t), ElbowF=Vector2.Lerp(a.ElbowF,b.ElbowF,t), HandF=Vector2.Lerp(a.HandF,b.HandF,t),
                Lift=Mathf.Lerp(a.Lift,b.Lift,t) };
        }
        sealed class Person
        {
            public Vector3 Home, Position, From, To, Carry, Glance;
            public float Size, Offset, MoveAge, MoveSeconds, Gone, Reach=.75f, Facing=1, Face=1, Appear=1;
            public float ActAge, ActSeconds, Stride, StrideLength=.3f, FromLift, ToLift;
            public bool Kid, Away, MoveActive, Posed, Faced, Grounded, Running, Talker, Joining;
            public int Hops;
            public Act Act; public Joy Joy; public Role Role; public Color Flag;
            public Person Partner; public Transform Part; public Sketch Pose;
            public bool Moving => MoveActive && MoveAge>0;
        }
        struct Piece { public Vector3 Position, Velocity; public float Age, Life, Angle, Spin, Size; public Color Color; }

        const float Tilt=.55f;     // how far the cut-outs lean back to face a player looking down at them
        static readonly Color[] Palette={ new(.95f,.61f,.19f), new(.21f,.78f,.84f), new(.95f,.36f,.48f), new(.97f,.82f,.27f) };
        static readonly Vector2[] Ring6=Ring(6), Ring8=Ring(8), Ring12=Ring(12);
        static readonly RaycastHit[] hits=new RaycastHit[16];
        // What people choose to do while you build, and how often.
        static readonly Act[] AdultActs={Act.Stand,Act.Stroll,Act.Look,Act.Point,Act.Wave,Act.Chat,Act.Stretch,Act.Sit,Act.TapFoot,Act.Jacks};
        static readonly float[] AdultOdds={2.2f,3f,1.2f,.8f,1f,2.2f,.8f,.7f,1f,.25f};
        static readonly Act[] KidActs={Act.Stand,Act.Stroll,Act.Hop,Act.Wave,Act.Look,Act.Chat,Act.Sit,Act.Jacks};
        static readonly float[] KidOdds={.8f,3.5f,2f,1.2f,.4f,.6f,.4f,.3f};
        readonly List<Person> people=new();
        readonly List<Piece> confetti=new();
        readonly List<Vector3> verts=new(); readonly List<Color> cols=new(); readonly List<int> tris=new();
        Mesh mesh; Material material; MeshRenderer view; Camera eye;
        Transform core, start, car;
        Vector3 coreStart, liftLocal, centreLocal;
        float coreHalf, phaseAge, moodAge, clock, vanishSeconds=.25f, joy=1, nextSearch;
        bool advanced; int signature=-1, burst;
        System.Random rng=new(7);
        DownhillController level; DownhillState lastState;
        Vector3 origin, right, up, normal; float scale, facing;     // the figure being drawn

        float Unit => Mathf.Max(1e-5f,transform.lossyScale.x);
        Vector3 Local(Vector3 world) => transform.InverseTransformPoint(world);
        static Vector3 Flat(Vector3 v) => new(v.x,0,v.z);
        static Vector2[] Ring(int n) { var ring=new Vector2[n]; for(int i=0;i<n;i++) { float a=i*Mathf.PI*2/n; ring[i]=new Vector2(Mathf.Cos(a),Mathf.Sin(a)); } return ring; }
        static float Hash(int i,int salt) { unchecked { uint x=(uint)(i*374761393+salt*668265263); x=(x^(x>>13))*1274126177u; x^=x>>16; return (x&0xFFFFFF)/16777216f; } }
        float Rand() => (float)rng.NextDouble();
        float Rand(float low,float high) => low+(high-low)*Rand();

        // Baked by the editor (Snap Finish Crowd to Ground, and on every level save): how far above or below
        // the marker's plane the visible ground is under each person, so people stand on slopes and decks
        // that have no colliders.
        [SerializeField, HideInInspector] float[] groundHeights=System.Array.Empty<float>();
        [SerializeField, HideInInspector] int groundKey;
        // Also baked: a grid over the area of ground heights (NaN where there is none) and which cells
        // people may walk on (no tree, post, bench or cliff in the way).
        [SerializeField, HideInInspector] float[] gridHeights=System.Array.Empty<float>();
        [SerializeField, HideInInspector] bool[] gridWalkable=System.Array.Empty<bool>();
        [SerializeField, HideInInspector] int gridColumns, gridRows;
        [SerializeField, HideInInspector] Vector2 gridArea;
        [SerializeField, HideInInspector] float gridPersonHeight;
        // Stable across sessions and builds, unlike System.HashCode.
        static int Key(int count,Vector2 area,int seed) { unchecked { return (count*73856093)^(seed*19349663)^(area.x.GetHashCode()*83492791)^(area.y.GetHashCode()*1640531527); } }
        public void SetGroundHeights(float[] heights)
        {
            groundHeights=heights??System.Array.Empty<float>();
            groundKey=Key(Mathf.Clamp(Count,0,MaxCount),Area,Seed); signature=-1;
        }
        // People only wander once the editor has baked where they can walk for the current area and height.
        public bool HasWalkableGround => gridColumns>=2 && gridRows>=2 && gridHeights!=null && gridHeights.Length==gridColumns*gridRows &&
            gridWalkable!=null && gridWalkable.Length==gridHeights.Length && gridArea==Area && Mathf.Abs(gridPersonHeight-PersonHeight)<1e-4f;
        // The walkable-ground grid for the current area: cells about four tenths of a person apart.
        public void GroundGridSize(out int columns,out int rows)
        {
            float cell=Mathf.Max(.05f,.4f*PersonHeight);
            columns=Mathf.Clamp(Mathf.RoundToInt(Area.x/cell),2,40); rows=Mathf.Clamp(Mathf.RoundToInt(Area.y/cell),2,20);
        }
        public Vector3 GridCellCentre(int column,int row,int columns,int rows) => new(((column+.5f)/columns-.5f)*Area.x,0,((row+.5f)/rows-.5f)*Area.y);
        public void SetGroundGrid(int columns,int rows,float[] heights,bool[] walkable)
        {
            gridColumns=columns; gridRows=rows;
            gridHeights=heights??System.Array.Empty<float>(); gridWalkable=walkable??System.Array.Empty<bool>();
            gridArea=Area; gridPersonHeight=PersonHeight;
        }
        bool Cell(Vector3 local,out int index)
        {
            index=-1;
            float u=(local.x/Mathf.Max(1e-5f,Area.x)+.5f)*gridColumns, v=(local.z/Mathf.Max(1e-5f,Area.y)+.5f)*gridRows;
            if(u<0 || v<0 || u>=gridColumns || v>=gridRows) return false;
            index=(int)v*gridColumns+(int)u; return true;
        }
        bool Walkable(Vector3 local) => HasWalkableGround && Cell(local,out int i) && gridWalkable[i] && !float.IsNaN(gridHeights[i]);
        float CellSize => Mathf.Min(Area.x/Mathf.Max(1,gridColumns),Area.y/Mathf.Max(1,gridRows));
        // Ground height at a point, blended between cell centres; NaN where no ground was found.
        float GridHeight(Vector3 local)
        {
            if(!HasWalkableGround) return float.NaN;
            float u=Mathf.Clamp((local.x/Area.x+.5f)*gridColumns-.5f,0,gridColumns-1), v=Mathf.Clamp((local.z/Area.y+.5f)*gridRows-.5f,0,gridRows-1);
            int x0=Mathf.Min((int)u,gridColumns-2), z0=Mathf.Min((int)v,gridRows-2); float fx=u-x0, fz=v-z0, sum=0, weight=0;
            for(int dz=0;dz<2;dz++) for(int dx=0;dx<2;dx++) {
                float height=gridHeights[(z0+dz)*gridColumns+x0+dx]; if(float.IsNaN(height)) continue;
                float w=(dx==0?1-fx:fx)*(dz==0?1-fz:fz)+1e-4f; sum+=height*w; weight+=w;
            }
            return weight>0?sum/weight:float.NaN;
        }
        // A straight walk that stays on walkable ground and never steps up or down more than a third of a
        // person. The awkward cell under someone's own place does not count, so they can always leave it.
        bool PathClear(Vector3 from,Vector3 to)
        {
            if(!HasWalkableGround) return false;
            float step=CellSize*.4f, leave=CellSize*.75f;
            int n=Mathf.Max(1,Mathf.CeilToInt(Flat(to-from).magnitude/step));
            float last=GridHeight(from);
            for(int i=1;i<=n;i++) {
                var at=Vector3.Lerp(from,to,i/(float)n);
                if(!Walkable(at) && Flat(at-from).magnitude>leave) return false;
                float height=GridHeight(at);
                if(float.IsNaN(height)) return false;
                if(!float.IsNaN(last) && Mathf.Abs(height-last)>PersonHeight*.34f) return false;
                last=height;
            }
            return true;
        }

        void EnsurePeople()
        {
            int count=Mathf.Clamp(Count,0,MaxCount);
            int sign=Key(count,Area,Seed);
            if(sign==signature) return;
            // Heights baked for a different crowd no longer match anyone's place.
            bool grounded=groundKey==sign && groundHeights!=null && groundHeights.Length==count;
            signature=sign; people.Clear(); confetti.Clear(); Phase=CrowdPhase.Waiting; phaseAge=0; core=null; joy=1;
            Mood=CrowdMood.Idle; moodAge=0; rng=new System.Random(unchecked(Seed*7919+count));
            int rows=count>12?3:count>5?2:1, perRow=Mathf.Max(1,Mathf.CeilToInt(count/(float)rows));
            for(int i=0;i<count;i++) {
                int row=i/perRow, col=i%perRow, inRow=Mathf.Max(1,row==rows-1?count-perRow*(rows-1):perRow);
                var p=new Person { Kid=i%5==2, Offset=Hash(i,Seed+2) };
                p.Size=p.Kid?.62f:.92f+.16f*Hash(i,Seed+1);
                // Row 0 is the front row, on the blue-arrow side of the area.
                float x=((col+.5f)/inRow-.5f)*Area.x+(Hash(i,Seed+3)-.5f)*Area.x/inRow*.4f;
                float z=(rows==1?0:Mathf.Lerp(.5f,-.5f,row/(float)(rows-1))*Area.y)+(Hash(i,Seed+4)-.5f)*Area.y*.2f/rows;
                p.Home=p.Position=new Vector3(x,grounded?groundHeights[i]:0,z);
                // Everyone starts at their place doing something different, and changes at different times.
                float pick=Hash(i,Seed+5);
                p.Act=row==0 && !p.Kid && i%4==1?Act.Sit:pick<.3f?Act.Look:pick<.55f?Act.Point:Act.Stand;
                p.ActSeconds=1+4*Hash(i,Seed+6); p.Glance=p.Home+Vector3.forward*(10*PersonHeight);
                p.Joy=(Joy)(((i*7+Seed)&0x7fffffff)%4);
                p.Flag=Palette[((i+Seed)&0x7fffffff)%Palette.Length];
                people.Add(p);
            }
        }
        // Everybody back at their place, waiting. Called when a win is cancelled.
        public void ResetCrowd() { signature=-1; EnsurePeople(); }
        // The helpers who left with parts pop back in at their places; everyone else stays where they are.
        void BringBack()
        {
            EnsurePeople();
            foreach(var p in people) {
                p.Role=Role.Crowd; p.Part=null; p.Gone=0;
                if(!p.Away) continue;
                p.Away=false; p.Appear=0; p.Position=p.Home; p.MoveActive=false; p.Posed=false; p.Faced=false;
                p.Act=Act.Stand; p.ActAge=0; p.ActSeconds=Rand(.5f,2f);
            }
            confetti.Clear(); core=null; joy=1; Enter(CrowdPhase.Waiting);
        }
        // Everybody drops what they are doing, wherever they are.
        void Gather()
        {
            BringBack();
            foreach(var p in people) { EndChat(p); p.Hops=0; p.MoveActive=false; p.Act=Act.Stand; }
        }

        // What the waiting crowd does. The game sets it from the run; tests set it directly.
        public void SetMood(CrowdMood mood)
        {
            EnsurePeople();
            if(mood==Mood) return;
            Mood=mood; moodAge=0;
            if(Phase!=CrowdPhase.Waiting) return;
            float h=PersonHeight;
            for(int i=0;i<people.Count;i++) {
                var p=people[i]; if(p.Away) continue;
                EndChat(p); p.Hops=0;
                switch(mood) {
                    case CrowdMood.Ready: case CrowdMood.Watch:
                        // Everyone hurries back to their place by the track to watch.
                        p.Act=Act.Stand; p.ActAge=0; p.ActSeconds=float.MaxValue;
                        bool homeward=p.MoveActive && Flat(p.To-p.Home).sqrMagnitude<1e-6f;
                        if(!homeward && Flat(p.Position-p.Home).magnitude>.05f*h)
                            Send(p,p.Home,Flat(p.Home-p.Position).magnitude/(1.3f*h)+.1f,Rand(0,.25f),true);
                        break;
                    case CrowdMood.Groan:
                        p.MoveActive=false; Begin(p,Hash(i,Seed+30)<.6f?Act.Groan:Act.Shrug);
                        break;
                    default:
                        p.Act=Act.Stand; p.ActAge=0; p.ActSeconds=Rand(.3f,2.5f); p.Glance=GlanceAt(p);
                        break;
                }
            }
        }

        // Picks what an idle person does next.
        void ChooseIdle(Person p)
        {
            EndChat(p);
            var acts=p.Kid?KidActs:AdultActs; var odds=p.Kid?KidOdds:AdultOdds;
            float total=0; for(int i=0;i<acts.Length;i++) total+=Odds(p,acts[i],odds[i]);
            float pick=Rand()*total; var act=Act.Stand;
            for(int i=0;i<acts.Length;i++) { pick-=Odds(p,acts[i],odds[i]); if(pick<=0) { act=acts[i]; break; } }
            Begin(p,act);
        }
        float Odds(Person p,Act act,float odds)
        {
            if(act==Act.Stroll && !HasWalkableGround) return 0;
            if(act==Act.Chat && !HasWalkableGround) odds*=.4f;      // only with someone already close
            if(act!=Act.Stroll && act==p.Act) odds*=.4f;            // rarely the same thing twice
            if(act==Act.Stroll || act==Act.Hop || act==Act.Jacks) odds*=.2f+1.6f*Liveliness;
            if(act==Act.Stand || act==Act.Sit) odds*=1.6f-Liveliness;
            return odds;
        }
        void Begin(Person p,Act act)
        {
            float h=PersonHeight;
            p.Act=act; p.ActAge=0; p.Joining=false;
            switch(act) {
                case Act.Stroll: {
                    // Kids dart about in two or three quick dashes; grown-ups wander, drifting back towards their place.
                    p.Hops=p.Kid?(Rand()<.5f?1:2):0;
                    var around=Flat(p.Position-p.Home).magnitude>1.6f*h?p.Home:p.Position;
                    if(PickSpot(p,around,.6f*h,(p.Kid?1.3f:1.6f)*h,out var spot)) { StrollTo(p,spot,p.Kid?1.5f:.7f); p.ActSeconds=float.MaxValue; }
                    else Begin(p,Act.Stand);
                    return; }
                case Act.Chat: {
                    var friend=Nearest(p,3.2f*h);
                    if(friend==null) { Begin(p,Act.Stand); return; }
                    if(Flat(friend.Position-p.Position).magnitude<=1.15f*h) { StartChat(p,friend); return; }
                    // Walk over to a friend and stop beside them.
                    var side=Flat(p.Position-friend.Position).normalized;
                    var spot=friend.Position+side*(.6f*h);
                    if(Walkable(spot) && PathClear(p.Position,spot) && !Crowded(p,spot,.35f*h,friend)) {
                        spot.y=GridHeight(spot); StrollTo(p,spot,.8f);
                        p.Partner=friend; p.Joining=true; p.ActSeconds=float.MaxValue;
                    } else Begin(p,Act.Stand);
                    return; }
                case Act.Stand: p.ActSeconds=Rand(1.5f,4f); p.Glance=GlanceAt(p); break;
                case Act.Sit: p.ActSeconds=Rand(6f,11f); p.Glance=GlanceAt(p); break;
                case Act.Look: p.ActSeconds=Rand(2f,4f); break;
                case Act.Point: p.ActSeconds=Rand(1.5f,3f); break;
                case Act.Wave: p.ActSeconds=Rand(1.4f,2.4f); p.Glance=GlanceAt(p); break;
                case Act.Stretch: p.ActSeconds=Rand(1.8f,2.6f); break;
                case Act.Hop: p.ActSeconds=Rand(1f,1.8f); break;
                case Act.TapFoot: p.ActSeconds=Rand(2f,3.5f); break;
                case Act.Jacks: p.ActSeconds=Rand(2.4f,3.4f); break;
                default: p.ActSeconds=Rand(1.6f,2.6f); break;
            }
        }
        // Someone not busy: standing about, looking, pointing or pausing between strolls.
        static bool Free(Person o) => !o.Away && !o.MoveActive && o.Partner==null &&
            (o.Act==Act.Stand || o.Act==Act.Look || o.Act==Act.Point || o.Act==Act.TapFoot || o.Act==Act.Stroll);
        Person Nearest(Person p,float range)
        {
            Person best=null; float closest=range*range;
            foreach(var o in people) {
                if(o==p || !Free(o)) continue;
                float d=Flat(o.Position-p.Position).sqrMagnitude; if(d<closest) { closest=d; best=o; }
            }
            return best;
        }
        void StartChat(Person a,Person b)
        {
            EndChat(a); EndChat(b);
            float seconds=Rand(3f,6f);
            a.Act=b.Act=Act.Chat; a.ActAge=b.ActAge=0; a.ActSeconds=b.ActSeconds=seconds;
            a.Partner=b; b.Partner=a; a.Talker=true; b.Talker=false; b.MoveActive=false;
        }
        void EndChat(Person p)
        {
            var friend=p.Partner; p.Partner=null; p.Joining=false;
            if(friend!=null && friend.Partner==p) { friend.Partner=null; if(friend.Act==Act.Chat) friend.ActSeconds=Mathf.Min(friend.ActSeconds,friend.ActAge); }
        }
        Vector3 Track => car?Local(car.position):start?Local(start.position):Vector3.forward*(10*PersonHeight);
        // Somewhere to look while standing about: down the track, at a neighbour, or off into the distance.
        Vector3 GlanceAt(Person p)
        {
            float pick=Rand(), h=PersonHeight;
            if(pick<.4f) return Track;
            if(pick<.7f) {
                Person near=null; float best=float.MaxValue;
                foreach(var o in people) { if(o==p || o.Away) continue; float d=Flat(o.Position-p.Position).sqrMagnitude; if(d<best) { best=d; near=o; } }
                if(near!=null) return near.Position;
            }
            float angle=Rand()*Mathf.PI*2;
            return p.Position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(10*h);
        }
        // A free spot on walkable ground that can be reached in a straight line, away from everyone else.
        bool PickSpot(Person p,Vector3 around,float near,float far,out Vector3 spot)
        {
            float h=PersonHeight, margin=.3f*h;
            for(int attempt=0;attempt<14;attempt++) {
                float angle=Rand()*Mathf.PI*2;
                var at=around+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*Rand(near,far);
                if(Mathf.Abs(at.x)>Area.x*.5f-margin || Mathf.Abs(at.z)>Area.y*.5f-margin) continue;
                if(Flat(at-p.Position).magnitude<.35f*h || !Walkable(at) || !PathClear(p.Position,at) || Crowded(p,at,.5f*h,null)) continue;
                at.y=GridHeight(at); spot=at; return true;
            }
            spot=p.Position; return false;
        }
        bool Crowded(Person p,Vector3 at,float space,Person except)
        {
            foreach(var o in people) {
                if(o==p || o==except || o.Away) continue;
                if(Flat(o.Position-at).sqrMagnitude<space*space) return true;
                if(o.MoveActive && Flat(o.To-at).sqrMagnitude<space*space) return true;
            }
            return false;
        }
        void StrollTo(Person p,Vector3 to,float pace) => Send(p,to,Flat(to-p.Position).magnitude/Mathf.Max(.05f,pace*PersonHeight)+.15f,0,true);
        // A move arrived during idle time: kids dash on, someone joining a friend starts talking, others pause.
        void Arrived(Person p)
        {
            if(Phase!=CrowdPhase.Waiting) return;
            if(Mood!=CrowdMood.Idle) { p.Act=Act.Stand; return; }
            if(p.Joining) {
                var friend=p.Partner; p.Joining=false; p.Partner=null;
                if(friend!=null && Free(friend) && Flat(friend.Position-p.Position).magnitude<1.3f*PersonHeight) StartChat(p,friend);
                else Begin(p,Act.Stand);
                return;
            }
            if(p.Act!=Act.Stroll) return;
            if(p.Hops>0) {
                p.Hops--;
                if(PickSpot(p,p.Position,.5f*PersonHeight,1.1f*PersonHeight,out var spot)) { StrollTo(p,spot,1.5f); return; }
            }
            // Pause a moment, still looking the way they walked.
            var heading=Flat(p.To-p.From);
            p.Glance=p.Position+(heading.sqrMagnitude>1e-6f?heading.normalized:Vector3.forward)*(10*PersonHeight);
            p.ActAge=0; p.ActSeconds=Rand(.3f,1.2f);
        }

        // The winning car has stopped: one helper runs to each part, the rest gather round the core.
        // coreHalfHeight is half the core's height in world metres (0 when unknown).
        public void BeginSwarm(IReadOnlyList<Transform> parts,Transform coreBody,float coreHalfHeight=0)
        {
            Gather();
            core=coreBody; coreHalf=coreHalfHeight/Unit;
            float h=PersonHeight;
            centreLocal=core?Local(core.position):parts!=null && parts.Count>0 && parts[0]?Local(parts[0].position):Vector3.zero;
            int helpers=HelperCount(parts?.Count??0,people.Count,AlwaysStay);
            joy=people.Count>0?(people.Count-helpers)/(float)people.Count:0;
            var free=new List<Person>(people);
            for(int i=0;i<helpers;i++) {
                var at=parts[i]?Local(parts[i].position):centreLocal;
                Person person=null; float best=float.MaxValue;
                foreach(var candidate in free) { float d=(candidate.Position-at).sqrMagnitude; if(d<best) { best=d; person=candidate; } }
                free.Remove(person);
                var away=Flat(at-centreLocal);
                if(away.sqrMagnitude<1e-4f) away=Flat(person.Position-at);
                if(away.sqrMagnitude<1e-4f) away=Vector3.right;
                var stand=at+away.normalized*(.45f*h+coreHalf); stand.y=Ground(stand,at.y);
                person.Role=Role.Helper; person.Part=parts[i];
                person.Reach=Mathf.Clamp((at.y-stand.y)/(h*person.Size),.45f,1f);
                Send(person,stand,RushSeconds*(.7f+.15f*Hash(i,Seed+9)),RushSeconds*.15f*Hash(i,Seed+10));
            }
            // The nearest grown-ups carry the core; everyone gathers on the side the crowd came from.
            free.Sort((a,b)=>Rank(a).CompareTo(Rank(b)));
            int carriers=Mathf.Min(4,free.Count);
            liftLocal=new Vector3(centreLocal.x,Ground(centreLocal,centreLocal.y)+h*1.03f+coreHalf,centreLocal.z);
            var toward=Flat(-centreLocal);
            float heading=toward.sqrMagnitude>1e-4f?Mathf.Atan2(toward.z,toward.x):0;
            for(int i=0;i<free.Count;i++) {
                var person=free[i];
                float angle=heading+((i+.5f)/free.Count-.5f)*Mathf.PI*1.2f;
                var spot=centreLocal+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(coreHalf+h*(1f+.35f*(i%2)));
                spot.y=Ground(spot,centreLocal.y);
                if(i<carriers) {
                    float under=heading+(i+.5f)/carriers*Mathf.PI*2;
                    person.Role=Role.Carrier;
                    person.Carry=centreLocal+new Vector3(Mathf.Cos(under),0,Mathf.Sin(under))*Mathf.Max(.2f*h,coreHalf*.8f);
                    person.Carry.y=liftLocal.y-h*1.03f-coreHalf;
                }
                Send(person,spot,RushSeconds*(.7f+.15f*Hash(i,Seed+11)),RushSeconds*.15f*Hash(i,Seed+12));
            }
            Enter(CrowdPhase.Rush);
            float Rank(Person p) => (p.Kid?1e6f:0)+Flat(p.Position-centreLocal).sqrMagnitude;
        }
        // The held parts start to dissolve: their helpers vanish with them over the same time, and the
        // carriers step under the core and lift it.
        public void ReleaseParts(float seconds)
        {
            if(Phase!=CrowdPhase.Rush && Phase!=CrowdPhase.Hold) return;
            vanishSeconds=Mathf.Max(.05f,seconds);
            foreach(var p in people) {
                if(p.MoveActive) { p.Position=p.To; p.MoveActive=false; }
                if(p.Role==Role.Carrier) Send(p,p.Carry,.25f,0);
            }
            coreStart=core?core.position:Vector3.zero;
            Enter(CrowdPhase.Lift);
            Burst(Mathf.RoundToInt(10+38*joy));
        }
        // Standalone/playtest wins have no car-return sequence, but still get the full cheer.
        public void Celebrate()
        {
            EnsurePeople(); Gather(); joy=1; Enter(CrowdPhase.Cheer); Burst(32);
        }
        // The core leaves for the workbench: whoever is left keeps cheering, then walks home.
        public void ReleaseCore()
        {
            if(Phase!=CrowdPhase.Lift) return;
            foreach(var p in people) if(p.Role==Role.Helper) { p.Away=true; p.Gone=1; }
            core=null; Enter(CrowdPhase.Cheer);
            Burst(Mathf.RoundToInt(6+18*joy));
        }
        // Nobody released the parts (the win was cancelled): everyone walks back.
        void StandDown()
        {
            foreach(var p in people) {
                p.Role=Role.Crowd; p.Part=null; p.Gone=0;
                if(!p.Away) Send(p,p.Home,Flat(p.Home-p.Position).magnitude/(1.3f*PersonHeight)+.2f,0,true);
            }
            Enter(CrowdPhase.Return);
        }
        // Back at their places after a win or a cancelled one: idle time starts again.
        void Settle()
        {
            foreach(var p in people) {
                p.Role=Role.Crowd; p.Part=null;
                if(!p.Away) { p.Act=Act.Stand; p.ActAge=0; p.ActSeconds=Rand(.5f,2.5f); p.Glance=GlanceAt(p); }
            }
            Enter(CrowdPhase.Waiting);
        }
        void Enter(CrowdPhase phase) { Phase=phase; phaseAge=0; }
        void Send(Person p,Vector3 to,float seconds,float delay,bool grounded=false)
        {
            p.From=p.Position; p.To=to; p.MoveSeconds=Mathf.Max(.01f,seconds); p.MoveAge=-delay; p.MoveActive=true;
            float pace=Flat(to-p.From).magnitude/p.MoveSeconds/Mathf.Max(1e-4f,PersonHeight);
            p.Running=pace>1.05f; p.StrideLength=PersonHeight*p.Size*(p.Running?.6f:.32f);
            p.Grounded=grounded && HasWalkableGround;
            if(p.Grounded) {
                float a=GridHeight(p.From), b=GridHeight(to);
                p.FromLift=float.IsNaN(a)?0:p.From.y-a; p.ToLift=float.IsNaN(b)?0:to.y-b;
            }
        }
        void Step(Person p,float dt)
        {
            if(!p.MoveActive) return;
            p.MoveAge+=dt; if(p.MoveAge<=0) return;
            float u=Mathf.SmoothStep(0,1,Mathf.Clamp01(p.MoveAge/p.MoveSeconds));
            var next=Vector3.Lerp(p.From,p.To,u);
            if(p.Grounded) { float g=GridHeight(next); next.y=float.IsNaN(g)?Mathf.Lerp(p.From.y,p.To.y,u):g+Mathf.Lerp(p.FromLift,p.ToLift,u); }
            // Feet keep pace with the ground covered, so nobody slides.
            p.Stride+=Flat(next-p.Position).magnitude/Mathf.Max(1e-4f,p.StrideLength)*Mathf.PI;
            p.Position=next;
            if(p.MoveAge>=p.MoveSeconds) { p.Position=p.To; p.MoveActive=false; Arrived(p); }
        }
        void MoveCore(Vector3 position)
        {
            var body=core.GetComponent<Rigidbody>(); if(body) body.position=position;
            core.position=position;
        }
        // Height of the solid ground under a point, in marker units, looking down from the given height.
        // The marker's own plane when nothing solid is below. Vehicle parts are not ground.
        float Ground(Vector3 local,float fromHeight)
        {
            var from=transform.TransformPoint(new Vector3(local.x,fromHeight,local.z));
            int count=Physics.RaycastNonAlloc(from,-transform.up,hits,Mathf.Max(.01f,(Mathf.Abs(fromHeight)+2)*Unit),~0,QueryTriggerInteraction.Ignore);
            float best=float.NegativeInfinity;
            for(int i=0;i<count;i++) {
                if(hits[i].collider.GetComponentInParent<AssemblyPart>()) continue;
                best=Mathf.Max(best,Local(hits[i].point).y);
            }
            return float.IsNegativeInfinity(best)?0:best;
        }
        void Burst(int count)
        {
            float h=PersonHeight;
            for(int i=0;i<count && confetti.Count<96;i++) {
                int n=burst++; float turn=Hash(n,Seed+20)*Mathf.PI*2, out_=(.4f+.9f*Hash(n,Seed+21))*h;
                confetti.Add(new Piece {
                    Position=liftLocal+new Vector3(Mathf.Cos(turn),0,Mathf.Sin(turn))*(.15f*h),
                    Velocity=new Vector3(Mathf.Cos(turn)*out_,(1.8f+1.6f*Hash(n,Seed+22))*h,Mathf.Sin(turn)*out_),
                    Life=1.6f+Hash(n,Seed+23), Spin=(Hash(n,Seed+24)-.5f)*18, Size=.7f+.6f*Hash(n,Seed+25),
                    Color=n%5==4?Color.white:Palette[n%Palette.Length] });
            }
        }

        // Steps the crowd. The game calls this every frame with unscaled time; tests call it directly.
        public void Advance(float dt)
        {
            EnsurePeople();
            clock+=dt; phaseAge+=dt; moodAge+=dt; advanced=true;
            switch(Phase) {
                case CrowdPhase.Waiting: if(Mood==CrowdMood.Groan && moodAge>2.8f) SetMood(CrowdMood.Idle); break;
                case CrowdPhase.Rush: if(phaseAge>=RushSeconds) Enter(CrowdPhase.Hold); break;
                case CrowdPhase.Hold: if(phaseAge>HoldSeconds+6) StandDown(); break;
                case CrowdPhase.Lift:
                    foreach(var p in people) if(p.Role==Role.Helper && !p.Away) { p.Gone+=dt/vanishSeconds; if(p.Gone>=1) p.Away=true; }
                    if(core && joy>0) {
                        float u=Mathf.Clamp01(phaseAge/Mathf.Max(.01f,LiftSeconds*.45f));
                        var target=transform.TransformPoint(liftLocal+Vector3.up*(Mathf.Sin(phaseAge*9)*.04f*PersonHeight*u));
                        MoveCore(Vector3.Lerp(coreStart,target,Mathf.SmoothStep(0,1,u)));
                    }
                    if(phaseAge>LiftSeconds+4) ReleaseCore();
                    break;
                case CrowdPhase.Cheer:
                    if(phaseAge>=CheerSeconds) {
                        for(int i=0;i<people.Count;i++) if(!people[i].Away) Send(people[i],people[i].Home,ReturnSeconds*.8f,ReturnSeconds*.2f*Hash(i,Seed+13),true);
                        Enter(CrowdPhase.Return);
                    }
                    break;
                case CrowdPhase.Return: if(phaseAge>=ReturnSeconds) Settle(); break;
            }
            foreach(var p in people) {
                if(p.Away) continue;
                if(p.Appear<1) p.Appear=Mathf.Min(1,p.Appear+dt*3);
                p.Face=Mathf.MoveTowards(p.Face,p.Facing,dt*8);
                Step(p,dt);
                if(Phase==CrowdPhase.Waiting) {
                    p.ActAge+=dt;
                    if(Mood==CrowdMood.Idle && !p.MoveActive && p.ActAge>=p.ActSeconds) ChooseIdle(p);
                }
                var aim=Aim(p);
                p.Pose=p.Posed?Sketch.Lerp(p.Pose,aim,1-Mathf.Exp(-dt*14)):aim; p.Posed=true;
            }
            for(int i=confetti.Count-1;i>=0;i--) {
                var c=confetti[i]; c.Age+=dt;
                if(c.Age>=c.Life) { confetti.RemoveAt(i); continue; }
                c.Velocity+=Vector3.down*(4.5f*PersonHeight*dt); c.Velocity*=Mathf.Max(0,1-1.2f*dt);
                c.Position+=c.Velocity*dt; c.Angle+=c.Spin*dt; confetti[i]=c;
            }
        }

        static Sketch Stand() => new Sketch {
            FootB=new(-.06f,0), FootF=new(.06f,0), Hip=new(0,.37f), Chest=new(0,.68f), Head=new(0,.88f),
            ElbowB=new(-.115f,.54f), HandB=new(-.135f,.41f), ElbowF=new(.115f,.54f), HandF=new(.135f,.41f) };
        static Sketch RunPose(float phase)
        {
            float s=Mathf.Sin(phase), c=Mathf.Cos(phase); var q=Stand();
            q.FootF=new(.14f*s,.05f*Mathf.Max(0,c)); q.FootB=new(-.14f*s,.05f*Mathf.Max(0,-c));
            q.Chest=new(.05f,.675f); q.Head=new(.085f,.87f);
            q.ElbowF=new(.14f-.08f*s,.57f); q.HandF=new(.21f-.17f*s,.52f-.05f*s);
            q.ElbowB=new(-.06f+.08f*s,.57f); q.HandB=new(-.02f+.17f*s,.52f+.05f*s);
            q.Lift=.03f*Mathf.Abs(s);
            return q;
        }
        static Sketch WalkPose(float phase)
        {
            float s=Mathf.Sin(phase), c=Mathf.Cos(phase); var q=Stand();
            q.FootF=new(.09f*s,.035f*Mathf.Max(0,c)); q.FootB=new(-.09f*s,.035f*Mathf.Max(0,-c));
            q.Chest=new(.02f,.68f); q.Head=new(.035f,.88f);
            q.ElbowF=new(.09f-.05f*s,.545f); q.HandF=new(.1f-.12f*s,.425f+.02f*Mathf.Abs(s));
            q.ElbowB=new(-.09f+.05f*s,.545f); q.HandB=new(-.1f+.12f*s,.425f+.02f*Mathf.Abs(s));
            q.Lift=.012f*Mathf.Abs(c);
            return q;
        }
        // Idle time, and the reaction to a failed run.
        Sketch IdlePose(Person p,float t)
        {
            var q=Stand();
            switch(p.Act) {
                case Act.Look:          // shading the eyes, other hand on the hip
                    q.ElbowF=new(.21f,.76f); q.HandF=new(.12f,.9f); q.ElbowB=new(-.18f,.545f); q.HandB=new(-.09f,.47f); q.Head=new(.02f,.88f); break;
                case Act.Point:
                    q.ElbowF=new(.235f,.7f); q.HandF=new(.38f,.74f+.015f*Mathf.Sin(t*2.3f)); break;
                case Act.Sit:
                    q.Hip=new(0,.21f); q.Chest=new(0,.515f); q.Head=new(.02f,.71f); q.FootB=new(.17f,.012f); q.FootF=new(.22f,.012f);
                    q.ElbowF=new(.12f,.42f); q.HandF=new(.15f,.3f); q.ElbowB=new(-.11f,.4f); q.HandB=new(-.04f,.3f); break;
                case Act.Wave:
                    q.ElbowF=new(.16f,.76f); q.HandF=new(.2f+.07f*Mathf.Sin(t*11),.97f); q.Head=new(.015f,.885f); break;
                case Act.Chat:
                    if((p.ActAge<p.ActSeconds*.5f)==p.Talker) {
                        // Talking with one hand.
                        q.ElbowF=new(.16f,.56f); q.HandF=new(.24f+.04f*Mathf.Sin(t*5),.64f+.06f*Mathf.Sin(t*7.3f)); q.Head=new(.02f,.88f+.008f*Mathf.Sin(t*9));
                    } else {
                        // Listening, hand on the hip, nodding.
                        q.ElbowB=new(-.18f,.545f); q.HandB=new(-.09f,.47f); q.Head=new(.02f,.875f+.012f*Mathf.Max(0,Mathf.Sin(t*4.5f)));
                    }
                    break;
                case Act.Stretch: {
                    float s=Mathf.Sin(t*1.6f);
                    q.Chest=new(.03f*s,.69f); q.Head=new(.05f*s,.895f);
                    q.ElbowB=new(-.07f+.03f*s,.86f); q.HandB=new(-.01f+.05f*s,1.05f); q.ElbowF=new(.07f+.03f*s,.86f); q.HandF=new(.01f+.05f*s,1.05f);
                    break; }
                case Act.Hop: {
                    float b=Mathf.Abs(Mathf.Sin(t*7.5f));
                    q.FootB=new(-.04f,0); q.FootF=new(.04f,0); q.ElbowB=new(-.15f,.6f); q.HandB=new(-.22f,.66f); q.ElbowF=new(.15f,.6f); q.HandF=new(.22f,.66f);
                    q.Lift=.08f*b; break; }
                case Act.TapFoot:       // hands on hips, one foot tapping
                    q.ElbowB=new(-.18f,.545f); q.HandB=new(-.09f,.47f); q.ElbowF=new(.18f,.545f); q.HandF=new(.09f,.47f);
                    q.FootF=new(.1f,.03f*Mathf.Max(0,Mathf.Sin(t*9))); break;
                case Act.Jacks: {
                    float s=.5f+.5f*Mathf.Sin(t*7);
                    q.FootB=new(-.05f-.09f*s,0); q.FootF=new(.05f+.09f*s,0);
                    q.ElbowB=new(-.13f-.06f*s,Mathf.Lerp(.53f,.84f,s)); q.HandB=new(Mathf.Lerp(-.14f,-.19f,s),Mathf.Lerp(.41f,1.02f,s));
                    q.ElbowF=new(.13f+.06f*s,Mathf.Lerp(.53f,.84f,s)); q.HandF=new(Mathf.Lerp(.14f,.19f,s),Mathf.Lerp(.41f,1.02f,s));
                    q.Lift=.035f*(1-Mathf.Abs(2*s-1)); break; }
                case Act.Groan:         // hands on the head
                    q.ElbowB=new(-.17f,.8f); q.HandB=new(-.08f,.96f); q.ElbowF=new(.17f,.8f); q.HandF=new(.08f,.96f);
                    q.Chest=new(.01f,.67f); q.Head=new(.025f,.86f); break;
                case Act.Shrug: {
                    float lift=.012f*Mathf.Sin(t*5);
                    q.ElbowB=new(-.16f,.5f); q.HandB=new(-.26f,.6f+lift); q.ElbowF=new(.16f,.5f); q.HandF=new(.26f,.6f+lift);
                    q.Chest=new(0,.69f); q.Head=new(0,.9f); break; }
                default: {              // standing about, shifting weight
                    float sway=Mathf.Sin(t*.9f);
                    q.Hip=new(.012f*sway,.37f); q.Chest=new(.008f*sway,.68f); q.Head=new(.015f*sway,.88f); break; }
            }
            if(p.Act!=Act.Jacks && p.Act!=Act.Hop) { float breathe=.008f*Mathf.Sin(t*1.7f); q.Chest.y+=breathe; q.Head.y+=breathe*1.3f; }
            return q;
        }
        // Leaning in, hands on knees, as the car sets off.
        static Sketch ReadyPose(float t)
        {
            var q=Stand(); q.Hip=new(-.02f,.34f); q.Chest=new(.07f,.62f+.006f*Mathf.Sin(t*3)); q.Head=new(.11f,.8f);
            q.ElbowB=new(.04f,.45f); q.HandB=new(.06f,.25f); q.ElbowF=new(.1f,.45f); q.HandF=new(.11f,.25f);
            q.FootB=new(-.08f,0); q.FootF=new(.08f,0);
            return q;
        }
        // Following the run: bouncing, pointing, clapping or pumping a fist.
        Sketch WatchPose(Person p,float t)
        {
            var q=Stand(); float beat=Mathf.Abs(Mathf.Sin(t*6));
            switch(p.Joy) {
                case Joy.Jump: q.ElbowB=new(-.16f,.7f); q.HandB=new(-.21f,.86f); q.ElbowF=new(.16f,.7f); q.HandF=new(.21f,.86f); q.Lift=.05f*beat; break;
                case Joy.Flag: q.ElbowF=new(.235f,.7f); q.HandF=new(.38f,.74f); q.Lift=.03f*beat; break;
                case Joy.Clap: {
                    float open=.05f*(.5f+.5f*Mathf.Sin(t*13));
                    q.ElbowB=new(-.12f,.55f); q.HandB=new(-.01f-open,.64f); q.ElbowF=new(.12f,.55f); q.HandF=new(.01f+open,.64f); break; }
                default:
                    q.ElbowF=new(.14f,.7f+.06f*Mathf.Sin(t*8)); q.HandF=new(.16f,.86f+.08f*Mathf.Sin(t*8)); q.ElbowB=new(-.18f,.545f); q.HandB=new(-.09f,.47f);
                    q.Lift=.03f*beat; break;
            }
            return q;
        }
        Sketch Cheer(Person p,float t)
        {
            var q=Stand(); float beat=Mathf.Abs(Mathf.Sin(t*6.5f));
            var kind=p.Joy==Joy.Flag && joy<.35f?Joy.Jump:p.Joy;
            switch(kind) {
                case Joy.Jump:
                    q.FootB=new(-.12f,0); q.FootF=new(.12f,0); q.ElbowB=new(-.17f,.82f); q.HandB=new(-.29f,1.18f);
                    q.ElbowF=new(.17f,.82f); q.HandF=new(.29f,1.18f); q.Lift=(.25f+.45f*joy)*beat; break;
                case Joy.Flag:
                    q.ElbowF=new(.15f,.82f); q.HandF=new(.2f+.07f*Mathf.Sin(t*7),.94f); q.ElbowB=new(-.18f,.545f); q.HandB=new(-.09f,.47f);
                    q.Lift=.30f*beat; break;
                case Joy.Clap: {
                    float open=.07f*(.5f+.5f*Mathf.Sin(t*15));
                    q.ElbowB=new(-.15f,.9f); q.HandB=new(-.02f-open,1.06f); q.ElbowF=new(.15f,.9f); q.HandF=new(.02f+open,1.06f);
                    q.Head=new(0,.86f); q.Lift=.25f*beat; break; }
                default:
                    q.ElbowF=new(.12f,.86f); q.HandF=new(.15f,1f+.06f*Mathf.Sin(t*9)); q.ElbowB=new(-.20f,.94f); q.HandB=new(-.26f,1.15f);
                    q.Lift=(.18f+.32f*joy)*beat; break;
            }
            return q;
        }
        static Sketch Carry(float t,float effort)
        {
            var q=Stand(); float bob=.02f*effort*Mathf.Sin(t*9);
            q.ElbowB=new(-.11f,.86f+bob); q.HandB=new(-.07f,1.03f+bob); q.ElbowF=new(.11f,.86f+bob); q.HandF=new(.07f,1.03f+bob);
            q.Lift=.03f*effort*Mathf.Abs(Mathf.Sin(t*9));
            return q;
        }
        static Sketch ReachFor(float height)
        {
            var q=Stand(); q.Chest=new(.03f,.68f); q.Head=new(.05f,.875f);
            q.ElbowF=new(.2f,Mathf.Lerp(.6f,height,.5f)); q.HandF=new(.3f,height);
            q.ElbowB=new(.1f,Mathf.Lerp(.55f,height-.12f,.5f)); q.HandB=new(.27f,height-.14f);
            return q;
        }
        Sketch Aim(Person p)
        {
            float t=clock+p.Offset*10;
            if(p.Moving) return p.Running?RunPose(p.Stride):WalkPose(p.Stride);
            switch(Phase) {
                case CrowdPhase.Rush: case CrowdPhase.Hold:
                    if(p.Role==Role.Helper) return ReachFor(p.Reach);
                    var ready=Stand(); ready.Lift=.04f*Mathf.Abs(Mathf.Sin(t*7)); return ready;
                case CrowdPhase.Lift:
                    return p.Role==Role.Helper?ReachFor(p.Reach):p.Role==Role.Carrier?Carry(t,1):Cheer(p,t);
                case CrowdPhase.Cheer: return Cheer(p,t);
                case CrowdPhase.Return: return IdlePose(p,t);
            }
            switch(Mood) {
                case CrowdMood.Ready: if(!p.Kid) return ReadyPose(t); var hop=Stand(); hop.Lift=.05f*Mathf.Abs(Mathf.Sin(t*7)); return hop;
                case CrowdMood.Watch: return WatchPose(p,t);
                default: return IdlePose(p,t);
            }
        }
        // What the person is turned towards, in marker space.
        Vector3 Focus(Person p)
        {
            if(p.Moving) return p.To;
            if(Phase==CrowdPhase.Waiting) {
                if(Mood!=CrowdMood.Idle) return Track;
                switch(p.Act) {
                    case Act.Chat: return p.Partner!=null?p.Partner.Position:p.Glance;
                    case Act.Look: case Act.Point: return Track;
                    default: return p.Glance;
                }
            }
            if(Phase==CrowdPhase.Return) return Track;
            if(p.Role==Role.Helper && p.Part) return Local(p.Part.position);
            return centreLocal;
        }
        bool ShowsFlag(Person p) => p.Joy==Joy.Flag && joy>=.35f && !p.Moving && p.Role!=Role.Helper &&
            (Phase==CrowdPhase.Cheer || Phase==CrowdPhase.Lift && p.Role!=Role.Carrier);

        // Draws the whole crowd as one mesh in the marker's space, turned to face the given camera.
        public Mesh Draw(Vector3 cameraWorld)
        {
            EnsurePeople();
            if(!mesh) { mesh=new Mesh { name="Mini people", hideFlags=HideFlags.HideAndDontSave }; mesh.MarkDynamic(); }
            verts.Clear(); cols.Clear(); tris.Clear();
            var cam=Local(cameraWorld); float h=PersonHeight, behind=-.012f*h;
            Color ink=Tone(new Color(.078f,.086f,.106f)), rim=Color.white;
            foreach(var p in people) {
                if(p.Away) continue;
                if(!p.Posed) { p.Pose=Aim(p); p.Posed=true; }
                Basis(p.Position,cam);
                float side=Vector3.Dot(right,Focus(p)-p.Position);
                if(Mathf.Abs(side)>.02f*h) p.Facing=Mathf.Sign(side);
                // Turning flips the cut-out through edge-on like a sheet of card. A still preview (nothing
                // stepping the crowd) shows each person already turned.
                if(!p.Faced || !advanced) { p.Face=p.Facing; p.Faced=true; }
                facing=p.Face; scale=h*p.Size*Shrink(p.Gone)*Pop(p.Appear);
                origin=p.Position+up*(p.Pose.Lift*scale);
                bool flag=ShowsFlag(p);
                Vector2 hand=p.Pose.HandF, along=default, across=default, tip=default;
                if(flag) {
                    float wave=.25f*Mathf.Sin((clock+p.Offset*10)*7);
                    along=new Vector2(Mathf.Sin(wave),Mathf.Cos(wave)); across=new Vector2(along.y,-along.x); tip=hand+along*.32f;
                }
                Figure(p.Pose,.023f,rim,behind);
                if(flag) {
                    Stroke(hand,tip,.034f,rim,behind,Ring6);
                    Triangle(tip+along*.03f-across*.02f,tip-along*.19f-across*.02f,tip-along*.08f+across*.285f,rim,behind);
                }
                Figure(p.Pose,0,ink,0);
                if(flag) {
                    Stroke(hand,tip,.012f,ink,0,Ring6);
                    Triangle(tip,tip-along*.16f,tip-along*.08f+across*.24f,Tone(p.Flag),-behind*.3f);
                }
            }
            foreach(var c in confetti) {
                Basis(c.Position,cam);
                float fade=Mathf.Clamp01((c.Life-c.Age)/.3f), a=Mathf.Cos(c.Angle), b=Mathf.Sin(c.Angle);
                var wide=(right*a+up*b)*(.05f*h*c.Size*fade); var tall=(up*a-right*b)*(.024f*h*c.Size*fade);
                int first=verts.Count; var color=Tone(c.Color);
                verts.Add(c.Position-wide-tall); verts.Add(c.Position+wide-tall); verts.Add(c.Position+wide+tall); verts.Add(c.Position-wide+tall);
                for(int i=0;i<4;i++) cols.Add(color);
                tris.Add(first); tris.Add(first+1); tris.Add(first+2); tris.Add(first); tris.Add(first+2); tris.Add(first+3);
            }
            mesh.Clear();
            if(verts.Count>0) { mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris,0,true); }
            advanced=false;
            return mesh;
        }
        static Color Tone(Color color) => QualitySettings.activeColorSpace==ColorSpace.Linear?color.linear:color;
        // Helpers swell a little, then shrink away with the part they hold.
        static float Shrink(float gone) => gone<=0?1:gone<.25f?1+.15f*(gone/.25f):Mathf.Lerp(1.15f,0,(gone-.25f)/.75f);
        // People coming back pop in with a small overshoot.
        static float Pop(float appear) { if(appear>=1) return 1; float x=appear-1; return 1+2.70158f*x*x*x+1.70158f*x*x; }
        // Upright cut-out turned to the camera, leaning back part of the way when seen from above.
        void Basis(Vector3 position,Vector3 cam)
        {
            var to=cam-position; var flat=Flat(to);
            if(flat.sqrMagnitude<1e-8f) flat=Vector3.back;
            var level_=flat.normalized; var direct=to.sqrMagnitude>1e-8f?to.normalized:level_;
            normal=Vector3.Lerp(level_,direct,Tilt).normalized;
            right=Vector3.Cross(normal,Vector3.up).normalized; up=Vector3.Cross(right,normal);
        }
        Vector3 At(Vector2 p,float depth) => origin+right*(p.x*facing*scale)+up*(p.y*scale)+normal*depth;
        void Figure(in Sketch q,float grow,Color color,float depth)
        {
            float limb=.038f+grow;
            var shoulderB=q.Chest+new Vector2(-.09f,-.015f); var shoulderF=q.Chest+new Vector2(.09f,-.015f);
            Stroke(q.Hip+new Vector2(-.045f,0),q.FootB,limb,color,depth,Ring6);
            Stroke(q.Hip+new Vector2(.045f,0),q.FootF,limb,color,depth,Ring6);
            Stroke(shoulderB,q.ElbowB,limb,color,depth,Ring6); Stroke(q.ElbowB,q.HandB,limb,color,depth,Ring6);
            Stroke(shoulderF,q.ElbowF,limb,color,depth,Ring6); Stroke(q.ElbowF,q.HandF,limb,color,depth,Ring6);
            Stroke(q.Hip+new Vector2(0,.01f),q.Chest,.098f+grow,color,depth,Ring8);
            Disc(q.Head,.121f+grow,color,depth,Ring12);
        }
        void Disc(Vector2 centre,float radius,Color color,float depth,Vector2[] ring)
        {
            int first=verts.Count; verts.Add(At(centre,depth)); cols.Add(color);
            for(int i=0;i<ring.Length;i++) { verts.Add(At(centre+ring[i]*radius,depth)); cols.Add(color); }
            for(int i=0;i<ring.Length;i++) { tris.Add(first); tris.Add(first+1+i); tris.Add(first+1+(i+1)%ring.Length); }
        }
        // A thick line with round ends.
        void Stroke(Vector2 a,Vector2 b,float radius,Color color,float depth,Vector2[] ring)
        {
            var d=b-a; float length=d.magnitude;
            if(length>1e-5f) {
                var n=new Vector2(-d.y,d.x)/length*radius; int first=verts.Count;
                verts.Add(At(a+n,depth)); verts.Add(At(b+n,depth)); verts.Add(At(b-n,depth)); verts.Add(At(a-n,depth));
                for(int i=0;i<4;i++) cols.Add(color);
                tris.Add(first); tris.Add(first+1); tris.Add(first+2); tris.Add(first); tris.Add(first+2); tris.Add(first+3);
            }
            Disc(a,radius,color,depth,ring); Disc(b,radius,color,depth,ring);
        }
        void Triangle(Vector2 a,Vector2 b,Vector2 c,Color color,float depth)
        {
            int first=verts.Count; verts.Add(At(a,depth)); verts.Add(At(b,depth)); verts.Add(At(c,depth));
            for(int i=0;i<3;i++) cols.Add(color);
            tris.Add(first); tris.Add(first+1); tris.Add(first+2);
        }

        Material Paint { get {
            if(!material) { var shader=Resources.Load<Shader>("SandboxMiniPeople"); if(shader) material=new Material(shader) { hideFlags=HideFlags.HideAndDontSave }; }
            return material;
        } }
        void OnEnable()
        {
            var factory=GetComponentInParent<SandboxFactoryLevel>(true);
            start=factory?factory.Start:null;
            signature=-1;
#if UNITY_EDITOR
            RenderPipelineManager.beginCameraRendering+=Preview;
#endif
        }
        void OnDisable()
        {
#if UNITY_EDITOR
            RenderPipelineManager.beginCameraRendering-=Preview;
#endif
            if(view) Dispose(view.gameObject);
            view=null; Dispose(mesh); mesh=null; Dispose(material); material=null;
        }
        static void Dispose(Object item) { if(!item) return; if(Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
        void LateUpdate()
        {
            if(!Application.isPlaying) return;
            Watch();
            Advance(Time.unscaledDeltaTime);
            if(!eye) eye=Camera.main;
            if(!eye || !Paint) return;
            var drawn=Draw(eye.transform.position);
            if(!view) {
                // Created only while playing: scene saves persist every mesh found under a level.
                var go=new GameObject("Mini people") { layer=gameObject.layer };
                go.transform.SetParent(transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=drawn;
                view=go.AddComponent<MeshRenderer>(); view.sharedMaterial=Paint;
                view.shadowCastingMode=ShadowCastingMode.Off; view.receiveShadows=false;
                view.lightProbeUsage=LightProbeUsage.Off; view.reflectionProbeUsage=ReflectionProbeUsage.Off;
            }
        }
        // Follows the game: mill about while you build, hurry back when you press START, watch the run,
        // groan when it fails, and bring the helpers back for the next run.
        void Watch()
        {
            if(!level && Time.unscaledTime>=nextSearch) {
                level=FindFirstObjectByType<DownhillController>(); nextSearch=Time.unscaledTime+1;
                if(level) { lastState=level.State; SetMood(MoodFor(lastState,DownhillState.Build,Mood)); }
            }
            if(!level) { car=null; return; }
            var state=level.State;
            if(state!=lastState) {
                if(state==DownhillState.Countdown && !level.VictoryInProgress && (Phase!=CrowdPhase.Waiting || Remaining<Total)) BringBack();
                if(state==DownhillState.Success && Phase==CrowdPhase.Waiting) Celebrate();
                SetMood(MoodFor(state,lastState,Mood));
                lastState=state;
            }
            bool testing=state==DownhillState.Countdown || state==DownhillState.Running || state==DownhillState.Success;
            car=testing && level.SimulationCore && level.SimulationCore.MainBody?level.SimulationCore.MainBody.transform:null;
        }
        static CrowdMood MoodFor(DownhillState state,DownhillState previous,CrowdMood current) => state switch {
            DownhillState.Build => CrowdMood.Idle,
            // START sends the car over: everyone heads back to watch.
            DownhillState.Transition when previous==DownhillState.Build => CrowdMood.Ready,
            DownhillState.Countdown => CrowdMood.Ready,
            DownhillState.Running => CrowdMood.Watch,
            DownhillState.Failed => CrowdMood.Groan,
            _ => current };
#if UNITY_EDITOR
        // Edit-time preview only. Nothing is added to the scene, so nothing is saved with the level.
        void Preview(ScriptableRenderContext context,Camera camera)
        {
            if(Application.isPlaying || !this || !isActiveAndEnabled || !camera) return;
            if(camera.cameraType!=CameraType.SceneView && camera.cameraType!=CameraType.Game) return;
            if(UnityEditor.SceneManagement.EditorSceneManager.IsPreviewSceneObject(this) || !Paint) return;
            Graphics.DrawMesh(Draw(camera.transform.position),transform.localToWorldMatrix,Paint,gameObject.layer,camera,0,null,ShadowCastingMode.Off,false);
        }
#endif
        void OnDrawGizmos()
        {
            var matrix=Gizmos.matrix; var color=Gizmos.color;
            Gizmos.matrix=transform.localToWorldMatrix; Gizmos.color=new Color(1,.6f,.15f,.9f);
            Gizmos.DrawWireCube(Vector3.up*(PersonHeight*.5f),new Vector3(Area.x,PersonHeight,Area.y));
            Gizmos.DrawLine(Vector3.zero,Vector3.forward*(Area.y*.5f+PersonHeight*.6f));
            Gizmos.matrix=matrix; Gizmos.color=color;
        }
        // Selected: where people may walk (green) and where they may not (red).
        void OnDrawGizmosSelected()
        {
            if(!HasWalkableGround) return;
            var matrix=Gizmos.matrix; var color=Gizmos.color;
            Gizmos.matrix=transform.localToWorldMatrix;
            var size=new Vector3(Area.x/gridColumns*.7f,.01f*PersonHeight,Area.y/gridRows*.7f);
            for(int r=0;r<gridRows;r++) for(int c=0;c<gridColumns;c++) {
                int i=r*gridColumns+c; if(float.IsNaN(gridHeights[i])) continue;
                var at=GridCellCentre(c,r,gridColumns,gridRows); at.y=gridHeights[i]+.02f*PersonHeight;
                Gizmos.color=gridWalkable[i]?new Color(.3f,1,.4f,.55f):new Color(1,.25f,.2f,.55f);
                Gizmos.DrawCube(at,size);
            }
            Gizmos.matrix=matrix; Gizmos.color=color;
        }
    }
}
