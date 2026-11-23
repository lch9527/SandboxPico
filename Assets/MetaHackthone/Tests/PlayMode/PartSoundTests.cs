using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    // Grabbing sounds like the part (wood, rubber wheel, metal drill), and breaking the drill target is heard.
    public sealed class PartSoundTests
    {
        DownhillController level;
        static void Comparing(DownhillController l,bool value) => typeof(DownhillController).GetProperty("GarageComparing").SetValue(l,value);

        IEnumerator LoadDrillBridge()
        {
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            foreach(var id in SandboxCourseCatalog.Courses) SandboxCampaignSession.Complete(id);
            SceneManager.LoadScene("DownhillSandbox"); yield return null; yield return null;
            level=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
            var load=typeof(DownhillController).GetMethod("LoadFactoryCourse",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.IsTrue((bool)load.Invoke(level,new object[]{SandboxCourseId.DrillDropBridge,true})); yield return null;
            Assert.IsTrue(level.TryLoadFactoryCar(level.FactoryLevel.Presets.First(c=>c && c.name=="High drill")));
            yield return null;
        }

        [Test] public void GrabCueFollowsPartKind()
        {
            Assert.AreEqual(SandboxSoundCue.Grab,SandboxPresentation.GrabCue(null));
            var go=new GameObject("probe"); var part=go.AddComponent<AssemblyPart>();
            try {
                foreach(var (kind,cue) in new[]{(PartKind.DownhillLongBlock,SandboxSoundCue.Grab),(PartKind.DownhillCore,SandboxSoundCue.Grab),
                    (PartKind.DownhillSmallWheel,SandboxSoundCue.GrabWheel),(PartKind.DownhillLargeWheel,SandboxSoundCue.GrabWheel),(PartKind.DownhillDrill,SandboxSoundCue.GrabDrill)}) {
                    part.Kind=kind;
                    Assert.AreEqual(cue,SandboxPresentation.GrabCue(part),kind.ToString());
                }
            } finally { Object.DestroyImmediate(go); }
        }

        [UnityTest] public IEnumerator GrabbingWheelAndDrillPlaysTheirOwnSounds()
        {
            yield return LoadDrillBridge();
            var world=level.Workshop.World; var played=new List<SandboxSoundCue>(); level.Audio.CuePlayed+=played.Add;
            var wheel=level.Kit.First(p=>p.Kind==PartKind.DownhillSmallWheel || p.Kind==PartKind.DownhillLargeWheel);
            var drill=level.Kit.First(p=>p.Kind==PartKind.DownhillDrill);
            foreach(var (part,cue) in new[]{(wheel,SandboxSoundCue.GrabWheel),(drill,SandboxSoundCue.GrabDrill)}) {
                played.Clear();
                Assert.IsTrue(world.TryGrab(0,part,new Pose(part.MainBody.Rigidbody.position,part.MainBody.Rigidbody.rotation)),part.Kind.ToString());
                yield return null;
                CollectionAssert.Contains(played,cue,part.Kind+" grab");
                CollectionAssert.DoesNotContain(played,SandboxSoundCue.Grab,part.Kind+" does not use the wooden grab");
                world.Release(0,false); yield return new WaitForSecondsRealtime(.15f);
            }
        }

        [UnityTest, Timeout(120000)] public IEnumerator DrillBreakingTheTargetPlaysBreakOnce()
        {
            yield return LoadDrillBridge();
            var played=new List<SandboxSoundCue>(); level.Audio.CuePlayed+=played.Add;
            var bridge=level.FactoryLevel.Puzzles.OfType<SandboxDropBridge>().First();
            Assert.IsTrue(level.TryStart()); Comparing(level,true);
            float deadline=Time.realtimeSinceStartup+30;
            while(!bridge.Released && level.State!=DownhillState.Failed && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.IsTrue(bridge.Released,"The high drill breaks the target");
            yield return null;
            Assert.AreEqual(1,played.Count(c=>c==SandboxSoundCue.DrillBreak),"One break sound");
            for(int i=0;i<30;i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(1,played.Count(c=>c==SandboxSoundCue.DrillBreak),"No repeat after the target is gone");
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(level) Comparing(level,false);
            SandboxCampaignSession.Reset(); DesignModeSession.SetEnabled(false);
            SceneManager.LoadScene("MainMenu"); yield return null;
        }
    }
}
