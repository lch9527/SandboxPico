using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Tests
{
    public sealed partial class SandboxAttachmentTests
    {
        [UnityTest] public IEnumerator AudioAttachmentFiresAtCommitAndRestoringConnectionsIsSilent()
        {
            var cues = new List<SandboxSoundCue>(); Level.Audio.CuePlayed += cues.Add;
            var target = Part(new Vector3(0,4,0)); var source = Part(new Vector3(1,4,0));
            var candidate = Prepare(source,target,Vector3.up,15);
            Assert.IsTrue(World.TryBeginAssembly(candidate));
            Assert.AreEqual(0,cues.Count(c=>c==SandboxSoundCue.Attach));
            yield return new WaitForSeconds(.08f);
            Assert.AreEqual(0,cues.Count(c=>c==SandboxSoundCue.Attach),"No early click during alignment");
            yield return Finish();
            Assert.AreEqual(1,cues.Count(c=>c==SandboxSoundCue.Attach));
            var record=World.Connections.Single(c=>c.a.partId==source.InstanceId);
            World.RemoveConnection(record.id); Assert.IsTrue(World.Connect(record));
            Assert.AreEqual(1,cues.Count(c=>c==SandboxSoundCue.Attach),"Save reconstruction stays silent");
            Assert.AreEqual(0,cues.Count(c=>c==SandboxSoundCue.Detach),"Raw removal stays silent");
        }

        [UnityTest] public IEnumerator AudioCancelledAlignmentAndTransferNeverPlayDelayedSuccess()
        {
            var cues=new List<SandboxSoundCue>(); Level.Audio.CuePlayed+=cues.Add;
            var source=Part(Vector3.up*4); var candidate=Prepare(source,Level.Core,Vector3.up,20);
            Assert.IsTrue(World.TryBeginAssembly(candidate));
            yield return new WaitForFixedUpdate(); World.CancelAlignments();
            Level.Audio.BeginTransfer(source.transform,Level.Core.transform,.2f,.5f);
            Level.Audio.StopAll();
            yield return new WaitForSecondsRealtime(.8f);
            Assert.AreEqual(0,cues.Count(c=>c==SandboxSoundCue.Attach || c==SandboxSoundCue.Arrive || c==SandboxSoundCue.Success));
            Assert.AreEqual(0,Level.Audio.ActiveOneShots);
            Assert.IsFalse(Level.Audio.GetComponentsInChildren<AudioSource>().Any(s=>s.isPlaying && s.outputAudioMixerGroup!=Level.Audio.Bank.Ambience),"Only environmental ambience may resume in Build");
        }

        [UnityTest] public IEnumerator AudioVariationsCooldownAndPoolStayBounded()
        {
            var audio=Level.Audio; var entry=audio.Bank.Find(SandboxSoundCue.Attach);
            AudioClip last=null;
            for(int i=0;i<8;i++) {
                audio.StopAll(); Assert.IsTrue(audio.Play(SandboxSoundCue.Attach,Level.Core.transform.position));
                var source=audio.GetComponentsInChildren<AudioSource>().Single(s=>s.isPlaying);
                Assert.AreNotEqual(last,source.clip,"Avoid immediate sample repetition"); last=source.clip;
                Assert.AreEqual(audio.Bank.Interactions,source.outputAudioMixerGroup);
            }
            audio.StopAll(); Assert.IsTrue(audio.Play(SandboxSoundCue.Reject,Vector3.zero));
            Assert.IsFalse(audio.Play(SandboxSoundCue.Reject,Vector3.zero),"One rejected attempt cannot chatter");
            audio.StopAll();
            for(int i=0;i<100;i++)audio.Play(SandboxSoundCue.WoodImpact,Vector3.zero,1,i);
            Assert.LessOrEqual(audio.ActiveOneShots,audio.Bank.Find(SandboxSoundCue.WoodImpact).MaxVoices);
            Assert.AreEqual(audio.Bank.OneShotVoices+5,audio.GetComponentsInChildren<AudioSource>().Length);
            audio.StopAll(); yield return null;
        }

        [UnityTest] public IEnumerator AudioPauseAndDisableStopLoopsAndPendingTransfer()
        {
            var audio=Level.Audio;
            audio.BeginTransfer(Level.Core.transform,Level.Course.StartPoint,0,1);
            yield return null;
            Assert.IsTrue(audio.GetComponentsInChildren<AudioSource>().Any(s=>s.isPlaying));
            float scale=Time.timeScale;
            try {
                Time.timeScale=0; yield return null; yield return null;
                Assert.IsFalse(audio.GetComponentsInChildren<AudioSource>().Any(s=>s.isPlaying));
                Assert.IsFalse(audio.Play(SandboxSoundCue.Attach,Vector3.zero));
            } finally { Time.timeScale=scale; }
            audio.enabled=false;
            Assert.IsFalse(audio.GetComponentsInChildren<AudioSource>().Any(s=>s.isPlaying));
            audio.enabled=true; Assert.IsTrue(audio.Play(SandboxSoundCue.Grab,Vector3.zero));
            audio.StopAll();
        }

        [UnityTest] public IEnumerator AudioLaunchRetryAndReturnDoNotDuplicateOrLeak()
        {
            var cues=new List<SandboxSoundCue>(); Level.Audio.CuePlayed+=cues.Add;
            Level.LaunchDecomposeSeconds=Level.LaunchTravelSeconds=Level.LaunchReconstructSeconds=.02f;
            settings.CountdownStep=.02f;settings.GoDuration=.02f;
            Assert.IsTrue(Level.TryStart());
            float end=Time.realtimeSinceStartup+2;
            while(Level.State!=DownhillState.Running && Time.realtimeSinceStartup<end)yield return null;
            Assert.AreEqual(DownhillState.Running,Level.State);
            Assert.AreEqual(1,cues.Count(c=>c==SandboxSoundCue.Gather));
            Assert.AreEqual(1,cues.Count(c=>c==SandboxSoundCue.Arrive));
            Assert.AreEqual(1,cues.Count(c=>c==SandboxSoundCue.Go));
            Assert.AreEqual(0,cues.Count(c=>c==SandboxSoundCue.Countdown),"Campaign skips 3-2-1");
            Assert.IsTrue(Level.TryRetry());
            end=Time.realtimeSinceStartup+2;
            while(Level.State!=DownhillState.Running && Time.realtimeSinceStartup<end)yield return null;
            Assert.AreEqual(2,cues.Count(c=>c==SandboxSoundCue.Go));
            Level.RestoreBuildImmediately();yield return null;
            Assert.AreEqual(0,Level.Audio.ActiveOneShots);Assert.AreEqual(0,Level.Audio.RollingVolume);
            Assert.IsFalse(Level.Audio.GetComponentsInChildren<AudioSource>().Any(s=>s.isPlaying && s.outputAudioMixerGroup!=Level.Audio.Bank.Ambience),"Vehicle and transfer audio must stop on return");
        }

        [UnityTest] public IEnumerator AudioWheelContactDrivesRollingAndStopsInAir()
        {
            var simulation=new GameObject("Audio physics fixture");var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position=new Vector3(0,8,0);floor.transform.localScale=new Vector3(6,.2f,6);
            var world=simulation.AddComponent<AssemblyWorld>();
            var wheel=DownhillParts.Create(world,PartKind.DownhillLargeWheel,new Pose(new Vector3(0,8.6f,0),Quaternion.identity),1,settings);
            wheel.transform.SetParent(simulation.transform,true);
            var levelType=typeof(DownhillController);
            levelType.GetProperty("SimulationWorld").SetValue(Level,world);
            levelType.GetProperty("State").SetValue(Level,DownhillState.Running);
            Level.Audio.BindSimulation();
            var probe=wheel.GetComponentsInChildren<SandboxBodyAudio>().First(p=>p.Wheel);
            var hits=new List<SandboxSoundCue>();Level.Audio.CuePlayed+=hits.Add;
            try {
                foreach(var body in wheel.Bodies){body.Rigidbody.isKinematic=false;body.Rigidbody.useGravity=true;}
                float end=Time.realtimeSinceStartup+2;
                while(!probe.Grounded && Time.realtimeSinceStartup<end)yield return new WaitForFixedUpdate();
                Assert.IsTrue(probe.Grounded,"Actual wheel-floor contact establishes support");
                Assert.Contains(SandboxSoundCue.RubberImpact,hits);
                for(int i=0;i<20;i++){
                    foreach(var body in wheel.Bodies)body.Rigidbody.linearVelocity=Vector3.forward*.8f;
                    yield return new WaitForFixedUpdate();
                }
                yield return null;Assert.Greater(Level.Audio.RollingVolume,.02f);
                Object.DestroyImmediate(floor);floor=null;
                foreach(var body in wheel.Bodies){body.Rigidbody.useGravity=false;body.Rigidbody.linearVelocity=Vector3.forward*.8f;}
                yield return new WaitForSecondsRealtime(.3f);
                Assert.IsFalse(probe.Grounded);Assert.Less(Level.Audio.RollingVolume,.001f,"Airborne speed must not produce wheel rumble");
                Level.Audio.StopAll();hits.Clear();
                Level.Audio.Impact(SandboxSurfaceMaterial.Wood,Vector3.zero,.001f,1);Assert.IsEmpty(hits);
                for(int i=0;i<50;i++)Level.Audio.Impact(SandboxSurfaceMaterial.Metal,Vector3.zero,2,i);
                Assert.AreEqual(1,hits.Count,"One contact burst is rate-limited across bodies");
            } finally {
                Level.Audio.StopAll();levelType.GetProperty("State").SetValue(Level,DownhillState.Build);
                levelType.GetProperty("SimulationWorld").SetValue(Level,null);
                if(floor)Object.DestroyImmediate(floor);Object.DestroyImmediate(simulation);
            }
        }
    }
}
