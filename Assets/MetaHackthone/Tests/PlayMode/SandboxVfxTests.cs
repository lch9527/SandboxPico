using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    public sealed partial class SandboxAttachmentTests
    {
        [UnityTest] public IEnumerator VictoryCelebrationReplaysAndClearsWithPresentation()
        {
            Level.Feedback.Emit(SandboxSoundCue.Success,Level.Core.transform.position,Level.Core);yield return null;
            var effect=Level.Vfx.GetComponent<SandboxVictoryCelebration>();Assert.IsNotNull(effect);Assert.IsTrue(effect.Playing);
            effect.Advance(1);Assert.AreEqual(2,effect.GetComponentsInChildren<ParticleSystem>().Length);
            Assert.LessOrEqual(effect.GetComponentsInChildren<ParticleSystem>().Sum(p=>p.particleCount),256);
            var reward=new SandboxFragments(Level.transform,512);
            try {Assert.IsTrue(effect.Playing,"Reward transfer must not cancel the victory celebration");}
            finally {reward.Release();}
            Level.Feedback.Clear();Assert.IsFalse(effect.Playing);Assert.IsEmpty(effect.GetComponentsInChildren<ParticleSystem>());
            Level.Feedback.Emit(SandboxSoundCue.Success,Level.Core.transform.position,Level.Core);effect.Advance(2);Assert.IsTrue(effect.Playing);
            effect.Advance(SandboxVictoryCelebration.Duration);Assert.IsFalse(effect.Playing);
            Level.Feedback.Clear();
        }
        [UnityTest] public IEnumerator VfxAttachUsesCommittedSocketWithAudioUnavailable()
        {
            Level.Audio.enabled=false;
            var feedback=new List<SandboxFeedbackEvent>();Level.Feedback.Presented+=feedback.Add;
            var target=Part(Vector3.up*4);var source=Part(Vector3.up*5);var c=Prepare(source,target,Vector3.right,15);
            Assert.IsTrue(World.TryBeginAssembly(c));
            Assert.AreEqual(0,Level.Vfx.ActiveRings);
            yield return new WaitForSeconds(.06f);Assert.IsEmpty(feedback);
            yield return Finish();
            var attached=feedback.Single(e=>e.Cue==SandboxSoundCue.Attach);
            Assert.Less(Vector3.Distance(c.targetSocket.WorldPosition,attached.Position),.0001f);
            Assert.Greater(Vector3.Dot(c.targetSocket.Normal,attached.Normal),.99f);
            Assert.AreEqual(source,attached.Source);Assert.Greater(Level.Vfx.PresentedCount,0);
            var edge=World.Connections.Single(e=>e.a.partId==source.InstanceId);
            World.RemoveConnection(edge.id);Assert.IsTrue(World.Connect(edge));Assert.AreEqual(1,feedback.Count);
            Level.Feedback.Clear();var point=c.targetSocket.WorldPosition;
            Assert.IsTrue(World.TryDetachPart(source));
            Assert.AreEqual(1,Level.Vfx.ActiveRings);
            Assert.Less(Vector3.Distance(point,feedback.Last().Position),.0001f);
        }

        [UnityTest] public IEnumerator VfxCancelledAlignmentAndTransferLeaveNoArrival()
        {
            var events=new List<SandboxFeedbackEvent>();Level.Feedback.Presented+=events.Add;
            var source=Part(Vector3.up*4);var c=Prepare(source,Level.Core,Vector3.up);
            Assert.IsTrue(World.TryBeginAssembly(c));yield return new WaitForFixedUpdate();World.CancelAlignments();
            Level.Feedback.BeginTransfer(source.transform,Level.Core.transform,.05f,.1f);Level.Feedback.Clear();
            Level.Feedback.CompleteTransfer(Level.Core.transform.position);
            yield return new WaitForSeconds(.4f);
            Assert.IsFalse(events.Any(e=>e.Cue==SandboxSoundCue.Attach || e.Cue==SandboxSoundCue.Arrive));
            Assert.AreEqual(0,Level.Vfx.ActiveParticles);Assert.AreEqual(0,Level.Vfx.ActiveRings);
        }

        [UnityTest] public IEnumerator VfxBudgetGivesFullCapacityToTransfersAndPauseClears()
        {
            var part=Level.Core;var rb=part.MainBody.Rigidbody;var position=rb.position;var rotation=rb.rotation;
            var colliders=part.GetComponentsInChildren<Collider>().Length;
            for(int i=0;i<100;i++)Level.Feedback.Emit(SandboxSoundCue.Attach,position,part);
            Assert.AreEqual(64,Level.Vfx.ActiveParticles);Assert.AreEqual(8,Level.Vfx.ActiveRings);
            Assert.AreEqual(colliders,part.GetComponentsInChildren<Collider>().Length);
            Assert.AreEqual(position,rb.position);Assert.AreEqual(rotation,rb.rotation);
            var transfer=new SandboxFragments(Level.transform,512);
            try {Assert.AreEqual(0,Level.Vfx.ActiveParticles);Assert.AreEqual(512,transfer.Points.Length);Assert.AreEqual(512,SandboxFragments.ActiveCount);}
            finally {transfer.Release();}
            Level.Feedback.Emit(SandboxSoundCue.Splash,position,part,Vector3.up);
            Assert.AreEqual(16,Level.Vfx.ActiveParticles);Assert.AreEqual(2,Level.Vfx.ActiveRings);
            float scale=Time.timeScale;
            try {Time.timeScale=0;yield return null;yield return null;Assert.AreEqual(0,Level.Vfx.ActiveParticles);Assert.AreEqual(0,Level.Vfx.ActiveRings);}
            finally {Time.timeScale=scale;}
            Level.Feedback.Emit(SandboxSoundCue.Attach,position,part);Level.Vfx.enabled=false;
            Assert.AreEqual(0,Level.Vfx.ActiveParticles);Assert.AreEqual(0,Level.Vfx.ActiveRings);Assert.AreEqual(0,SandboxFragments.ActiveCount);
        }

        [UnityTest] public IEnumerator VfxLaunchUnderPressureCompletesOnceAndRetryClears()
        {
            var events=new List<SandboxFeedbackEvent>();Level.Feedback.Presented+=events.Add;
            Level.LaunchDecomposeSeconds=Level.LaunchTravelSeconds=Level.LaunchReconstructSeconds=.04f;
            settings.CountdownStep=.02f;settings.GoDuration=.02f;
            for(int i=0;i<20;i++)Level.Feedback.Emit(SandboxSoundCue.Attach,Level.Core.transform.position,Level.Core);
            Assert.IsTrue(Level.TryStart());
            float end=Time.realtimeSinceStartup+3;
            while(Level.State!=DownhillState.Running && Time.realtimeSinceStartup<end) {Assert.LessOrEqual(SandboxFragments.ActiveCount,512);yield return null;}
            Assert.AreEqual(DownhillState.Running,Level.State);
            Assert.AreEqual(1,events.Count(e=>e.Cue==SandboxSoundCue.Arrive));
            Assert.IsTrue(Level.TryRetry());Assert.AreEqual(0,Level.Vfx.ActiveRings);Assert.AreEqual(0,Level.Vfx.ActiveParticles);
            Level.RestoreBuildImmediately();Assert.AreEqual(0,SandboxFragments.ActiveCount);
        }

        [UnityTest] public IEnumerator VfxContactThresholdAndDrillRateRemainIndependentOfMutedAudio()
        {
            typeof(DownhillController).GetProperty("State").SetValue(Level,DownhillState.Running);
            bool mute=AudioListener.pause;
            try {
                AudioListener.pause=true;
                Level.Feedback.Impact(SandboxSurfaceMaterial.Wood,Vector3.zero,Vector3.up,.001f,Level.Core);
                Assert.AreEqual(0,Level.Vfx.ActiveParticles);
                Level.Feedback.Impact(SandboxSurfaceMaterial.Rubber,Vector3.zero,Vector3.up,1,Level.Core);
                Assert.Greater(Level.Vfx.ActiveParticles,0);
                Level.Feedback.Clear();
                for(int i=0;i<50;i++)Level.Feedback.DrillContact(Level.Core,Vector3.zero,Vector3.right);
                Assert.AreEqual(4,Level.Vfx.ActiveParticles,"Sustained contact is rate limited independently of audio");
                yield return new WaitForSeconds(.1f);Level.Feedback.DrillContact(Level.Core,Vector3.zero,Vector3.right);
                Assert.AreEqual(8,Level.Vfx.ActiveParticles);
                Level.Feedback.Clear();
                typeof(DownhillController).GetProperty("State").SetValue(Level,DownhillState.Build);
                Level.Feedback.DrillContact(Level.Core,Vector3.zero,Vector3.right);Assert.AreEqual(0,Level.Vfx.ActiveParticles);
            } finally {AudioListener.pause=mute;typeof(DownhillController).GetProperty("State").SetValue(Level,DownhillState.Build);}
        }

        [UnityTest] public IEnumerator VfxRealDrillMissIsSilentAndWaterUsesSurfaceContact()
        {
            Level.LaunchDecomposeSeconds=Level.LaunchTravelSeconds=Level.LaunchReconstructSeconds=.02f;
            settings.CountdownStep=.02f;settings.GoDuration=.02f;Assert.IsTrue(Level.TryStart());
            float end=Time.realtimeSinceStartup+3;
            while(Level.State!=DownhillState.Running && Time.realtimeSinceStartup<end)yield return null;
            Assert.IsNotNull(Level.Run25D);Level.Feedback.Clear();
            typeof(GravityGarage25DRun).GetProperty("Stage").SetValue(Level.Run25D,Garage25DStage.Lower);
            var stone=Level.Course25D.Sandstone.GetComponent<Collider>();
            var events=new List<SandboxFeedbackEvent>();Level.Feedback.Presented+=events.Add;
            Level.Run25D.SweepDrill(Level.SimulationCore,Vector3.up*20,Vector3.up*21,.001f);
            Assert.IsFalse(events.Any(e=>e.Cue==SandboxSoundCue.Drill));
            Level.Run25D.SweepDrill(Level.SimulationCore,stone.bounds.center+Vector3.up,stone.bounds.center,.001f);
            Assert.AreEqual(1,events.Count(e=>e.Cue==SandboxSoundCue.Drill));
            Level.Run25D.SweepDrill(Level.SimulationCore,stone.bounds.center,stone.bounds.center,.001f);
            Assert.AreEqual(1,events.Count(e=>e.Cue==SandboxSoundCue.Drill));
            Level.Feedback.Clear();events.Clear();
            foreach(var body in Level.SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies)) {
                body.Rigidbody.isKinematic=true;body.transform.position=Level.Course25D.Point(5,-.3f,0);body.Rigidbody.position=body.transform.position;
            }
            Physics.SyncTransforms();yield return new WaitForFixedUpdate();yield return null;
            var splash=events.Single(e=>e.Cue==SandboxSoundCue.Splash);
            Assert.AreEqual(SandboxWater.SurfaceY,Level.Course25D.Coordinates(splash.Position).y,.0001f);
            Assert.AreEqual(2,Level.Vfx.ActiveRings);Assert.IsNull(Level.Course25D.transform.Find("Water splash"));
            Level.RestoreBuildImmediately();Assert.AreEqual(0,Level.Vfx.ActiveParticles);
        }

        [UnityTest] public IEnumerator VfxRejectedGhostPulsesOnceAndRestores()
        {
            var source=Part(Vector3.up*4);var target=Part(Vector3.up*5);var c=Prepare(source,target,Vector3.up);
            var ghost=Object.FindObjectsByType<AttachmentGhost>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single();
            ghost.Show(c,source,true);
            Assert.IsTrue(World.TryBeginAssembly(c));var blocker=Part(c.SnappedPose.position);
            yield return Finish();Assert.IsTrue(ghost.gameObject.activeSelf);
            yield return new WaitForSeconds(.25f);Assert.IsFalse(ghost.gameObject.activeSelf);
            Assert.IsFalse(World.Connections.Any(e=>e.a.partId==source.InstanceId));Object.DestroyImmediate(blocker.gameObject);
        }
    }
}
