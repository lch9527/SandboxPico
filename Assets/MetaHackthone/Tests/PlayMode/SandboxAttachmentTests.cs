using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    public sealed partial class SandboxAttachmentTests
    {
        GameObject root;
        WorkshopBootstrap workshop;
        DownhillSettings settings;
        GravityGarageLevelAsset course;
        DownhillController Level=>workshop.Downhill;
        AssemblyWorld World=>workshop.World;
        [UnitySetUp] public IEnumerator Setup()
        {
            SceneManager.LoadScene("MainMenu"); yield return null;
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset(); SandboxCampaignSession.Complete(SandboxCourseId.BeautifulCorner); SandboxCampaignSession.Advance();
            var scene=SceneManager.GetSceneByName("Attachment fixture"); if(!scene.IsValid()) scene=SceneManager.CreateScene("Attachment fixture"); SceneManager.SetActiveScene(scene);
            settings=ScriptableObject.CreateInstance<DownhillSettings>();
            settings.DesignerBuilder=settings.UseGravityGarage=settings.Campaign=settings.SandboxDrillCourse=true;
            course=ScriptableObject.CreateInstance<GravityGarageLevelAsset>(); course.parameters=GravityGarage25DParameters.UnityCalibrated(); settings.Level25D=course;
            root=new GameObject("Attachment test"); root.SetActive(false);
            workshop=root.AddComponent<WorkshopBootstrap>(); workshop.DownhillSettings=settings; root.SetActive(true);
            Level.Course.SandboxPolish=true;
            World.Feedback+=message=>Debug.Log("Attachment test: "+message);
            yield return null;
        }
        [TearDown] public void Cleanup()
        {
            if(workshop && Level) Level.RestoreBuildImmediately();
            if(root) Object.DestroyImmediate(root);
            foreach(var p in Object.FindObjectsByType<AssemblyPart>(FindObjectsInactive.Include,FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
            foreach(var e in Object.FindObjectsByType<WorkshopEnvironment>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
            Object.DestroyImmediate(settings); Object.DestroyImmediate(course); SandboxCampaignSession.Reset();
        }
        AssemblyPart Part(Vector3 position,PartKind kind=PartKind.DownhillShortBlock)
        {
            var p=DownhillParts.Create(World,kind,new Pose(position,Quaternion.identity),1,settings,DownhillParts.DesignerGridVersion);
            foreach(var b in p.Bodies) { b.Rigidbody.isKinematic=true; b.Rigidbody.useGravity=false; }
            return p;
        }
        AttachmentCandidate Prepare(AssemblyPart source,AssemblyPart target,Vector3 normal,float twist=0,float gap=.03f)
        {
            var to=target.Sockets.OrderByDescending(s=>Vector3.Dot(s.Normal,normal)).First(s=>!World.SocketOccupied(s));
            var from=source.Sockets.First(s=>Vector3.Dot(s.transform.localRotation*Vector3.forward,Vector3.down)>.99f);
            var rotation=Quaternion.AngleAxis(twist,to.Normal)*Quaternion.LookRotation(-to.Normal,to.Up)*Quaternion.Inverse(from.transform.localRotation);
            var rb=source.MainBody.Rigidbody; rb.rotation=rotation; rb.position=to.WorldPosition+to.Normal*gap-rotation*from.transform.localPosition;
            rb.transform.SetPositionAndRotation(rb.position,rb.rotation); Physics.SyncTransforms();
            return new AttachmentCandidate(from.Body,to.Body,from.WorldPosition,to.WorldPosition,from,to);
        }
        IEnumerator Finish()
        {
            float end=Time.time+2;
            while(World.IsBusy && Time.time<end) yield return new WaitForFixedUpdate();
            Assert.IsFalse(World.IsBusy);
        }
        [UnityTest] public IEnumerator FasterAlignmentThenTwoMillimetreRecoilKeepsPlatformStill()
        {
            var source=Part(Vector3.up*4); var c=Prepare(source,Level.Core,Vector3.up,25);
            var start=source.MainBody.Rigidbody.position; var coreStart=Level.Core.MainBody.Rigidbody.position;
            var approach=(c.SnappedPose.position-start).normalized;
            var group=World.GetConnectedParts(Level.Core.InstanceId).SelectMany(p=>p.Bodies).ToArray();
            var positions=group.Select(b=>b.Rigidbody.position).ToArray(); var rotations=group.Select(b=>b.Rigidbody.rotation).ToArray();
            var loose=Level.Kit.First(p=>!Level.IsCoreConnected(p)); var loosePosition=loose.MainBody.Rigidbody.position;
            Level.Presentation.FollowCore(Level.Core); var pad=Level.Presentation.EnsureGridPadGroup(); var padPosition=pad.position;
            int edges=World.Connections.Count; float begun=Time.fixedTime;
            Assert.IsTrue(World.TryBeginAssembly(c)); Assert.IsFalse(World.TryBeginAssembly(c));
            Assert.IsFalse(Level.TryStart()); Assert.IsFalse(Level.TryResetBuild());
            Assert.IsFalse(Level.BeginRotation(0,Level.RotationWheel.transform.position));
            Assert.IsFalse(World.TryGrab(1,source,new Pose(start,Quaternion.identity)));
            yield return new WaitForSeconds(.1f);
            Assert.AreEqual(edges,World.Connections.Count);
            Assert.That(Vector3.Distance(start,source.MainBody.Rigidbody.position),Is.InRange(.003f,.025f));
            Assert.Greater(Quaternion.Angle(c.SnappedPose.rotation,source.MainBody.Rigidbody.rotation),1);
            while(World.Connections.Count==edges && Time.time-begun<1) yield return new WaitForFixedUpdate();
            Assert.AreEqual(edges+1,World.Connections.Count);
            Assert.That(Time.fixedTime-begun,Is.InRange(.16f,.21f));
            float min=0,max=0,previous=0; int signChanges=0; bool first=true;
            while(World.IsBusy && Time.time-begun<2) {
                yield return new WaitForFixedUpdate();
                var offset=Level.Core.MainBody.Rigidbody.position-coreStart; float amount=Vector3.Dot(offset,approach);
                if(Mathf.Abs(amount)>.00001f) {
                    if(first) { Assert.Greater(amount,0); first=false; }
                    if(previous*amount<0) signChanges++; previous=amount;
                }
                min=Mathf.Min(min,amount);max=Mathf.Max(max,amount);
                Assert.Less(Vector3.Cross(offset,approach).magnitude,.00001f);
                for(int i=0;i<group.Length;i++) {
                    Assert.Less(Vector3.Distance(positions[i]+offset,group[i].Rigidbody.position),.00001f);
                    Assert.Less(Quaternion.Angle(rotations[i],group[i].Rigidbody.rotation),.01f);
                }
                Assert.Less(Vector3.Distance(c.sourceSocket.WorldPosition,c.targetSocket.WorldPosition),.0001f);
                Level.Presentation.FollowCore(Level.Core); Assert.Less(Vector3.Distance(padPosition,pad.position),.00001f);
            }
            Assert.IsFalse(World.IsBusy); Assert.That(max-min,Is.InRange(.0016f,.00202f)); Assert.AreEqual(3,signChanges);
            Assert.Less(Vector3.Distance(coreStart,Level.Core.MainBody.Rigidbody.position),.000001f);
            Assert.AreEqual(loosePosition,loose.MainBody.Rigidbody.position);
            Assert.IsTrue(source.MainBody.Rigidbody.isKinematic); Assert.IsTrue(Level.CanSwitchMode);
        }
        [UnityTest] public IEnumerator LooseBranchPreservesSpacingAndRotationWithoutRecoil()
        {
            var target=Part(new Vector3(0,4,0)); var source=Part(new Vector3(1,4,0));
            var c=Prepare(source,target,Vector3.up,25);
            var child=Part(Vector3.up*5); var branch=Prepare(child,source,Vector3.up,0,0);
            var record=new ConnectionRecord {id=System.Guid.NewGuid().ToString(),a=new BodyRef(child.InstanceId,child.MainBody.Id),b=new BodyRef(source.InstanceId,source.MainBody.Id),socketA=branch.sourceSocket.Id,socketB=branch.targetSocket.Id,anchorA=branch.sourceSocket.transform.localPosition,anchorB=branch.targetSocket.transform.localPosition};
            Assert.IsTrue(World.Connect(record)); float distance=Vector3.Distance(source.MainBody.Rigidbody.position,child.MainBody.Rigidbody.position);
            var relative=Quaternion.Inverse(source.MainBody.Rigidbody.rotation)*child.MainBody.Rigidbody.rotation;
            var targetStart=target.MainBody.Rigidbody.position; float begun=Time.fixedTime; int edges=World.Connections.Count;
            Assert.IsTrue(World.TryBeginAssembly(c));
            while(World.IsBusy && Time.time-begun<2) {
                yield return new WaitForFixedUpdate();
                Assert.That(Vector3.Distance(source.MainBody.Rigidbody.position,child.MainBody.Rigidbody.position),Is.EqualTo(distance).Within(.00001));
                Assert.Less(Quaternion.Angle(relative,Quaternion.Inverse(source.MainBody.Rigidbody.rotation)*child.MainBody.Rigidbody.rotation),.01f);
            }
            Assert.AreEqual(edges+1,World.Connections.Count);
            Assert.That(Time.fixedTime-begun,Is.InRange(.16f,.21f)); Assert.AreEqual(targetStart,target.MainBody.Rigidbody.position);
            Assert.Less(Vector3.Distance(c.sourceSocket.WorldPosition,c.targetSocket.WorldPosition),.0001f);
        }
        [UnityTest] public IEnumerator TiltedKitPartsCompleteAtValidCoreSockets()
        {
            var target=Level.Core;
            // Isolate the receiving core from the palette, while retaining real campaign rules.
            foreach(var body in target.Bodies) {body.Rigidbody.position+=Vector3.up*3;body.transform.position=body.Rigidbody.position;}
            Physics.SyncTransforms();
            foreach(var kind in new[]{PartKind.DownhillLargeWheel,PartKind.DownhillSmallWheel,PartKind.DownhillLongBlock,PartKind.DownhillCube,PartKind.DownhillDrill})
            foreach(float angle in new[]{-55f,30f,55f}) {
                var source=Part(Vector3.up*5,kind);var from=source.Sockets[0];
                var to=target.Sockets.OrderByDescending(s=>Vector3.Dot(s.Normal,Vector3.right)).First();
                var rotation=Quaternion.AngleAxis(angle,to.Up)*Quaternion.LookRotation(-to.Normal,to.Up)*Quaternion.Inverse(from.transform.localRotation);
                var oldPosition=source.MainBody.Rigidbody.position;var delta=rotation*Quaternion.Inverse(source.MainBody.Rigidbody.rotation);
                var position=to.WorldPosition+to.Normal*.015f-rotation*from.transform.localPosition;
                var poses=source.Bodies.Select(b=>new Pose(b.Rigidbody.position,b.Rigidbody.rotation)).ToArray();
                for(int i=0;i<source.Bodies.Length;i++) {
                    var rb=source.Bodies[i].Rigidbody;rb.position=position+delta*(poses[i].position-oldPosition);rb.rotation=delta*poses[i].rotation;
                    rb.transform.SetPositionAndRotation(rb.position,rb.rotation);
                }
                Physics.SyncTransforms();
                var candidate=new AttachmentCandidate(from.Body,to.Body,from.WorldPosition,to.WorldPosition,from,to);
                Assert.IsTrue(World.PlacementClear(candidate,new[]{source}),kind+" final placement");
                int edges=World.Connections.Count;
                Assert.IsTrue(World.TryBeginAssembly(candidate),kind+" at "+angle+" degrees");
                yield return Finish();
                Assert.AreEqual(edges+1,World.Connections.Count,kind+" at "+angle+" degrees cancelled despite a valid endpoint");
                Assert.Less(Vector3.Distance(from.WorldPosition,to.WorldPosition),.0001f);
                Assert.IsTrue(World.TryDetachPart(source));Object.DestroyImmediate(source.gameObject);
            }
        }
        [UnityTest] public IEnumerator WheelSnapsFromEitherSideWithoutTurningAround()
        {
            var core=Level.Core;
            foreach(var body in core.Bodies) {body.Rigidbody.position+=Vector3.up*3;body.transform.position=body.Rigidbody.position;}
            Physics.SyncTransforms();
            foreach(var kind in new[]{PartKind.DownhillSmallWheel,PartKind.DownhillLargeWheel})
            foreach(var side in new[]{"mount","mount_opposite"}) {
                var wheel=Part(Vector3.up*5,kind);
                CollectionAssert.AreEquivalent(new[]{"mount","mount_opposite"},wheel.Sockets.Select(s=>s.Id).ToArray(),kind+" mounts on both hub faces");
                var from=wheel.FindSocket(side); var other=wheel.Sockets.First(s=>s!=from);
                Assert.Less(Vector3.Dot(from.Normal,other.Normal),-.99f,"The two mounts face opposite ways");
                // Hold the wheel with this face toward the core's side face, slightly apart, already aligned.
                var to=core.Sockets.OrderByDescending(s=>Vector3.Dot(s.Normal,Vector3.right)).First(s=>!World.SocketOccupied(s));
                var rotation=Quaternion.LookRotation(-to.Normal,to.Up)*Quaternion.Inverse(from.transform.localRotation);
                var rb=wheel.MainBody.Rigidbody; var old=rb.position; var delta=rotation*Quaternion.Inverse(rb.rotation);
                var position=to.WorldPosition+to.Normal*.02f-rotation*from.transform.localPosition;
                foreach(var b in wheel.Bodies) { var r=b.Rigidbody; r.position=position+delta*(r.position-old); r.rotation=delta*r.rotation; r.transform.SetPositionAndRotation(r.position,r.rotation); }
                Physics.SyncTransforms();
                var candidate=World.DragBuilder.Query(wheel);
                Assert.IsTrue(candidate.IsValid,kind+" "+side+" finds the core socket");
                Assert.AreEqual(side,candidate.sourceSocket.Id,"The face presented to the block is the one that connects");
                Assert.Less(Quaternion.Angle(Quaternion.identity,candidate.rotationDelta),1f,"No correction turn when either face is presented");
                int edges=World.Connections.Count;
                Assert.IsTrue(World.TryBeginAssembly(candidate)); yield return Finish();
                Assert.AreEqual(edges+1,World.Connections.Count,kind+" "+side+" attaches");
                Assert.Less(Vector3.Distance(candidate.sourceSocket.WorldPosition,candidate.targetSocket.WorldPosition),.0001f);
                Assert.IsTrue(World.TryDetachPart(wheel)); Object.DestroyImmediate(wheel.gameObject);
            }
        }
        [UnityTest] public IEnumerator PositionAndRotationAccelerateThroughoutAttachment()
        {
            Assert.That(World.CampaignAlignmentTime,Is.EqualTo(.25f/1.5f).Within(.00001f));
            var target=Part(new Vector3(0,4,0));var source=Part(Vector3.up*5);var c=Prepare(source,target,Vector3.up,25);
            var previousPosition=source.MainBody.Rigidbody.position;var previousRotation=source.MainBody.Rigidbody.rotation;
            var movement=new System.Collections.Generic.List<float>();var turns=new System.Collections.Generic.List<float>();
            Assert.IsTrue(World.TryBeginAssembly(c));float deadline=Time.time+1;
            while(World.IsBusy && Time.time<deadline) {
                yield return new WaitForFixedUpdate();
                var rb=source.MainBody.Rigidbody;
                // The last fixed step is clipped to the exact endpoint; compare complete steps only.
                if(World.IsBusy) {movement.Add(Vector3.Distance(previousPosition,rb.position));turns.Add(Quaternion.Angle(previousRotation,rb.rotation));}
                previousPosition=rb.position;previousRotation=rb.rotation;
            }
            Assert.IsFalse(World.IsBusy);Assert.GreaterOrEqual(movement.Count,4);
            for(int i=1;i<movement.Count;i++) {
                Assert.GreaterOrEqual(movement[i]+.00001f,movement[i-1],"Position slowed before reaching its socket");
                Assert.GreaterOrEqual(turns[i]+.03f,turns[i-1],"Rotation slowed before reaching its socket");
            }
            Assert.Greater(movement.Last(),movement.First()*2);Assert.Greater(turns.Last(),turns.First()*2);
            Assert.Less(Vector3.Distance(c.sourceSocket.WorldPosition,c.targetSocket.WorldPosition),.0001f);
        }
        [UnityTest] public IEnumerator RepeatedOppositeImpactsAndIndirectCoreAttachment()
        {
            foreach(var normal in new[]{Vector3.up,Vector3.down}) {
                var source=Part(Vector3.up*4); var c=Prepare(source,Level.Core,normal); var origin=Level.Core.MainBody.Rigidbody.position;
                int edges=World.Connections.Count; Assert.IsTrue(World.TryBeginAssembly(c));
                while(World.Connections.Count==edges && World.IsBusy) yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
                Assert.Greater(Vector3.Dot(Level.Core.MainBody.Rigidbody.position-origin,-normal),.0001f);
                yield return Finish();
                var child=Part(Vector3.up*4); var second=Prepare(child,source,normal);
                edges=World.Connections.Count; Assert.IsTrue(World.TryBeginAssembly(second));
                while(World.Connections.Count==edges && World.IsBusy) yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
                Assert.Greater(Vector3.Dot(Level.Core.MainBody.Rigidbody.position-origin,-normal),.0001f);
                yield return Finish(); Assert.IsTrue(World.TryDetachPart(child)); Object.DestroyImmediate(child.gameObject);
                Assert.IsTrue(World.TryDetachPart(source)); Object.DestroyImmediate(source.gameObject);
            }
        }
        [UnityTest] public IEnumerator DisableBeforeCommitRestoresAndAfterCommitSettles()
        {
            var source=Part(Vector3.up*4); var c=Prepare(source,Level.Core,Vector3.up); var start=source.MainBody.Rigidbody.position;
            int edges=World.Connections.Count; Assert.IsTrue(World.TryBeginAssembly(c)); yield return new WaitForSeconds(.1f);
            World.enabled=false; Assert.IsFalse(World.IsBusy); Assert.AreEqual(edges,World.Connections.Count); Assert.AreEqual(start,source.MainBody.Rigidbody.position);
            World.enabled=true; c=Prepare(source,Level.Core,Vector3.up); var coreStart=Level.Core.MainBody.Rigidbody.position;
            Assert.IsTrue(World.TryBeginAssembly(c)); yield return new WaitForSeconds(.33f);
            Assert.AreEqual(edges+1,World.Connections.Count); Assert.IsTrue(World.IsBusy);
            Level.RestoreBuildImmediately(); Assert.IsFalse(World.IsBusy); Assert.AreEqual(edges+1,World.Connections.Count);
            Assert.AreEqual(coreStart,Level.Core.MainBody.Rigidbody.position);
            Assert.Less(Vector3.Distance(c.sourceSocket.WorldPosition,c.targetSocket.WorldPosition),.0001f);
        }
        [UnityTest] public IEnumerator DestroyDuringAlignmentAndImpactReleasesSurvivors()
        {
            foreach(float delay in new[]{.1f,.33f}) {
                var source=Part(Vector3.up*4); var c=Prepare(source,Level.Core,Vector3.up); var origin=Level.Core.MainBody.Rigidbody.position;
                int edges=World.Connections.Count; Assert.IsTrue(World.TryBeginAssembly(c)); yield return new WaitForSeconds(delay);
                Object.Destroy(source.gameObject); yield return new WaitForSeconds(.1f);
                Assert.IsFalse(World.IsBusy); Assert.AreEqual(origin,Level.Core.MainBody.Rigidbody.position); Assert.AreEqual(edges,World.Connections.Count);
            }
        }
        [UnityTest] public IEnumerator BlockedPathCancelsWithoutConnectionOrMovingUnrelatedPart()
        {
            var target=Part(new Vector3(0,4,0)); var source=Part(Vector3.up*5); var c=Prepare(source,target,Vector3.up);
            var origin=source.MainBody.Rigidbody.position; int edges=World.Connections.Count;
            Assert.IsTrue(World.TryBeginAssembly(c));
            var blocker=Part(c.SnappedPose.position); var blockPosition=blocker.MainBody.Rigidbody.position;
            yield return Finish(); Assert.AreEqual(edges,World.Connections.Count); Assert.AreEqual(origin,source.MainBody.Rigidbody.position);
            Assert.AreEqual(blockPosition,blocker.MainBody.Rigidbody.position);
            Object.DestroyImmediate(blocker.gameObject);
            Assert.IsTrue(World.TryBeginAssembly(c));yield return Finish();Assert.AreEqual(edges+1,World.Connections.Count,"A cancelled attachment must allow immediate retry.");
        }
        [UnityTest] public IEnumerator ReceivingAssemblyStillCannotOverlapFinalPlacement()
        {
            var receiver=Part(Vector3.up*4);var existing=Prepare(receiver,Level.Core,Vector3.down,0,0);
            Assert.IsTrue(World.Connect(new ConnectionRecord {id=System.Guid.NewGuid().ToString(),a=new BodyRef(receiver.InstanceId,receiver.MainBody.Id),b=new BodyRef(Level.Core.InstanceId,Level.Core.MainBody.Id),socketA=existing.sourceSocket.Id,socketB=existing.targetSocket.Id,anchorA=existing.sourceSocket.transform.localPosition,anchorB=existing.targetSocket.transform.localPosition}));
            var receiverStart=receiver.MainBody.Rigidbody.position;
            var source=Part(Vector3.up*4);var c=Prepare(source,Level.Core,Vector3.up);var start=source.MainBody.Rigidbody.position;
            int edges=World.Connections.Count;string feedback="";World.Feedback+=message=>feedback+=message;
            Assert.IsTrue(World.TryBeginAssembly(c));yield return new WaitForFixedUpdate();
            // Simulate external movement of a reserved receiver into the endpoint.
            // It is excluded from the animation sweep, but must still fail final validation.
            receiver.MainBody.Rigidbody.position=c.SnappedPose.position;receiver.MainBody.Rigidbody.rotation=c.SnappedPose.rotation;
            receiver.MainBody.transform.SetPositionAndRotation(c.SnappedPose.position,c.SnappedPose.rotation);Physics.SyncTransforms();
            yield return Finish();Assert.AreEqual(edges,World.Connections.Count);Assert.AreEqual(start,source.MainBody.Rigidbody.position);
            Assert.AreEqual(receiverStart,receiver.MainBody.Rigidbody.position);StringAssert.Contains("Placement intersects",feedback);
            Assert.IsTrue(World.TryBeginAssembly(c));yield return Finish();Assert.AreEqual(edges+1,World.Connections.Count);
            var second=Part(Vector3.up*4);var other=Prepare(second,Level.Core,Vector3.up);
            var occupied=new AttachmentCandidate(other.source,c.target,other.sourcePoint,c.targetSocket.WorldPosition,other.sourceSocket,c.targetSocket);
            Assert.IsFalse(World.TryBeginAssembly(occupied));Assert.IsFalse(World.IsBusy);Assert.AreEqual(edges+1,World.Connections.Count);
        }
        [UnityTest] public IEnumerator NonCampaignDesignerKeepsInstantAttachment()
        {
            settings.Campaign=false;
            var target=Part(new Vector3(0,4,0)); var source=Part(Vector3.up*5); var c=Prepare(source,target,Vector3.up);
            int edges=World.Connections.Count; Assert.IsTrue(World.TryBeginAssembly(c));
            Assert.IsFalse(World.IsBusy); Assert.AreEqual(edges+1,World.Connections.Count);
            yield return null;
        }
        [UnityTest] public IEnumerator NearbyLoosePartLimitsRecoilWithoutMovingOrBreakingIt()
        {
            var blocker=Part(Vector3.up*4); Prepare(blocker,Level.Core,Vector3.down,0,.0008f);
            var blockPosition=blocker.MainBody.Rigidbody.position;
            var source=Part(Vector3.up*4); var c=Prepare(source,Level.Core,Vector3.up);
            var origin=Level.Core.MainBody.Rigidbody.position; int edges=World.Connections.Count;
            Assert.IsTrue(World.TryBeginAssembly(c)); float begun=Time.time,maximum=0;
            while(World.IsBusy && Time.time-begun<2) {
                yield return new WaitForFixedUpdate();
                maximum=Mathf.Max(maximum,Vector3.Dot(Level.Core.MainBody.Rigidbody.position-origin,Vector3.down));
                Assert.AreEqual(blockPosition,blocker.MainBody.Rigidbody.position);
            }
            Assert.IsFalse(World.IsBusy); Assert.AreEqual(edges+1,World.Connections.Count);
            Assert.Less(maximum,.0014f); Assert.AreEqual(origin,Level.Core.MainBody.Rigidbody.position);
            Assert.Less(Vector3.Distance(c.sourceSocket.WorldPosition,c.targetSocket.WorldPosition),.0001f);
        }
    }
}
