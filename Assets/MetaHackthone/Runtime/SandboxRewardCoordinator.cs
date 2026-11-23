using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalAssembly
{
    public enum SandboxRewardState { Dormant=0, Revealing=1, ToMenu=4, AtMenu=5, Claimed=7 }
    public sealed class SandboxRewardCoordinator : MonoBehaviour
    {
        public float RevealSeconds=1.1f, DeliverySeconds=1f, PreviewTravelSeconds=.45f, ButtonClearance=.035f;
        public float GoalHeight=1.8f, RotationSeconds=2f, RayRadius=.16f, RaySize=.016f;
        // Rise distance at the authored reveal point.
        public float RiseDistance=.2f;
        SandboxRewardPresentation focus;
        float goalScale=1, currentScale=1, travelScale=1, spin, streamAge, revealProgress;
        Transform landing;
        SandboxLevelLayout layout;
        readonly List<AssemblyPart> temporarySources=new();
        SandboxFragments rays, stream;
        public float GoalScale => goalScale;
        public SandboxRewardState State { get; private set; }
        public Transform Preview { get; private set; }
        public bool HaloVisible { get; private set; }
        DownhillController level;
        Transform goal, frame;
        FloatingStartButton nextTile;
        SandboxFragments fragments;
        readonly List<(Renderer renderer,Material[] materials)> visuals=new();
        readonly List<Vector3> samples=new();
        // Each unlocked part spins about its own vertical axis in its own slot beside Next Level.
        readonly List<(Transform group,Vector3 slot,Vector3 center)> spinners=new();
        float spinReach; // furthest any spinning part reaches from the row's centre (preview space)
        Material dissolve;
        Vector3 travelStart, goalOffset, previewExtents;
        Quaternion travelRotation;
        float age;
        bool cancelled;
        public void Initialize(DownhillController owner)
        {
            level=owner; goal=owner.RewardGoal; frame=owner.Course.SandboxFrame;
            var next=owner.Presentation.Interface.NextLevelButton;
            if(next) nextTile=next.GetComponent<FloatingStartButton>();
        }
        public void Reveal()
        {
            if(cancelled || State!=SandboxRewardState.Dormant) return;
            var sources=new List<AssemblyPart>();
            foreach(var kind in level.RewardKinds) {
                var part=level.Kit.FirstOrDefault(p=>p.Kind==kind);
                if(!part && level.Core) {
                    part=DownhillParts.Create(level.Workshop.World,kind,new Pose(Vector3.zero,Quaternion.identity),level.Core.GeometryScale,gridVersion:DownhillParts.DesignerGridVersion);
                    level.Workshop.World.UnregisterPart(part);
                    part.gameObject.SetActive(false);
                    temporarySources.Add(part);
                }
                if(part) sources.Add(part);
            }
            if(sources.Count==0 || !goal) return;
            Preview=new GameObject("Reward preview (visual only)").transform;
            Preview.SetParent(frame,false);
            // Levels with a layout present the part at build size above the building area.
            layout=level.ActiveLayout;
            landing=layout || level.FactoryLevel?null:level.GarageCourse?level.GarageCourse.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Garage landing"):null;
            goalScale=layout?1:level.TransferScale; currentScale=goalScale;
            rays=new SandboxFragments(transform,49); // one soft halo plus twelve spokes of four
            dissolve=new Material(Resources.Load<Shader>("SandboxDissolve"));
            // Each unlocked part gets its own group; groups stand side by side along the preview's X axis.
            var groups=new List<(Transform group,Bounds bounds,int firstSample)>();
            foreach(var source in sources) {
                var group=new GameObject("Reward part "+source.Kind).transform; group.SetParent(Preview,false);
                int firstSample=samples.Count; bool any=false; var groupBounds=new Bounds();
                // Copy only mesh data and materials; never instantiate the interactive part.
                var origin=source.MainBody.transform;
                foreach(var renderer in source.VisualRenderers) {
                    var filter=renderer.GetComponent<MeshFilter>(); if(!filter || !filter.sharedMesh) continue;
                    var child=new GameObject("Reward mesh").transform; child.SetParent(group,false);
                    child.localPosition=Quaternion.Inverse(origin.rotation)*(renderer.transform.position-origin.position);
                    child.localRotation=Quaternion.Inverse(origin.rotation)*renderer.transform.rotation;
                    child.localScale=renderer.transform.lossyScale;
                    child.gameObject.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
                    var visual=child.gameObject.AddComponent<MeshRenderer>(); visual.shadowCastingMode=ShadowCastingMode.Off;
                    visuals.Add((visual,renderer.sharedMaterials));
                    visual.sharedMaterials=Enumerable.Repeat(dissolve,filter.sharedMesh.subMeshCount).ToArray();
                    var mesh=filter.sharedMesh;
                    var vertices=mesh.isReadable?mesh.vertices:new[]{mesh.bounds.center};
                    for(int i=0;i<vertices.Length;i+=Mathf.Max(1,vertices.Length/64))
                        samples.Add(child.localPosition+child.localRotation*Vector3.Scale(vertices[i],child.localScale));
                    var b=mesh.bounds;
                    for(int i=0;i<8;i++) {
                        var corner=child.localPosition+child.localRotation*Vector3.Scale(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)),child.localScale);
                        if(!any) { groupBounds=new Bounds(corner,Vector3.zero); any=true; } else groupBounds.Encapsulate(corner);
                    }
                }
                groups.Add((group,groupBounds,firstSample));
            }
            // Slots are as wide as the circle each part sweeps while spinning, so neighbours never overlap.
            float Slot(Bounds b)=>2*new Vector2(b.extents.x,b.extents.z).magnitude;
            float gap=groups.Count>1?groups.Max(g=>Slot(g.bounds))*.15f:0;
            float width=groups.Sum(g=>Slot(g.bounds))+gap*(groups.Count-1), cursor=-width*.5f;
            var bounds=new Bounds(); bool started=false;
            for(int g=0;g<groups.Count;g++) {
                var item=groups[g];
                // Centre each group on its own slot; bottoms line up so the pair sits on one baseline.
                var offset=new Vector3(cursor+Slot(item.bounds)*.5f-item.bounds.center.x,-item.bounds.min.y,-item.bounds.center.z);
                item.group.localPosition=offset; cursor+=Slot(item.bounds)+gap;
                int end=g+1<groups.Count?groups[g+1].firstSample:samples.Count;
                for(int i=item.firstSample;i<end;i++) samples[i]+=offset;
                var placed=new Bounds(item.bounds.center+offset,item.bounds.size);
                if(!started) { bounds=placed; started=true; } else bounds.Encapsulate(placed);
            }
            foreach(Transform child in Preview) child.localPosition-=bounds.center;
            for(int i=0;i<samples.Count;i++)samples[i]-=bounds.center;
            foreach(var item in groups) {
                // Pivot each part around the middle of its own footprint (keep its height).
                var center=new Vector3(item.bounds.center.x,0,item.bounds.center.z);
                spinners.Add((item.group,item.group.localPosition+center,center));
                spinReach=Mathf.Max(spinReach,Mathf.Abs(item.group.localPosition.x+center.x)+new Vector2(item.bounds.extents.x,item.bounds.extents.z).magnitude);
            }
            previewExtents=bounds.extents;
            focus=Preview.gameObject.AddComponent<SandboxRewardPresentation>();
            focus.Initialize(level.FactoryLevel?level.FactoryLevel.TrackRoot:level.GarageCourse.transform,Preview);
            goalOffset=Vector3.up*(previewExtents.y+.045f);
            var pose=GoalPose;
            SetPose(pose.position-pose.rotation*Vector3.up*RiseDistance,pose.rotation);
            fragments=new SandboxFragments(transform,96);
            stream=new SandboxFragments(transform,48);
            State=SandboxRewardState.Revealing; age=0; revealProgress=0;
            if(level.Feedback) level.Feedback.Emit(SandboxSoundCue.Reveal,pose.position,this,pose.rotation*Vector3.up,1,SandboxSurfaceMaterial.Wood,previewExtents.magnitude*2*currentScale);
        }
        // Where the preview rests: the level's reveal point (the part's centre), else above the course goal.
        public Pose GoalPose => landing?new Pose(landing.parent.TransformPoint(new Vector3(landing.localPosition.x,GoalHeight,landing.localPosition.z)),goal.rotation):
            layout?layout.RewardRevealPose():new Pose(goal.position+goal.rotation*goalOffset,goal.rotation);
        Vector3 GoalPosition => GoalPose.position;
        Quaternion GoalRotation => GoalPose.rotation;
        // World radius of the halo: at least RayRadius, and wide enough to frame a build-size part.
        float HaloRadius => Mathf.Max(RayRadius,previewExtents.magnitude*currentScale*1.5f);
        void SetPose(Vector3 position,Quaternion rotation)
        {
            Preview.SetPositionAndRotation(position,rotation);
            var scale=Preview.parent.lossyScale;
            Preview.localScale=new Vector3(currentScale/scale.x,currentScale/scale.y,currentScale/scale.z);
        }
        void FinishReveal()
        {
            foreach(var v in visuals) if(v.renderer) v.renderer.sharedMaterials=v.materials;
        }
        void BeginMenuTravel()
        {
            if(!Preview || State==SandboxRewardState.Claimed) return;
            FinishReveal(); fragments.Clear(); stream.Clear(); HaloVisible=false;
            if(nextTile) nextTile.RewardPulse=true;
            // Travel starts from the completed reveal pose.
            travelScale=currentScale;
            travelStart=frame.InverseTransformPoint(Preview.position);
            travelRotation=Quaternion.Inverse(frame.rotation)*Preview.rotation;
            State=SandboxRewardState.ToMenu; age=0;
        }
        void LateUpdate() { Tick(Time.unscaledDeltaTime); DrawReminder(Time.unscaledDeltaTime); }
        void DrawReminder(float delta)
        {
            if(!Preview || cancelled) return;
            rays.Clear(); HaloVisible=false;
            if(State==SandboxRewardState.Revealing) {
                var camera=Camera.main;
                DrawRays(rays,Preview.position,camera?camera.transform.rotation:GoalRotation,HaloRadius,RaySize,Time.unscaledTime,Mathf.SmoothStep(0,1,revealProgress));
            }
            if(level.Workshop.RepositionRequested || State!=SandboxRewardState.ToMenu && State!=SandboxRewardState.AtMenu) { stream.Clear(); return; }
            streamAge+=delta;
            DrawStream(stream,Preview.position,level.Presentation.Interface.NextLevelButton.transform.position,streamAge/Mathf.Max(.01f,DeliverySeconds),.1f,1.6f);
        }
        // The looping reward visuals are static so the VFX showcase draws exactly what the game does.
        // A steady halo behind the reward with six broad fans of light that turn slowly. Each fan is eight
        // large, faint, overlapping discs that widen outwards, so it reads as one soft wedge, not as dots.
        static readonly (float along,float across)[] Fan={(.34f,0),(.56f,-.5f),(.56f,.5f),(.78f,-1f),(.78f,0),(.78f,1f),(1f,-.9f),(1f,.9f)};
        public static void DrawRays(SandboxFragments rays,Vector3 center,Quaternion facing,float rayRadius,float raySize,float time,float intensity=1)
        {
            float turn=time*.12f;
            if(rays.Points.Length>0) rays.Set(0,center+facing*new Vector3(0,0,rayRadius*.19f),rayRadius*2.9f,WarmGlow(Mathf.Clamp01((.30f+.03f*Mathf.Sin(time*1.6f))*intensity)));
            for(int i=1;i<rays.Points.Length;i++){
                int fan=(i-1)/8, dot=(i-1)%8; var shape=Fan[dot];
                // Alternate long and short fans; each breathes on its own slow phase.
                float reach=rayRadius*(fan%2==0?1:.74f), a=fan*Mathf.PI/3+turn, breath=.75f+.25f*Mathf.Sin(time*1.1f+fan*1.7f);
                float along=reach*shape.along, across=reach*.105f*shape.across*shape.along;
                var offset=new Vector3(Mathf.Cos(a)*along-Mathf.Sin(a)*across,Mathf.Sin(a)*along+Mathf.Cos(a)*across,rayRadius*.16f);
                rays.Set(i,center+facing*offset,raySize*(3.4f+3.6f*shape.along),WarmGlow(Mathf.Clamp01(.2f*(1.15f-.6f*shape.along)*breath*intensity)));
            }
            rays.Draw();
        }
        // Palette shared by the reward visuals: warm gold for success, cream for hot centres, teal for "yours to take".
        static readonly Color Gold=new Color(1,.74f,.2f), Cream=new Color(1,.95f,.78f), Teal=new Color(.38f,.86f,.82f);
        static Color WarmGlow(float alpha) { var c=Gold; c.a=alpha; return c; }
        static Color Tint(Color color,float alpha) { color.a=alpha; return color; }
        // The reveal: the part rises into place with one settling turn; t runs 0..1 over the reveal.
        public static Pose RevealPose(Pose goal,float riseDistance,float t)
        {
            float rise=1-Mathf.Pow(1-Mathf.Clamp01(t),3);
            return new Pose(goal.position-goal.rotation*Vector3.up*(riseDistance*(1-rise)),goal.rotation*Quaternion.AngleAxis(-360*(1-rise),Vector3.up));
        }
        // The surface finishes forming before the part lands.
        public static float RevealDissolve(float t) => 1-Mathf.Clamp01(t/.6f);
        // Two thirds of the sparkles spiral onto the mesh (samples are in the preview's space); the rest
        // climb in a column from under the part. reach is the part's world radius.
        public static void DrawReveal(SandboxFragments sparkles,Pose goal,Transform preview,IReadOnlyList<Vector3> samples,float reach,float riseDistance,float t,float age)
        {
            float ring=.1f+reach*1.2f, fade=1-Mathf.SmoothStep(.8f,1,t);
            int spiral=sparkles.Points.Length*2/3;
            for(int i=0;i<sparkles.Points.Length;i++) {
                if(i<spiral) {
                    // Sparkles spiral inward onto the mesh instead of sliding in straight lines.
                    Vector3 end=preview.TransformPoint(samples!=null && samples.Count>0?samples[i%samples.Count]:Vector3.zero);
                    float eased=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.75f)), angle=i*2.4f+(1-eased)*3f;
                    var start=goal.position+goal.rotation*new Vector3(Mathf.Sin(angle)*ring,-riseDistance,Mathf.Cos(angle)*ring);
                    // One in four is a large soft glow that carries the sweep; the rest are small glints that settle on the surface.
                    bool broad=i%4==0; float settle=1-eased*eased;
                    sparkles.Set(i,Vector3.Lerp(start,end,eased),broad?.034f:.011f+.003f*(i%3),
                        Tint(Color.Lerp(Cream,Gold,eased),broad?.26f*settle:settle*(.75f+.25f*Mathf.Sin(age*9+i*1.7f))));
                } else {
                    // A column of sparkles climbs from under the part and lifts it into place.
                    int k=i-spiral; float u=Mathf.Repeat(age*1.3f+k*.137f,1), angle=k*2.4f+age*2, radius=reach*.6f*(1-.5f*u);
                    sparkles.Set(i,goal.position+goal.rotation*new Vector3(Mathf.Sin(angle)*radius,(u*1.2f-1)*(riseDistance+reach),Mathf.Cos(angle)*radius),
                        k%3==0?.022f:.009f+.003f*(k%3),Tint(Gold,Mathf.Sin(u*Mathf.PI)*fade*(k%3==0?.3f:.8f)));
                }
            }
            sparkles.Draw();
        }
        // A few large beads of light travel the arc one after another, each with a short fading tail:
        // enough to point the way without drawing a dotted line. phase advances by one per trip.
        public static void DrawStream(SandboxFragments stream,Vector3 from,Vector3 to,float phase,float arc=.08f,float size=1)
        {
            const int beads=4; int count=stream.Points.Length, perBead=Mathf.Max(1,(count+beads-1)/beads);
            for(int i=0;i<count;i++){
                int bead=i%beads; float lag=(i/beads)/(float)perBead; // 0 at the bead, 1 at the end of its tail
                float u=Mathf.Repeat(phase-bead/(float)beads-lag*.09f,1), ends=Mathf.Sin(u*Mathf.PI);
                stream.Set(i,Vector3.Lerp(from,to,u)+Vector3.up*(arc*ends),Mathf.Lerp(.0125f,.003f,Mathf.Sqrt(lag))*size,
                    Tint(Color.Lerp(Cream,Gold,Mathf.Clamp01(lag*2.5f)),ends*Mathf.Lerp(.85f,.12f,lag)));
            }
            stream.Draw();
        }
        // Three comets spiral toward the fixed Next Level button.
        // Each has a large white-hot head and a tapering gold tail; t runs 0..1 over the whole flight.
        public static void DrawDelivery(SandboxFragments comets,Vector3 from,Vector3 to,float t)
        {
            const int strands=3;
            int count=comets.Points.Length, perStrand=Mathf.Max(1,(count+strands-1)/strands);
            Vector3 path=to-from; float length=path.magnitude, arc=Mathf.Max(.08f,length*.2f), swirl=Mathf.Clamp(length*.08f,.015f,.05f);
            Vector3 forward=length>.0001f?path/length:Vector3.forward, side=Vector3.Cross(forward,Vector3.up);
            side=side.sqrMagnitude>.001f?side.normalized:Vector3.right; Vector3 lift=Vector3.Cross(side,forward);
            for(int i=0;i<count;i++){
                int strand=i%strands; float lag=(i/strands)/(float)perStrand; // 0 at the head, 1 at the tail's end
                float raw=t*1.55f-strand*.1f-lag*.35f, u=Mathf.SmoothStep(0,1,Mathf.Clamp01(raw)), angle=u*Mathf.PI*4+strand*Mathf.PI*2/strands;
                var position=Vector3.Lerp(from,to,u)+Vector3.up*(arc*Mathf.Sin(u*Mathf.PI))+(side*Mathf.Cos(angle)+lift*Mathf.Sin(angle))*(swirl*Mathf.Sin(u*Mathf.PI));
                float alpha=raw<=0 || raw>=1?0:Mathf.Lerp(1,.25f,lag);
                var color=Color.Lerp(new Color(1,.97f,.8f),new Color(1,.75f,.03f),Mathf.Clamp01(lag*3)); color.a=alpha;
                comets.Set(i,position,Mathf.Lerp(.03f,.006f,Mathf.Sqrt(lag)),color);
            }
            comets.Draw();
        }
        public void Tick(float delta)
        {
            if(!Preview || !goal || cancelled || State==SandboxRewardState.Claimed) return;
            if(focus){focus.Focus=State==SandboxRewardState.Revealing && !level.Workshop.RepositionRequested;focus.Advance(delta);}
            age+=Mathf.Max(0,delta); spin=Mathf.Repeat(spin+360*Mathf.Max(0,delta)/Mathf.Max(.01f,RotationSeconds),360); HaloVisible=false;
            if(State==SandboxRewardState.Revealing) {
                float t=Mathf.Clamp01(age/Mathf.Max(.01f,RevealSeconds)); revealProgress=t;
                var pose=GoalPose; var rising=RevealPose(pose,RiseDistance,t);
                SetPose(rising.position,rising.rotation);
                dissolve.SetFloat("_Dissolve",RevealDissolve(t));
                DrawReveal(fragments,pose,Preview,samples,previewExtents.magnitude*currentScale,RiseDistance,t,age);
                if(t>=1) { FinishReveal(); BeginMenuTravel(); }
                return;
            }
            if(State==SandboxRewardState.ToMenu || State==SandboxRewardState.AtMenu) {
                var button=level.Presentation.Interface.NextLevelButton.transform;
                Vector3 target=button.position+button.right*(button.lossyScale.x*.5f+Mathf.Max(spinReach,new Vector2(previewExtents.x,previewExtents.z).magnitude)+ButtonClearance);
                // The row of parts stays square to the button; each part spins on its own.
                Quaternion rotation=button.rotation;
                float t=State==SandboxRewardState.AtMenu?1:Mathf.SmoothStep(0,1,age/Mathf.Max(.01f,PreviewTravelSeconds));
                currentScale=Mathf.Lerp(travelScale,1,t);
                SetPose(Vector3.Lerp(frame.TransformPoint(travelStart),target,t),Quaternion.Slerp(frame.rotation*travelRotation,rotation,t));
                SpinParts(t);
                if(State==SandboxRewardState.ToMenu && !level.Workshop.RepositionRequested) DrawDelivery(fragments,frame.TransformPoint(travelStart),button.position,t); else fragments.Clear();
                if(t>=1 && State!=SandboxRewardState.AtMenu) { State=SandboxRewardState.AtMenu; age=0; }
                return;
            }
        }
        // weight 0: parts face the authored way; 1: each part fully spinning about its own centre.
        void SpinParts(float weight)
        {
            var turn=Quaternion.Slerp(Quaternion.identity,Quaternion.AngleAxis(spin,Vector3.up),Mathf.Clamp01(weight));
            foreach(var s in spinners) if(s.group) { s.group.localRotation=turn; s.group.localPosition=s.slot-turn*s.center; }
        }
        public void Claim()
        {
            if(State==SandboxRewardState.Claimed) return;
            bool present=!cancelled && State!=SandboxRewardState.Dormant;
            var point=Preview?Preview.position:transform.position;float size=previewExtents.magnitude*2*currentScale;
            Cancel(); State=SandboxRewardState.Claimed;
            // The coordinator is destroyed when advancing; the level owns the final claim pulse.
            if(present && level && level.Feedback) level.Feedback.Emit(SandboxSoundCue.Claim,point,level,goal?goal.up:Vector3.up,1,SandboxSurfaceMaterial.Wood,size);
        }
        public void Cancel()
        {
            if(level && level.Vfx) level.Vfx.ClearSource(this);
            cancelled=true; HaloVisible=false; SandboxRewardHandOutline.Requested=false; rays?.Release(); rays=null;
            if(focus)focus.Restore();
            if(nextTile) nextTile.RewardPulse=false;
            fragments?.Release(); fragments=null; stream?.Release(); stream=null;
            if(Preview) { Preview.gameObject.SetActive(false); Destroy(Preview.gameObject); Preview=null; }
            if(dissolve) Destroy(dissolve);
            foreach(var part in temporarySources) if(part) Destroy(part.gameObject);
            temporarySources.Clear();
            visuals.Clear(); samples.Clear(); spinners.Clear(); spinReach=0;
        }
        void OnDisable() { Cancel(); }
        void OnDestroy() { Cancel(); }
    }
}
