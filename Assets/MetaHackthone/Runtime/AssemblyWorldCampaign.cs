using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicalAssembly
{
    public sealed partial class AssemblyWorld
    {
        [Min(.01f)] public float CampaignAlignmentTime=.25f/1.5f;
        [Min(.01f)] public float CoreImpactDuration=.45f;
        [Min(0)] public float CoreImpactRange=.002f;
        [Range(1,2)] public int CoreImpactOscillations=2;
        AssemblyPart impactCore;
        Vector3 impactOffset;
        bool CampaignAttachment => DragBuilder && DragBuilder.Level && DragBuilder.Level.Settings.Campaign && DragBuilder.Level.Course.SandboxPolish;
        public Vector3 RestingCorePosition(AssemblyPart core) => core.MainBody.Rigidbody.position-(impactCore==core?impactOffset:Vector3.zero);

        static void Place(Snapshot[] poses,Vector3 offset)
        {
            foreach(var s in poses) if(s.body) {
                var r=s.body.Rigidbody; r.position=s.position+offset; r.rotation=s.rotation;
                r.transform.SetPositionAndRotation(r.position,r.rotation);
            }
            Physics.SyncTransforms();
        }

        IEnumerator AlignCampaign(AttachmentCandidate c,List<AssemblyPart> a,List<AssemblyPart> b)
        {
            var original=Capture(a.Concat(b)); var moving=a.SelectMany(p=>p.Bodies).ToArray();
            var sourceStart=new Pose(c.source.Rigidbody.position,c.source.Rigidbody.rotation);
            // Socket alignment must reach touching faces; hand movement retains its clearance skin.
            var motion=new BuildMotionConstraint.Group(moving,sourceStart,0);
            // A valid snap may rotate through its receiving assembly on the way in.
            // Keep unrelated obstacles solid; final placement still checks every part.
            var receivingColliders=new HashSet<Collider>(b.SelectMany(p=>p.Bodies).SelectMany(body=>body.Colliders));
            bool BlocksAlignment(Collider collider) => !receivingColliders.Contains(collider) && IsBuildBlocker(collider);
            var targetRotation=c.target.Rigidbody.rotation;
            var targetAnchor=Quaternion.Inverse(targetRotation)*(c.targetPoint-c.target.Rigidbody.position);
            var rotationDelta=c.rotationDelta;
            bool committed=false; Snapshot[] rest=null;
            var core=b.FirstOrDefault(p=>p.BuildingCore);
            foreach(var s in original) { s.body.Rigidbody.isKinematic=true; s.body.Rigidbody.interpolation=RigidbodyInterpolation.None; }
            Action cleanup=null;
            cleanup=()=> {
                if(!alignmentCleanup.Remove(cleanup)) return;
                Place(committed?rest:original,Vector3.zero);
                foreach(var s in original) if(s.body) s.Restore(false,!committed);
                if(impactCore==core) { impactCore=null; impactOffset=Vector3.zero; }
                foreach(var p in a.Concat(b)) if(p) reserved.Remove(p.InstanceId);
                if(!committed) Feedback?.Invoke("Attachment cancelled");
            };
            alignmentCleanup.Add(cleanup);
            try {
                float elapsed=0, duration=Mathf.Max(.01f,CampaignAlignmentTime);
                Vector3 direction=-c.targetSocket.Normal;
                while(elapsed<duration) {
                    yield return new WaitForFixedUpdate();
                    if(!EditingEnabled || !c.IsValid || original.Any(s=>!s.body || !s.body.gameObject.activeInHierarchy)) yield break;
                    elapsed+=Time.fixedDeltaTime; float t=Mathf.Clamp01(elapsed/duration);
                    t*=t; // Accelerate throughout the snap, with no ease-out before contact.
                    var targetDelta=c.target.Rigidbody.rotation*Quaternion.Inverse(targetRotation);
                    var point=c.target.Rigidbody.position+c.target.Rigidbody.rotation*targetAnchor;
                    var endPosition=point+targetDelta*rotationDelta*(sourceStart.position-c.sourcePoint);
                    var travel=endPosition-sourceStart.position;
                    if(travel.sqrMagnitude>1e-8f) direction=travel.normalized;
                    var pose=new Pose(Vector3.Lerp(sourceStart.position,endPosition,t),Quaternion.Slerp(sourceStart.rotation,targetDelta*rotationDelta*sourceStart.rotation,t));
                    var from=new Pose(c.source.Rigidbody.position,c.source.Rigidbody.rotation);
                    var moved=motion.Move(from,pose,BlocksAlignment,false);
                    if(!moved.Reached) { Feedback?.Invoke("Attachment path blocked."); NotifyInteraction(AssemblyInteractionKind.Reject,c.source.Part,c.source.transform.position); yield break; }
                }
                if(!PlacementClear(new AttachmentCandidate(c.source,c.target,c.sourceSocket.WorldPosition,c.targetSocket.WorldPosition,c.sourceSocket,c.targetSocket),a)) { Feedback?.Invoke("Placement intersects another part."); NotifyInteraction(AssemblyInteractionKind.Reject,c.source.Part,c.source.transform.position); yield break; }
                var record=new ConnectionRecord {
                    id=Guid.NewGuid().ToString("N"), a=new BodyRef(c.source.Part.InstanceId,c.source.Id), b=new BodyRef(c.target.Part.InstanceId,c.target.Id),
                    socketA=c.sourceSocket.Id,socketB=c.targetSocket.Id,
                    anchorA=c.sourceSocket.transform.localPosition,anchorB=targetAnchor
                };
                rest=Capture(a.Concat(b)); committed=ConnectInternal(record,true);
                if(!committed) { Feedback?.Invoke("Attachment socket unavailable."); NotifyInteraction(AssemblyInteractionKind.Reject,c.source.Part,c.source.transform.position); yield break; }
                Feedback?.Invoke("Attached");
                if(!core || CoreImpactRange<=0) yield break;

                impactCore=core;
                var origin=new Pose(core.MainBody.Rigidbody.position,core.MainBody.Rigidbody.rotation);
                var recoil=new BuildMotionConstraint.Group(rest.Select(s=>s.body).ToArray(),origin);
                duration=Mathf.Max(.01f,CoreImpactDuration); elapsed=0;
                int cycles=Mathf.Clamp(CoreImpactOscillations,1,2);
                // Normalize this damped sinusoid to the requested peak-to-peak range.
                float minimum=0,maximum=0;
                for(int i=0;i<=256;i++) { float value=ImpactWave(i/256f,cycles); minimum=Mathf.Min(minimum,value); maximum=Mathf.Max(maximum,value); }
                float amplitude=CoreImpactRange/(maximum-minimum);
                while(elapsed<duration) {
                    yield return new WaitForFixedUpdate();
                    if(!EditingEnabled || rest.Any(s=>!s.body || !s.body.gameObject.activeInHierarchy)) yield break;
                    elapsed+=Time.fixedDeltaTime;
                    var offset=direction*(amplitude*ImpactWave(Mathf.Clamp01(elapsed/duration),cycles));
                    recoil.Move(new Pose(core.MainBody.Rigidbody.position,origin.rotation),new Pose(origin.position+offset,origin.rotation),IsBuildBlocker,false);
                    impactOffset=core.MainBody.Rigidbody.position-origin.position;
                }
            }
            finally { cleanup(); }
        }
        static float ImpactWave(float t,int cycles) => t>=1?0:Mathf.Exp(-4*t)*Mathf.Sin(2*Mathf.PI*cycles*t);
    }
}
