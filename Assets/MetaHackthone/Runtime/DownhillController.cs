using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicalAssembly
{
    public enum DownhillState { Build, Transition, Running, Success, Failed, Countdown }

    public sealed partial class DownhillController : MonoBehaviour, IBuildModeTarget, IAssemblyInteractionRules, IAssemblyCollisionRules
    {
        public const string BuildInstructions="WEIGHTLESS BUILDING\nPinch to lift; release near a socket to attach.\nTurn the wheel to inspect your design; launch heading stays fixed.\nSTART begins the countdown.";
        public WorkshopBootstrap Workshop { get; private set; }
        public DownhillSettings Settings { get; private set; }
        public DownhillCourse Course { get; private set; }
        public DownhillPresentation Presentation { get; private set; }
        public float BuildScale => Presentation.BuildScale;
        public float BuildingLayoutScale => ActiveLayout?ActiveLayout.BuildingScale:1f;
        public float TestScale => Presentation.TestScale*(!FactoryLevel && (Settings.Campaign || DesignModeSession.Enabled) && CurrentCourse==SandboxCourseId.TeleportPipes?Settings.Level2VehicleScale:1f);
        // Garage comparisons use one physical test scale, independent of the authored palette size.
        public float TransferScale => TestScale/(Settings.UseGravityGarage && Core?Core.GeometryScale:BuildScale);
        public AssemblyPart Core { get; private set; }
        public AssemblyPart SimulationCore { get; private set; }
        public AssemblyWorld SimulationWorld { get; private set; }
        public DownhillState State { get; private set; }=DownhillState.Build;
        public const float LaunchDelay=3f;
        public float CountdownRemaining => State==DownhillState.Countdown?Mathf.Max(0,physicsStartTime-Time.unscaledTime):0;
        public bool SupportsModeGesture => false;
        public bool IsBuilding => State==DownhillState.Build;
        public ModeGestureDirection GestureDirection => IsBuilding?ModeGestureDirection.Open:ModeGestureDirection.Close;
        public bool CanSwitchMode => !SandboxFirstEntryIntro.Playing && !VictoryInProgress && (!GarageComparing || garageAdvancing) && State!=DownhillState.Transition && !Workshop.World.HasHolds && !Workshop.World.IsBusy && !Workshop.RepositionRequested;
        public int BuildCyclesUsed => 1;
        public BuildModeGesture ModeGesture { get; }=new();
        public ChallengeGestureFeedback GestureFeedback { get; private set; }
        public SandboxAudioController Audio { get; private set; }
        public SandboxPresentation Feedback { get; private set; }
        public SandboxVfxController Vfx { get; private set; }
        public DownhillRotationWheel RotationWheel => Course.RotationWheel;
        public DownhillSpawnPreview SpawnPreview { get; private set; }
        public bool Rotating => rotationHand>=0;
        public bool IsRotatingWithHand(int hand) => Rotating && rotationHand==hand;
        public IReadOnlyList<AssemblyPart> Kit => kit;
        readonly List<AssemblyPart> kit=new();
        readonly Dictionary<AssemblyPart,BodyPose[]> spawns=new();
        sealed class ReturnGroup
        {
            public HashSet<AssemblyPart> Parts;
            public float Remaining;
        }
        readonly List<ReturnGroup> returns=new();
        public int PendingReturnCount => returns.Count;
        readonly HashSet<AssemblyPart> previouslyHeld=new();
        readonly HashSet<AssemblyPart> connected=new();
        readonly HashSet<Collider> buildBlockers=new();
        readonly List<BodyPose> saved=new(),moving=new();
        readonly GrabLatch[] wheelGrips={new(),new()};
        readonly float[] lastTracked=new float[2];
        GameObject simulationRoot;
        int rotationHand=-1;
        Vector3 rotationOrigin,rotationCenter,rotationPivot;
        Quaternion wheelStartRotation;
        TextMesh buttonLabel;
        float elapsed,stalled,physicsStartTime;
        Coroutine transition;
        BodyPose[] testStart;
        float previousTimeScale,previousFixedStep;
        bool ownsClock;
        int countdownNumber;
        struct BodyPose
        {
            public AssemblyBody Body;
            public Vector3 Position;
            public Quaternion Rotation;
        }
        public void Initialize(WorkshopBootstrap workshop,DownhillSettings settings,DownhillCourse course=null)
        {
            Workshop=workshop; Settings=settings; InitializeCampaign(); Course=course?course:DownhillCourse.Build(settings,workshop.Arena);
            var initial=FindLayout(CurrentCourse);
            if(!initial && Workshop.Layout.GetComponent<SandboxFactoryLevel>()) initial=Workshop.Layout.GetComponent<SandboxFactoryLevel>().Layout;
            if(initial) {
                if(!initial.Validate(out var layoutError)) throw new InvalidOperationException(layoutError);
                foreach(var item in Workshop.Layout.LevelLayouts) if(item) item.gameObject.SetActive(item==initial);
                initial.Bind(Workshop.Layout); Course=initial.Course;
            }
            workshop.World.Rules=this; workshop.World.HoldsChanged+=HoldsChanged; workshop.World.TopologyChanged+=TopologyChanged;
            Course.EnsureControls(settings);
            Course.ConfigurePhysics();
            Presentation=Course.GetComponent<DownhillPresentation>();
            if(!Presentation) Presentation=Course.gameObject.AddComponent<DownhillPresentation>();
            Presentation.Initialize(Course,settings,Workshop.Layout); Presentation.Configure();
            workshop.World.FloatingBuild=true; workshop.World.ConstrainBuildMotion=true; workshop.World.QuarterTurnBlocks=true;
            workshop.World.BuildSpring=settings.BuildSpring;
            workshop.World.SocketDistance=settings.SnapRadius*(ActiveLayout?ActiveLayout.BuildingScale:1f); workshop.World.AttachDistance=Mathf.Max(.10f,settings.SnapRadius+.01f)*(ActiveLayout?ActiveLayout.BuildingScale:1f);
            if(settings.DesignerBuilder) { DragBuilder=gameObject.AddComponent<SandboxDragBuilder>(); DragBuilder.Initialize(this); }
            RotationWheel.Controller=this;
            Course.ModeButton.Pressed=()=>TrySwitchMode();
            buttonLabel=Course.ModeButton.GetComponentInChildren<TextMesh>();
            SpawnPreview=new GameObject("Sandbox car preview").AddComponent<DownhillSpawnPreview>();
            SpawnPreview.Initialize(this);
            Workshop.RebindLevelPresentation();
            Feedback=GetComponent<SandboxPresentation>(); if(!Feedback) Feedback=gameObject.AddComponent<SandboxPresentation>(); Feedback.Initialize(this);
            Audio=GetComponent<SandboxAudioController>(); if(!Audio) Audio=gameObject.AddComponent<SandboxAudioController>(); Audio.Initialize(this);
            Vfx=GetComponent<SandboxVfxController>(); if(!Vfx) Vfx=gameObject.AddComponent<SandboxVfxController>(); Vfx.Initialize(Feedback,settings.Vfx);
            if(Application.isEditor) { var panel=gameObject.AddComponent<GravityGaragePanel>(); panel.Level=this; }
        }
        public SandboxDragBuilder DragBuilder { get; private set; }

        public bool IsCoreConnected(AssemblyPart part) => part && connected.Contains(part);
        public bool IsReturning(AssemblyPart part) => boundaryReturns.ContainsKey(part);
        public void SpawnKit()
        {
            if(kit.Count>0) return;
            if(Settings.DesignerBuilder) { SpawnDesignerKit(); return; }
            if(Settings.UseGravityGarage) { LoadGaragePreset(GravityGaragePreset.BigFront); return; }
            var kinds=new[]{PartKind.DownhillLongBlock,PartKind.DownhillLargeWheel,PartKind.DownhillLargeWheel,
                PartKind.DownhillSmallWheel,PartKind.DownhillSmallWheel,PartKind.DownhillShortBlock,PartKind.DownhillTriangle,PartKind.DownhillIrregular};
            for(int i=0;i<kinds.Length;i++)
            {
                var markers=Workshop.Layout.Spawns;
                // Empty optional slots deliberately remove parts from the authored kit.
                // Keep slot indices stable so later parts retain their kind and pose.
                if(i>0 && markers!=null && (i>=markers.Length || !markers[i])) continue;
                float size=markers!=null && i<markers.Length && markers[i]?markers[i].SizeMultiplier:1f;
                var part=DownhillParts.Create(Workshop.World,kinds[i],Presentation.SpawnPose(i),BuildScale*size,Settings,gridVersion:i==0?4:3,buildingCore:i==0);
                kit.Add(part); spawns[part]=Capture(part.Bodies).ToArray(); if(i==0) Core=part;
            }
            TopologyChanged(); Workshop.Session.Select(null);
            Workshop.Session.Notify(Settings.DesignerBuilder?DesignerInstructions:BuildInstructions);
            UpdateControls();
        }
        public bool TryLoadDesignPreset()
        {
            if(FactoryLevel && CurrentCourse==SandboxCourseId.BeautifulCorner && FactoryLevel.UseStarterPalette) return TryLoadDesignerExample();
            if(FactoryLevel) return FactoryLevel.TestCar && TryLoadFactoryCar(FactoryLevel.TestCar);
            if(!DesignModeSession.Enabled) return false;
            if(Settings.DesignerBuilder) return TryLoadDesignerExample();
            if(Settings.UseGravityGarage) return LoadGaragePreset(GravityGaragePreset.BigFront);
            if(!DesignModeSession.Enabled || !IsBuilding || !CanSwitchMode || Rotating || !Core) return false;
            var world=Workshop.World;
            var wheels=kit.Where(p=>p.Kind==PartKind.DownhillLargeWheel).ToArray();
            if(wheels.Length!=2 || kit.Any(p=>!spawns.ContainsKey(p))) return false;
            var sockets=new AssemblySocket[2];
            for(int i=0;i<2;i++)
            {
                var direction=i==0?Vector3.right:Vector3.left;
                sockets[i]=Core.Sockets.Where(s=>Vector3.Dot(s.transform.localRotation*Vector3.forward,direction)>.99f)
                    .OrderBy(s=>s.Id,System.StringComparer.Ordinal).FirstOrDefault();
                if(!sockets[i] || !wheels[i].FindSocket("mount")) return false;
            }
            var previous=Capture(kit.SelectMany(p=>p.Bodies)).ToArray();
            var edges=world.Connections.Select(e=>JsonUtility.FromJson<ConnectionRecord>(JsonUtility.ToJson(e))).ToArray();
            try
            {
                foreach(var edge in world.Connections.ToArray()) world.RemoveConnection(edge.id);
                foreach(var part in kit) Restore(spawns[part]);
                for(int i=0;i<2;i++)
                {
                    var wheel=wheels[i]; var mount=wheel.FindSocket("mount"); var socket=sockets[i];
                    var rotation=Core.MainBody.Rigidbody.rotation*Quaternion.Euler(0,i==0?0:180,0);
                    foreach(var body in wheel.Bodies)
                    {
                        body.Rigidbody.position=socket.WorldPosition-rotation*mount.transform.localPosition;
                        body.Rigidbody.rotation=rotation;
                        body.transform.SetPositionAndRotation(body.Rigidbody.position,rotation);
                    }
                    Physics.SyncTransforms();
                    if(!world.Connect(new ConnectionRecord {
                        id=Guid.NewGuid().ToString("N"),a=new BodyRef(wheel.InstanceId,mount.Body.Id),b=new BodyRef(Core.InstanceId,socket.Body.Id),
                        socketA=mount.Id,socketB=socket.Id,anchorA=mount.transform.localPosition,anchorB=socket.transform.localPosition }))
                        throw new InvalidOperationException("Could not attach the test wheel.");
                }
                Workshop.Session.Select(Core);
                Workshop.Session.Notify("Design Mode test car ready. Press TEST to run the course under gravity.");
                return true;
            }
            catch(Exception error)
            {
                foreach(var edge in world.Connections.ToArray()) world.RemoveConnection(edge.id);
                Restore(previous);
                var rules=world.Rules; world.Rules=null;
                try { foreach(var edge in edges) if(!world.Connect(edge)) Debug.LogError("Could not restore test construction: "+edge.id); }
                finally { world.Rules=rules; TopologyChanged(); }
                Workshop.Session.Notify("Test car could not load: "+error.Message); return false;
            }
        }
        AssemblyPart Spawn(PartKind kind,Vector3 position,BuildingPartSpawn marker)
        {
            var p=DownhillParts.Create(Workshop.World,kind,marker?marker.Pose:new Pose(Course.WorkbenchFrame.TransformPoint(position),Course.WorkbenchFrame.rotation*(DownhillParts.IsWheel(kind)?Quaternion.Euler(0,90,0):Quaternion.identity)),1,Settings);
            kit.Add(p); spawns[p]=Capture(p.Bodies).ToArray(); return p;
        }
        IEnumerable<BodyPose> Capture(IEnumerable<AssemblyBody> bodies)
        {
            foreach(var body in bodies) yield return new BodyPose { Body=body,Position=Course.WorkbenchFrame.InverseTransformPoint(body.gameObject.activeInHierarchy?body.Rigidbody.position:body.transform.position),Rotation=Quaternion.Inverse(Course.WorkbenchFrame.rotation)*(body.gameObject.activeInHierarchy?body.Rigidbody.rotation:body.transform.rotation) };
        }
        static void StopBody(AssemblyBody b)
        { if(!b.Rigidbody.isKinematic) { b.Rigidbody.linearVelocity=Vector3.zero; b.Rigidbody.angularVelocity=Vector3.zero; } }
        void Restore(IEnumerable<BodyPose> poses)
        {
            foreach(var p in poses) if(p.Body)
            {
                var b=p.Body; StopBody(b);
                var position=Course.WorkbenchFrame.TransformPoint(p.Position); var rotation=Course.WorkbenchFrame.rotation*p.Rotation;
                // Inactive rigidbodies can return their old physics pose after a setter.
                b.transform.SetPositionAndRotation(position,rotation);
                b.Rigidbody.position=position; b.Rigidbody.rotation=rotation;
            }
            Physics.SyncTransforms();
        }
        void TopologyChanged()
        {
            if(!isActiveAndEnabled || !Core || !Core.MainBody || !Core.MainBody.Rigidbody || !IsBuilding) return;
            var previous=connected.ToArray();
            connected.Clear(); connected.UnionWith(Workshop.World.GetConnectedParts(Core.InstanceId));
            buildBlockers.Clear();
            foreach(var part in connected) foreach(var body in part.Bodies)
                foreach(var collider in body.Colliders) buildBlockers.Add(collider);
            returns.RemoveAll(group=>group.Parts.Overlaps(connected));
            Workshop.World.Collisions.Release("downhill-build");
            foreach(var p in kit.Where(p=>p)) foreach(var b in p.Bodies)
            {
                if(!b || !b.Rigidbody) continue;
                bool anchor=connected.Contains(p); b.Anchored=anchor;
                if(!Workshop.World.IsSuspended(p)) { StopBody(b); b.Rigidbody.isKinematic=true; b.Rigidbody.useGravity=false; b.Rigidbody.linearDamping=b.Rigidbody.angularDamping=1; b.Rigidbody.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative; }
            }
            var parts=connected.ToArray();
            for(int i=0;i<parts.Length;i++) for(int j=i+1;j<parts.Length;j++)
                foreach(var a in parts[i].Bodies.SelectMany(b=>b.Colliders)) foreach(var b in parts[j].Bodies.SelectMany(b=>b.Colliders)) Workshop.World.Collisions.Acquire(a,b,"downhill-build");
            returns.Clear();
        }
        void ScheduleReturn(AssemblyPart part)
        {
            if(Workshop.World.FloatingBuild) return;
            if(!part || part==Core || connected.Contains(part) || returns.Any(g=>g.Parts.Contains(part))) return;
            var group=new HashSet<AssemblyPart>(Workshop.World.GetConnectedParts(part.InstanceId));
            if(group.Any(p=>!spawns.ContainsKey(p) || Workshop.World.IsSuspended(p))) return;
            returns.RemoveAll(g=>g.Parts.Overlaps(group));
            returns.Add(new ReturnGroup { Parts=group,Remaining=Settings.ReturnDelay });
        }
        void HoldsChanged()
        {
            var held=new HashSet<AssemblyPart>();
            for(int hand=0;hand<3;hand++) held.UnionWith(Workshop.World.HeldParts(hand));
            returns.RemoveAll(group=>group.Parts.Overlaps(held));
            foreach(var p in previouslyHeld) if(p && !held.Contains(p)) ScheduleReturn(p);
            previouslyHeld.Clear(); previouslyHeld.UnionWith(held);
        }
        public void TickReturns(float seconds)
        {
            foreach(var group in returns.ToArray())
            {
                if(group.Parts.Any(p=>!p || connected.Contains(p))) { returns.Remove(group); continue; }
                if(group.Parts.Any(Workshop.World.IsSuspended)) continue;
                group.Remaining-=seconds;
                if(group.Remaining>0) continue;
                returns.Remove(group);
                var ids=new HashSet<string>(group.Parts.Select(p=>p.InstanceId));
                foreach(var edge in Workshop.World.Connections.Where(c=>ids.Contains(c.a.partId) || ids.Contains(c.b.partId)).ToArray()) Workshop.World.RemoveConnection(edge.id);
                foreach(var part in group.Parts) Restore(spawns[part]);
                Workshop.World.Collisions.Reapply();
            }
        }
        void Update()
        {
            if(Course && Course.SandboxPolish) { TickSandboxPolish(Time.unscaledDeltaTime); TickSoulStream(Time.unscaledDeltaTime); }
            TickReturns(Time.deltaTime);
            if(State==DownhillState.Countdown) UpdateCountdown();
            UpdateControls();
        }
        void UpdateCountdown()
        {
            if(CampaignPresentation) { if(countdownNumber!=0) { countdownNumber=0; Presentation.SetCountdown(""); } return; }
            int seconds=Mathf.Clamp(Mathf.CeilToInt((CountdownRemaining-Settings.GoDuration)/Settings.CountdownStep),0,3);
            if(seconds==countdownNumber) return;
            countdownNumber=seconds;
            Presentation.SetCountdown(seconds>0?seconds.ToString():"GO");
            if(seconds>0 && Feedback) Feedback.Emit(SandboxSoundCue.Countdown,Course.StartPoint.position);
            Workshop.Session.Notify(seconds>0?$"STARTING IN {seconds}":"GO");
        }
        void UpdateControls()
        {
            if(!Course) return;
            if(Presentation && Presentation.Interface) {
                Presentation.Interface.SetNextLevelAvailable(CanAdvanceLevel);
                // Unlocking courses pulse once their reward reaches the button; the rest pulse as soon as Next Level is offered.
                var next=Presentation.Interface.NextLevelButton?Presentation.Interface.NextLevelButton.GetComponent<FloatingStartButton>():null;
                if(next && !HasReward) next.RewardPulse=CanAdvanceLevel;
            }
            Course.ModeButton.Interactable=Core && CanSwitchMode && !Rotating;
            if(buttonLabel) buttonLabel.text="START";
            if(Presentation)
            {
                Presentation.RetryButton.Interactable=!VictoryInProgress && !GarageComparing && State!=DownhillState.Build && State!=DownhillState.Transition;
                Presentation.BackButton.Interactable=!VictoryInProgress && !GarageComparing && State!=DownhillState.Build && State!=DownhillState.Transition;
                Presentation.ExitButton.Interactable=!GarageComparing;
                if(Presentation.Interface && Presentation.Interface.ResetButton)
                    Presentation.Interface.ResetButton.Interactable=IsBuilding && CanSwitchMode && !Rotating;
            }
            if(RotationWheel) RotationWheel.gameObject.SetActive(IsBuilding);
        }
        public bool PrepareGrab(int hand,AssemblyPart part,Pose pose)
        {
            return IsBuilding && !IsReturning(part) && part!=Core && hand!=rotationHand && !Workshop.World.IsBusy && (!connected.Contains(part) || !Rotating);
        }
        public AssemblyPart BranchRoot => Core;
        public bool IsBuildBlocker(Collider collider) => IsBuilding && collider && (Settings.DesignerBuilder?Workshop.World.IsAssemblyPart(collider.GetComponentInParent<AssemblyPart>()):buildBlockers.Contains(collider));
        public bool CanAttachFromHand(int hand) => IsBuilding && !Rotating;
        public bool CanTransferHold(int fromHand,int toHand,AssemblyPart part)
            => IsBuilding && fromHand>=0 && fromHand<2 && toHand>=0 && toHand<2 && fromHand!=toHand && toHand!=rotationHand;
        public bool CanAttach(AssemblyPart source,AssemblyPart target,AssemblySocket socket)
            => IsBuilding && !IsReturning(source) && !IsReturning(target) && !Rotating && source!=Core && !connected.Contains(source) && (!DragBuilder || DragBuilder.Compatible(source,target) && !Workshop.World.GetConnectedParts(source.InstanceId).Contains(target)) && Exposed(socket,source);
        public bool CanSearchTarget(AssemblyPart part,AssemblySocket socket,AssemblyPart source) => IsBuilding && !IsReturning(part) && !IsReturning(source) && !Rotating && part!=source && (!DragBuilder || DragBuilder.Compatible(source,part) && DragBuilder.SocketAvailable(socket)) && Exposed(socket,source);
        bool Exposed(AssemblySocket socket,AssemblyPart ignored)
        {
            if(!socket) return false;
            foreach(var hit in Physics.OverlapSphere(socket.WorldPosition+socket.Normal*(.002f*BuildingLayoutScale),.001f*BuildingLayoutScale,~0,QueryTriggerInteraction.Ignore))
            {
                var b=hit.GetComponentInParent<AssemblyBody>();
                if(IsBuildBlocker(hit) && b && b.Part!=socket.Body.Part && b.Part!=ignored) return false;
            }
            return true;
        }
        public bool CanDetach(AssemblyPart part)
        {
            if(!IsBuilding || !part || part==Core || (Rotating && connected.Contains(part))) return false;
            return true;
        }
        readonly List<Vector3> colliderVertices=new();
        internal IEnumerable<Vector3> ColliderPoints(Collider collider)
        {
            // Work from shape-local bounds. A world AABB grows when the arena turns,
            // incorrectly lifting cars above the ramp.
            Bounds b;
            if(collider is BoxCollider box) b=new Bounds(box.center,box.size);
            else if(collider is MeshCollider mesh && mesh.sharedMesh)
            {
                mesh.sharedMesh.GetVertices(colliderVertices);
                foreach(var vertex in colliderVertices) yield return collider.transform.TransformPoint(vertex);
                yield break;
            }
            else
            {
                b=collider.bounds;
                for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
                    yield return b.center+Vector3.Scale(b.extents,new Vector3(x,y,z));
                yield break;
            }
            for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
                yield return collider.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z)));
        }
        // Shared by the mesh preview and the physics copy. Geometry, rather than
        // world AABBs, gives the same ramp clearance at every arena heading.
        public Pose SandboxPlacement(out Quaternion delta)
        {
            if(FactoryLevel) Course.StartPoint.SetPositionAndRotation(FactoryLevel.Start.position,FactoryLevel.Start.rotation);
            if(Settings.Level3D && Course3D){var pose=Course3D.ReleasePose(connected.ToArray(),Core,TransferScale);delta=pose.rotation*Quaternion.Inverse(Core.MainBody.Rigidbody.rotation);return pose;}
            if(Settings.Level25D && Course25D){var pose=GravityGarage25DVehicle.ReleasePose(connected.ToArray(),Core,Course25D,TransferScale);delta=pose.rotation*Quaternion.Inverse(Core.MainBody.Rigidbody.rotation);return pose;}
            if(GarageCourse && GarageCourse.Markers) {
                var marker=GarageCourse.Markers.Start;
                Course.StartPoint.SetPositionAndRotation(marker.position,marker.rotation);
            }
            var target=Course.StartPoint?Course.StartPoint.position:Course.SandboxFrame.TransformPoint(Settings.StartPosition);
            var rotation=Course.StartPoint?Course.StartPoint.rotation:Course.SandboxFrame.rotation*Settings.StartRotation;
            var up=rotation*Vector3.up;
            if(Settings.UseGravityGarage) rotation=GarageReleaseRotation(rotation);
            delta=rotation*Quaternion.Inverse(Core.MainBody.Rigidbody.rotation);
            var origin=Core.MainBody.Rigidbody.position;
            float lowest=float.PositiveInfinity;
            Physics.SyncTransforms();
            foreach(var part in connected) foreach(var body in part.Bodies) foreach(var collider in body.Colliders)
                foreach(var point in ColliderPoints(collider))
                    lowest=Mathf.Min(lowest,Vector3.Dot(delta*(point-origin)*TransferScale,up));
            float clearance=Settings.UseGravityGarage?.003f*GarageWorldScale:.003f;
            return new Pose(target+up*(clearance-lowest),delta*Core.MainBody.Rigidbody.rotation);
        }
        bool OutsideAuthoredCourse(Vector3 worldPoint)
        {
            var area=Course.PlayArea; var point=area.transform.InverseTransformPoint(worldPoint); var bounds=area.LocalBounds;
            return point.y<bounds.min.y || point.z<bounds.min.z || point.z>bounds.max.z || point.x>bounds.max.x;
        }
        void LateUpdate() { if(IsBuilding && Presentation) Presentation.FollowCore(Core); }
        public bool BeginRotation(int hand,Vector3 point)
        {
            if(!Core || !IsBuilding || Rotating || Workshop.World.HeldPart(hand) || Workshop.World.IsBusy || Workshop.RepositionRequested) return false;
            Presentation.FollowCore(Core);
            rotationCenter=RotationWheel.transform.position; rotationPivot=Core.MainBody.Rigidbody.position;
            rotationOrigin=Vector3.ProjectOnPlane(point-rotationCenter,Course.WorkbenchFrame.up);
            if(rotationOrigin.sqrMagnitude<.0025f*BuildingLayoutScale*BuildingLayoutScale) return false;
            rotationHand=hand; moving.Clear(); moving.AddRange(Capture(connected.SelectMany(p=>p.Bodies)));
            wheelStartRotation=RotationWheel.transform.rotation;
            ResetWheelSteps();
            Workshop.World.ExternalHold=true; UpdateControls(); return true;
        }
        Vector3 rotationTarget; bool rotationPending;
        public void Rotate(int hand,Vector3 point) { if(hand==rotationHand && Rotating){rotationTarget=point;rotationPending=true;} }
        void ApplyRotation(int hand,Vector3 point)
        {
            if(hand!=rotationHand || !Rotating) return;
            var direction=Vector3.ProjectOnPlane(point-rotationCenter,Course.WorkbenchFrame.up);
            if(direction.sqrMagnitude<.0025f*BuildingLayoutScale*BuildingLayoutScale) return;
            float angle=Vector3.SignedAngle(rotationOrigin,direction,Course.WorkbenchFrame.up);
            if(CampaignPresentation){ApplyWheelStep(angle);return;}
            var delta=Quaternion.AngleAxis(angle,Course.WorkbenchFrame.up);
            var pivot=rotationPivot;
            float groupRadius=moving.Max(s=>Vector3.Distance(s.Body.Rigidbody.position,pivot));
            foreach(var s in moving)
            {
                var rb=s.Body.Rigidbody;
                var targetRotation=delta*Course.WorkbenchFrame.rotation*s.Rotation;
                float radius=groupRadius;
                float limit=Mathf.Min(180,Mathf.Rad2Deg*1.5f/Mathf.Max(.05f,radius))*Time.fixedDeltaTime;
                var next=Quaternion.RotateTowards(rb.rotation,targetRotation,limit);
                var turn=next*Quaternion.Inverse(rb.rotation);
                rb.MovePosition(pivot+turn*(rb.position-pivot));rb.MoveRotation(next);
            }
            // The grouped wheel inherits its turn from the grid. Do not rotate it twice.
            Presentation.FollowCore(Core);
            if(!RotationWheel.transform.IsChildOf(Presentation.WorkbenchVisuals))
                RotationWheel.transform.rotation=delta*wheelStartRotation;
            Physics.SyncTransforms();
        }
        public void EndRotation(int hand)
        {
            if(hand!=rotationHand || !Rotating) return;
            ResetWheelSteps();
            rotationPending=false; rotationHand=-1; Workshop.World.ExternalHold=false; moving.Clear(); UpdateControls();
        }
        public void CancelRotation()
        {
            if(Rotating) EndRotation(rotationHand);
            foreach(var latch in wheelGrips) latch.Reset();
        }
        readonly float[] wheelOpenSince={-1,-1};
        public bool SampleWheel(int hand,bool tracked,float distance,bool pinch,float grip,Pose pose,bool allow,float now)
        {
            if(!tracked)
            {
                wheelOpenSince[hand]=-1;
                if(rotationHand==hand) {rotationPending=false;SuspendWheelSteps();}
                else wheelGrips[hand].Reset();
                return rotationHand==hand;
            }
            lastTracked[hand]=now;
            bool before=wheelGrips[hand].IsClosed,closed=wheelGrips[hand].Update(true,distance,pinch,0);
            if(rotationHand==hand)
            {
                if(distance>=.03f) {
                    rotationPending=false;
                    if(wheelOpenSince[hand]<0)wheelOpenSince[hand]=now;
                    if(now-wheelOpenSince[hand]>=.08f)EndRotation(hand);
                } else {wheelOpenSince[hand]=-1;Rotate(hand,pose.position);}
                return true;
            }
            if(allow && IsBuilding && closed && !before && RotationWheel && RotationWheel.IsNearRim(pose.position))
                return BeginRotation(hand,pose.position);
            return false;
        }
        public bool TrySwitchMode() => IsBuilding?TryStart():TryReturn();
        public bool TryStart()
        {
            if(!IsBuilding || !CanSwitchMode || Rotating) return false;
            if(FactoryLevel && !FactoryLevel.Validate(out var factoryError)) { Workshop.Session.Notify(factoryError); return false; }
            if(ActiveLayout && !ActiveLayout.Validate(out var layoutError)) { Workshop.Session.Notify(layoutError); return false; }
            if(!Core || connected.Count==0) return false;
            CompleteBoundaryReturns();
            
            State=DownhillState.Transition; saved.Clear(); saved.AddRange(Capture(kit.SelectMany(p=>p.Bodies)));
            Workshop.World.EditingEnabled=false; Workshop.Session.ClearInput(); returns.Clear();
            try
            {
                simulationRoot=new GameObject("Sandbox test vehicle"); SimulationWorld=simulationRoot.AddComponent<AssemblyWorld>();
                if(FactoryLevel) {
                    FactoryLevel.ResetPuzzles(false);
                    simulationRoot.AddComponent<SandboxPuzzleRun>().Level=this;
                    simulationRoot.AddComponent<SandboxFactoryPathAssist>().Level=this;
                }
                var originals=connected.ToArray(); var map=new Dictionary<string,AssemblyPart>();
                float scale=TransferScale;
                var origin=Core.MainBody.Rigidbody.position;
                var placement=SandboxPlacement(out var delta);
                var target=placement.position+Course.SandboxFrame.parent.TransformVector(Presentation.TestPosition-Presentation.BuildPosition);
                var destinations=new List<(AssemblyBody body,Vector3 position,Quaternion rotation)>();
                foreach(var p in originals)
                {
                    var clone=DownhillParts.Create(SimulationWorld,p.Kind,new Pose(target,delta*p.MainBody.Rigidbody.rotation),p.GeometryScale*TransferScale,Settings,p.GridVersion,p.BuildingCore,p.DesignWheelRadius);
                    clone.transform.SetParent(simulationRoot.transform,true); map[p.InstanceId]=clone; simulationOriginals[clone]=p;
                    foreach(var body in clone.Bodies)
                    {
                        var source=p.FindBody(body.Id).Rigidbody; body.Rigidbody.isKinematic=true;
                        destinations.Add((body,target+delta*(source.position-origin)*scale,delta*source.rotation));
                        body.Rigidbody.useGravity=false;
                        body.Rigidbody.position=origin+(source.position-origin)*scale; body.Rigidbody.rotation=source.rotation;
                        body.transform.SetPositionAndRotation(body.Rigidbody.position,body.Rigidbody.rotation);
                    }
                }
                Physics.SyncTransforms();
                foreach(var original in Workshop.World.Connections.Where(e=>map.ContainsKey(e.a.partId)&&map.ContainsKey(e.b.partId)))
                {
                    var edge=new ConnectionRecord { id=Guid.NewGuid().ToString("N"),a=new BodyRef(map[original.a.partId].InstanceId,original.a.bodyId),b=new BodyRef(map[original.b.partId].InstanceId,original.b.bodyId),socketA=original.socketA,socketB=original.socketB,attachmentOrder=original.attachmentOrder,hierarchyEdge=original.hierarchyEdge,parentIsA=original.parentIsA,anchorA=original.anchorA*scale,anchorB=original.anchorB*scale };
                    if(!SimulationWorld.Connect(edge)) throw new InvalidOperationException("Could not reconstruct vehicle connection.");
                }
                SimulationCore=map[Core.InstanceId];
                if(Feedback) Feedback.BindSimulation();
                if(Audio) Audio.BindSimulation();
                // Ignore contact within the welded vehicle, including non-adjacent blocks.
                var bodies=SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies).ToArray();
                for(int i=0;i<bodies.Length;i++) for(int j=i+1;j<bodies.Length;j++)
                    foreach(var a in bodies[i].Colliders) foreach(var b in bodies[j].Colliders) SimulationWorld.Collisions.Acquire(a,b,"vehicle");
                StopSoulStream(); // hand the particle budget to the launch transfer
                if(CampaignPresentation)PrepareLaunchEffects(destinations);
                foreach(var p in kit) p.gameObject.SetActive(false);
                Workshop.World.EditingEnabled=false; SimulationWorld.EditingEnabled=false;
                Presentation.SetBuilding(false); ModeGesture.Reset(true); SpawnPreview.Hide();
                if(Feedback) Feedback.BeginTransfer(Core.MainBody.transform,Course.StartPoint,CampaignPresentation?LaunchDecomposeSeconds:.15f,CampaignPresentation?LaunchTravelSeconds:Mathf.Max(.01f,Settings.TransitionDuration-.15f));
                transition=StartCoroutine(CampaignPresentation?LaunchTransfer():Approach(destinations)); UpdateControls(); return true;
            }
            catch(Exception error)
            {
                RestoreBuildImmediately();
                SpawnPreview.Refresh(); UpdateControls();
                Workshop.Session.Notify("Transfer failed; build preserved: "+error.Message); return false;
            }
        }
        IEnumerator Approach(List<(AssemblyBody body,Vector3 position,Quaternion rotation)> destinations)
        {
            var starts=destinations.Select(d=>new Pose(d.body.Rigidbody.position,d.body.Rigidbody.rotation)).ToArray();
            float elapsed=0;
            do
            {
                elapsed+=Time.unscaledDeltaTime;
                float t=Settings.TransitionDuration<=0?1:Mathf.Clamp01(elapsed/Settings.TransitionDuration);
                t=Settings.TransitionEasing.Evaluate(t);
                Presentation.SetCoursePosition(Vector3.Lerp(Presentation.BuildPosition,Presentation.TestPosition,t));
                for(int i=0;i<destinations.Count;i++)
                {
                    var item=destinations[i]; var rb=item.body.Rigidbody;
                    rb.position=Vector3.Lerp(starts[i].position,item.position,t); rb.rotation=Quaternion.Slerp(starts[i].rotation,item.rotation,t);
                    rb.transform.SetPositionAndRotation(rb.position,rb.rotation);
                }
                Physics.SyncTransforms();
                if(elapsed<Settings.TransitionDuration) yield return null;
            } while(elapsed<Settings.TransitionDuration);
            testStart=Capture(SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies)).ToArray();
            transition=null;
            if(FactoryLevel) { State=DownhillState.Countdown; ResetRun(true); }
            else BeginCountdown();
        }
        void BeginCountdown()
        {
            if(Audio) Audio.StopVehicle();
            if(Feedback) Feedback.CompleteTransfer(SimulationCore?SimulationCore.MainBody.transform.position:Course.StartPoint.position,SimulationCore);
            elapsed=stalled=0; countdownNumber=-1; finishBraking=false;
            float countdown=Settings.CountdownStep*3+Settings.GoDuration;
            // Campaign: no 3-2-1. A brief rest only, never longer than the configured countdown.
            physicsStartTime=Time.unscaledTime+(CampaignPresentation?Mathf.Min(Settings.LaunchPause,countdown):countdown);
            State=DownhillState.Countdown; UpdateCountdown(); UpdateControls();
            var desktop=Workshop.GetComponent<DesktopInputAdapter>();if((Settings.Level3D || FactoryLevel) && desktop && desktop.IsDesktop && desktop.View)desktop.FrameGarage();
        }
        // Internal: the first-entry intro runs its own cars under the same miniature physics clock.
        internal void SetTestClock()
        {
            if(ownsClock) return;
            previousTimeScale=Time.timeScale; previousFixedStep=Time.fixedDeltaTime; ownsClock=true;
            float speed=Settings.UseGravityGarage?GaragePlaybackRate:Mathf.Clamp(Settings.PhysicsSpeed,.25f,1);
            Time.timeScale=previousTimeScale*speed;
            // Miniature scale must not multiply the real-time physics workload.
            // Two CCD steps per 90 Hz frame keep the valley simulation bounded.
            Time.fixedDeltaTime=Settings.Level3D?speed/180f:Settings.UseGravityGarage?Mathf.Sqrt(GarageWorldScale)/240:previousFixedStep*speed;
        }
        internal void RestoreClock()
        {
            if(!ownsClock) return;
            Time.timeScale=previousTimeScale; Time.fixedDeltaTime=previousFixedStep; ownsClock=false;
        }
        void FixedUpdate()
        {
            if(IsBuilding && Rotating && rotationPending) ApplyRotation(rotationHand,rotationTarget);
            if(State==DownhillState.Countdown)
            {
                if(Time.unscaledTime>=physicsStartTime && SimulationCore && SimulationWorld)
                {
                    foreach(var body in SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies))
                    { body.Rigidbody.isKinematic=false; body.Rigidbody.useGravity=true; StopBody(body); body.Rigidbody.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic; body.Rigidbody.ResetInertiaTensor(); }
                    SetTestClock(); if(Settings.UseGravityGarage) BeginGarageRun(); elapsed=stalled=0; State=DownhillState.Running; if(FactoryLevel) FactoryLevel.ResetPuzzles(true); Presentation.SetCountdown("");
                    if(Feedback) Feedback.Emit(SandboxSoundCue.Go,SimulationCore.MainBody.transform.position,SimulationCore);
                    Workshop.Session.Notify("TESTING — Retry to run again, or Back to Build to edit.");
                }
                return;
            }
            if(State!=DownhillState.Running || !SimulationCore) return;
            if(Settings.UseGravityGarage) { UpdateGarageRun(); return; }
            elapsed+=Time.fixedDeltaTime;
            var rb=SimulationCore.MainBody.Rigidbody; var pos=Course.SandboxFrame.InverseTransformPoint(rb.position);
            stalled=rb.linearVelocity.sqrMagnitude<.0004f?stalled+Time.fixedDeltaTime:0;
            if(Course.FinishArea?Course.FinishArea.Contains(rb.position):Settings.FinishBounds.Contains(pos)) Resolve(true);
            else if((Course.PlayArea?OutsideAuthoredCourse(rb.position):pos.y<.76f || Mathf.Abs(pos.z-.65f)>.46f || pos.x>1.5f) || elapsed>=Settings.AttemptDuration || stalled>=Settings.StallDuration) Resolve(false);
        }
        void Resolve(bool success)
        {
            if(VictoryInProgress || State==DownhillState.Success) return;
            finishBraking=false;
            State=success?DownhillState.Success:DownhillState.Failed;
            if(Audio) Audio.StopVehicle();
            if(Feedback) Feedback.Emit(success?SandboxSoundCue.Success:SandboxSoundCue.Failure,SimulationCore?SimulationCore.MainBody.transform.position:Course.SandboxFrame.position,SimulationCore);
            if(FactoryLevel) FreezeSimulation();
            if(success && Settings.Campaign && !DesignModeSession.Enabled) SandboxCampaignSession.Complete(CurrentCourse,Settings.Level4);
            if(success && Course.SandboxPolish && Settings.Campaign && SimulationWorld && !DesignModeSession.Enabled) BeginVictoryReturn();
            UpdateControls();
            Workshop.Session.Notify(success?(Settings.Campaign?(HasNextCourse?$"LEVEL {SandboxCourseCatalog.Number(CurrentCourse)} COMPLETE! Next Level is ready.":CurrentCourse==SandboxCourseId.FactoryLevel4?"ALL LEVELS COMPLETE! Retry or Back to Build.":$"LEVEL {SandboxCourseCatalog.Number(CurrentCourse)} COMPLETE!"):"FINISH! Retry or Back to Build."):"Test finished. Retry or Back to Build.");
        }
        public bool TryReturn()
        {
            if(IsBuilding || !CanSwitchMode) return false;
            if(Feedback) Feedback.Clear();
            if(Audio) Audio.StopAll();
            State=DownhillState.Transition; RestoreClock(); FreezeSimulation(); Presentation.SetCountdown("");
            if(FactoryLevel) FactoryLevel.ResetPuzzles(false);
            if(simulationRoot) simulationRoot.SetActive(false);
            transition=StartCoroutine(ReturnPresentation()); UpdateControls(); return true;
        }
        IEnumerator ReturnPresentation()
        {
            var start=Course.SandboxFrame.localPosition; float elapsed=0;
            do
            {
                elapsed+=Time.unscaledDeltaTime;
                float t=Settings.TransitionDuration<=0?1:Settings.TransitionEasing.Evaluate(Mathf.Clamp01(elapsed/Settings.TransitionDuration));
                Presentation.SetCoursePosition(Vector3.Lerp(start,Presentation.BuildPosition,t));
                if(elapsed<Settings.TransitionDuration) yield return null;
            } while(elapsed<Settings.TransitionDuration);
            transition=null; RestoreBuildImmediately(); Workshop.Session.Notify(Settings.DesignerBuilder?DesignerInstructions:BuildInstructions);
            if(Feedback && Core) Feedback.Emit(SandboxSoundCue.Arrive,Core.MainBody.transform.position,Core);
            var desktop=Workshop.GetComponent<DesktopInputAdapter>();if(Settings.Level3D && desktop && desktop.IsDesktop && desktop.View)desktop.FramePlatform();
        }
        void FreezeSimulation()
        {
            if(!SimulationWorld) return;
            foreach(var b in SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies)) { StopBody(b); b.Rigidbody.isKinematic=true; b.Rigidbody.useGravity=false; }
        }
        public bool TryRetry() => ResetRun(false);
        bool ResetRun(bool launchArrival)
        {
            if(VictoryInProgress || IsBuilding || State==DownhillState.Transition || !SimulationWorld || testStart==null) return false;
            // Factory approach rebuilds its native actors via this reset path. Preserve the
            // pending arrival until BeginCountdown; an explicit Retry still cancels it.
            if(Feedback && !launchArrival) Feedback.Clear();
            if(Audio) Audio.StopAll();
            if(FactoryLevel) {
                FactoryLevel.ResetPuzzles(false);
                foreach(var drill in simulationRoot.GetComponentsInChildren<SandboxDrill>()) drill.ResetSample();
                foreach(var burst in simulationRoot.GetComponents<SandboxDrillBurst>()) Destroy(burst);
            }
            if(Run3D)Run3D.ResetVehicle();
            if(Run25D)Run25D.ResetVehicle();
            RestoreClock(); FreezeSimulation(); Restore(testStart);
            if(FactoryLevel) {
                // Recreate native actors at the launch pose, including joint reference frames and contact caches.
                // The first transfer and subsequent retries must start from the same physical state.
                simulationRoot.SetActive(false); simulationRoot.SetActive(true); Restore(testStart);
            }
            SimulationWorld.Collisions.Reapply(); BeginCountdown();
            // Retry cleared the feedback above, so mark the car's return to the start like the first launch's arrival.
            if(!launchArrival && Feedback && SimulationCore) Feedback.Emit(SandboxSoundCue.Arrive,SimulationCore.MainBody.transform.position,SimulationCore);
            return true;
        }
        public void RestoreBuildImmediately()
        {
            if(Feedback) Feedback.Clear();
            if(Audio) Audio.StopAll();
            bool wasTesting=!IsBuilding;
            if(Workshop && Workshop.World) Workshop.World.CancelAlignments();
            if(transition!=null) { StopCoroutine(transition); transition=null; }
            FinishVictoryReturn(false);
            RestoreClock(); ClearSimulation();
            if(Course3D)Course3D.ResetCourse();
            if(!Workshop || !Presentation) return;
            Workshop.World.ReleaseAll();
            Presentation.SetCoursePosition(Presentation.BuildPosition);
            if(FactoryLevel) FactoryLevel.ResetPuzzles(false);
            foreach(var p in kit) if(p) p.gameObject.SetActive(!Settings.DesignerBuilder || DesignerPartAvailable(p.Kind));
            Restore(saved); saved.Clear(); Workshop.World.EditingEnabled=true; State=DownhillState.Build;
            TopologyChanged(); Workshop.World.Collisions.Reapply(); Presentation.SetBuilding(true);
            ModeGesture.Reset(true); if(SpawnPreview) SpawnPreview.Refresh(); UpdateControls();
            if(wasTesting && FactoryLevel) {
                var desktop=Workshop.GetComponent<DesktopInputAdapter>();
                if(desktop && desktop.IsDesktop) desktop.FramePlatform();
            }
        }
        public void Exit()
        {
            if(Workshop.World.IsBusy) return;
            rewardsSuppressed=true;
            
            if(Reward) Reward.Cancel();
            Workshop.World.ReleaseAll();CancelRotation();
            RestoreBuildImmediately();
            Workshop.ReturnToWorkshopRequested?.Invoke();
        }
        public bool TryResetBuild()
        {
            if(!IsBuilding || !CanSwitchMode || Rotating || !Core) return false;
            CompleteBoundaryReturns();
            
            foreach(var edge in Workshop.World.Connections.ToArray()) Workshop.World.RemoveConnection(edge.id);
            foreach(var part in kit) Restore(spawns[part]);
            saved.Clear(); returns.Clear(); previouslyHeld.Clear();
            elapsed=stalled=0; CancelRotation();
            Workshop.Session.ClearInput(); Workshop.Session.Select(null);
            if(Settings.DesignerBuilder) Workshop.Session.Selected=null;
            TopologyChanged(); Workshop.World.Collisions.Reapply();
            BuildStarterCar();
            Presentation.SetCountdown(""); SpawnPreview.Refresh(); UpdateControls();
            Workshop.Session.Notify("Build reset. All parts returned to their starting positions.");
            return true;
        }
        void ClearSimulation()
        {
            ClearLaunchEffects();
            physicsStartTime=0; countdownNumber=0; testStart=null;
            if(SimulationWorld) foreach(var p in SimulationWorld.Parts.Values.ToArray()) SimulationWorld.UnregisterPart(p);
            if(simulationRoot) { simulationRoot.SetActive(false); Destroy(simulationRoot); }
            SimulationWorld=null; SimulationCore=null; simulationRoot=null; simulationOriginals.Clear();
        }
        void OnDisable()
        {
            if(Feedback) Feedback.Clear();
            if(Audio) Audio.StopAll();
            if(Workshop && Workshop.World) Workshop.World.CancelAlignments();
            StopSoulStream();
            CompleteBoundaryReturns();
            if(Workshop && Workshop.World) Workshop.World.ReleaseAll();
            CancelRotation(); ModeGesture.Reset(true);
            if(!IsBuilding) RestoreBuildImmediately();
            if(SpawnPreview) SpawnPreview.Hide();
        }
        void OnApplicationFocus(bool focused)
        {
            if(focused || GarageComparing) return;
            if(Workshop && Workshop.World) Workshop.World.ReleaseAll();
            CancelRotation(); if(!IsBuilding) RestoreBuildImmediately();
        }
        void OnApplicationPause(bool paused)
        {
            if(!paused || GarageComparing) return;
            if(Workshop && Workshop.World) Workshop.World.ReleaseAll();
            CancelRotation(); if(!IsBuilding) RestoreBuildImmediately();
        }
        void OnDestroy()
        {
            CompleteBoundaryReturns();
            FinishVictoryReturn(false);
            RestoreClock(); ClearSimulation();
            if(Course3D)Course3D.ResetCourse();
            if(SpawnPreview) Destroy(SpawnPreview.gameObject);
            if(Workshop && Workshop.World) { Workshop.World.HoldsChanged-=HoldsChanged; Workshop.World.TopologyChanged-=TopologyChanged; Workshop.World.Collisions.Release("downhill-build"); }
        }
    }
}



