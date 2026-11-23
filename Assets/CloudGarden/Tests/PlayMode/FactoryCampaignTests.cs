using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    public sealed class FactoryCampaignTests
    {
        DownhillController level;
        [UnitySetUp] public IEnumerator Setup()
        {
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("DownhillSandbox"); yield return null;
            level=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(level) level.RestoreBuildImmediately();
            yield return null; // Finish deferred collider destruction before starting a scene load.
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("MainMenu"); yield return null;
        }
        [UnityTest] public IEnumerator LevelFourCarSurvivesAdvanceToAuthoredLevelFivePalette()
        {
            // Use the current campaign's real prefabs, including Level 5's non-starter palette.
            while(level.CurrentCourse!=SandboxCourseId.DrillDropBridge) {
                SandboxCampaignSession.Complete(level.CurrentCourse,true);
                Assert.IsTrue(level.TryAdvanceLevel()); yield return null;
            }
            Assert.AreEqual(4,level.Presentation.Interface.CourseNumber);
            Assert.IsFalse(level.Settings.FactoryCourse(SandboxCourseId.FactoryLevel4).UseStarterPalette);
            Assert.IsNotEmpty(level.Workshop.World.Connections,"The starter construction reaches Level 4 intact.");
            var parts=level.Kit.ToArray();
            var ids=parts.Select(p=>p.InstanceId).ToArray();
            var edges=level.Workshop.World.Connections.Select(e=>JsonUtility.ToJson(e)).ToArray();
            var positions=parts.SelectMany(p=>p.Bodies).Select(b=>level.Course.WorkbenchFrame.InverseTransformPoint(b.gameObject.activeInHierarchy?b.Rigidbody.position:b.transform.position)).ToArray();
            var rotations=parts.SelectMany(p=>p.Bodies).Select(b=>Quaternion.Inverse(level.Course.WorkbenchFrame.rotation)*(b.gameObject.activeInHierarchy?b.Rigidbody.rotation:b.transform.rotation)).ToArray();
            SandboxCampaignSession.Complete(level.CurrentCourse,true);
            Assert.IsTrue(level.TryAdvanceLevel()); yield return null;
            Assert.AreEqual(SandboxCourseId.FactoryLevel4,level.CurrentCourse);
            CollectionAssert.AreEquivalent(ids,level.Kit.Select(p=>p.InstanceId));
            CollectionAssert.AreEquivalent(edges,level.Workshop.World.Connections.Select(e=>JsonUtility.ToJson(e)));
            int i=0;
            foreach(var id in ids) foreach(var body in level.Kit.Single(p=>p.InstanceId==id).Bodies) {
                Assert.Less(Vector3.Distance(positions[i],level.Course.WorkbenchFrame.InverseTransformPoint(body.Rigidbody.position)),.001f,body.Part.Kind+" local position");
                Assert.Less(Quaternion.Angle(rotations[i],Quaternion.Inverse(level.Course.WorkbenchFrame.rotation)*body.Rigidbody.rotation),.1f); i++;
            }
            Assert.IsTrue(level.Kit.Single(p=>p.Kind==PartKind.DownhillDrill).gameObject.activeInHierarchy);
            Assert.IsTrue(level.IsBuilding);
            Assert.IsFalse(SandboxCampaignSession.AllComplete);
            level.Settings.TransitionDuration=0;
            level.LaunchDecomposeSeconds=level.LaunchTravelSeconds=level.LaunchReconstructSeconds=.01f;
            Assert.IsTrue(level.TryStart());
            float deadline=Time.realtimeSinceStartup+5;
            while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.AreEqual(DownhillState.Running,level.State);
            Assert.IsTrue(level.TryReturn());
            deadline=Time.realtimeSinceStartup+5;
            while(!level.IsBuilding && Time.realtimeSinceStartup<deadline) yield return null;
            CollectionAssert.AreEquivalent(ids,level.Kit.Select(p=>p.InstanceId));
            CollectionAssert.AreEquivalent(edges,level.Workshop.World.Connections.Select(e=>JsonUtility.ToJson(e)));
        }

        [UnityTest] public IEnumerator DirectLevelFiveDesignEntryKeepsAuthoredPalette()
        {
            DesignModeSession.SetEnabled(true);
            DesignModeSession.SelectLevel((DesignLevelId)SandboxCourseId.FactoryLevel4);
            SceneManager.LoadScene("DownhillSandbox"); yield return null; yield return null;
            level=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
            Assert.AreEqual(SandboxCourseId.FactoryLevel4,level.CurrentCourse);
            Assert.IsFalse(level.FactoryLevel.UseStarterPalette);
            CollectionAssert.AreEquivalent(level.FactoryLevel.CampaignSpawns.Where(p=>p).Select(p=>p.Kind),level.Kit.Select(p=>p.Kind));
            Assert.IsTrue(level.TryLoadDesignPreset());
        }

        [UnityTest] public IEnumerator DesignModeOpensAllFivePublishedCourses()
        {
            foreach(var id in DesignLevelCatalog.Levels) {
                if(level)level.RestoreBuildImmediately();yield return null;
                DesignModeSession.SetEnabled(true);DesignModeSession.SelectLevel(id);
                SceneManager.LoadScene("DownhillSandbox");yield return null;yield return null;
                level=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
                Assert.AreEqual((SandboxCourseId)id,level.CurrentCourse);
                Assert.IsNotNull(level.FactoryLevel);
                Assert.AreEqual(5,level.Presentation.Interface.CourseCount);
                Assert.AreEqual(DesignLevelCatalog.Number(id),level.Presentation.Interface.CourseNumber);
                Assert.IsTrue(level.TryLoadDesignPreset(),"Default car for "+id);
                Assert.IsFalse(SandboxCampaignSession.IsComplete((SandboxCourseId)id));
            }
        }
        [UnityTest] public IEnumerator FiveCoursesPreserveConstructionGrantPartsAndCompleteOnlyAtFactory()
        {
            Assert.IsNotNull(level.Settings.Level4,"The actual Sandbox settings must reference the published level.");
            Assert.AreEqual(SandboxCourseId.BeautifulCorner,level.CurrentCourse);
            Assert.AreEqual(1,level.Presentation.Interface.CourseNumber);
            Assert.IsEmpty(level.Workshop.World.Connections,"Campaign must start with loose parts.");
            Assert.AreEqual(7,level.Workshop.World.Parts.Count);
            Assert.IsFalse(level.Kit.Single(p=>p.Kind==PartKind.DownhillDrill).gameObject.activeSelf);
            Assert.IsTrue(level.FactoryLevel.UseStarterPalette);
            var lockedCar=level.Settings.Level4.TestCar;
            Assert.IsTrue(lockedCar.Assembly.parts.Any(p=>p.kind==PartKind.DownhillDrill));
            Assert.IsFalse(level.TryLoadFactoryCar(lockedCar),"Level 1 cannot load a drill car through the factory API.");
            Assert.IsEmpty(level.Workshop.World.Connections);
            Assert.IsEmpty(level.FactoryLevel.Puzzles);
            var table=level.FactoryLevel.WorkbenchVisuals;
            Assert.IsFalse(table.IsChildOf(level.FactoryLevel.TrackRoot));
            Assert.IsTrue(table.IsChildOf(level.ActiveLayout.BuildingRoot));
            var tablePosition=table.position;
            var tableScale=table.lossyScale;
            var markerIds=level.Workshop.Layout.Spawns.Select(p=>p.GetInstanceID()).ToArray();
            var starterIds=level.Kit.Select(p=>p.InstanceId).ToArray();
            Assert.IsFalse(level.TryAdvanceLevel());
            // Exercise the shared starter building/snapping implementation before launch.
            Assert.IsTrue(level.TryLoadDesignerExample());
            Assert.IsNotEmpty(level.Workshop.World.Connections);
            Assert.IsTrue(level.BeginRotation(1,level.RotationWheel.transform.position+Vector3.right*.2f));
            level.EndRotation(1);
            Assert.IsFalse(level.Rotating);
            level.Settings.CountdownStep=.01f;level.Settings.GoDuration=.01f;level.Settings.TransitionDuration=0;
            level.LaunchDecomposeSeconds=level.LaunchTravelSeconds=level.LaunchReconstructSeconds=.01f;
            level.VictoryPauseSeconds=0;
            level.VictoryDecomposeSeconds=level.VictoryTravelSeconds=level.VictoryReconstructSeconds=.01f;
            for(int attempt=0;attempt<2;attempt++) {
                Assert.IsTrue(level.TryStart());float deadline=Time.realtimeSinceStartup+5;
                while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline) yield return null;
                Assert.AreEqual(DownhillState.Running,level.State);
                Assert.IsTrue(table.gameObject.activeInHierarchy);
                Assert.Less(Vector3.Distance(table.position,tablePosition),.0001f);
                Assert.AreEqual(tableScale,table.lossyScale);
                Assert.IsTrue(level.TryRetry());yield return null;
                Assert.IsTrue(level.TryReturn());yield return null;
                Assert.IsTrue(level.IsBuilding);
            }
            Assert.IsTrue(level.TryStart());float firstDeadline=Time.realtimeSinceStartup+5;
            while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<firstDeadline) yield return null;
            var firstOffset=level.FactoryLevel.Finish.transform.TransformPoint(level.FactoryLevel.Finish.center)-level.SimulationCore.MainBody.Rigidbody.position;
            foreach(var body in level.SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies)) { body.Rigidbody.isKinematic=true;body.Rigidbody.position+=firstOffset; }
            typeof(DownhillController).GetMethod("UpdateFactoryRun",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(level,null);
            firstDeadline=Time.realtimeSinceStartup+5;
            while(!level.IsBuilding && Time.realtimeSinceStartup<firstDeadline) yield return null;
            Assert.IsTrue(level.IsBuilding);Assert.IsTrue(SandboxCampaignSession.IsComplete(SandboxCourseId.BeautifulCorner));
            Assert.IsFalse(SandboxCampaignSession.AllComplete); Assert.IsNotNull(level.Reward);
            var edges=level.Workshop.World.Connections.Select(e=>e.id).ToArray();
            for(int number=2;number<=5;number++) {
                Assert.IsTrue(level.TryAdvanceLevel(),"Advance to "+number); yield return null;
                Assert.AreEqual(number,level.Presentation.Interface.CourseNumber);
                Assert.IsNotNull(level.FactoryLevel);
                Assert.IsFalse(SandboxCampaignSession.AllComplete);
                Assert.IsTrue(level.FactoryLevel.Validate(out var error),error);
                if(number<5) {
                    foreach(var id in starterIds) Assert.IsTrue(level.Kit.Any(p=>p.InstanceId==id));
                    CollectionAssert.AreEquivalent(edges,level.Workshop.World.Connections.Select(e=>e.id));
                    Assert.AreEqual(2,level.Kit.Count(p=>p.Kind==PartKind.DownhillLargeWheel && p.gameObject.activeSelf));
                    Assert.AreEqual(number>=3?3:1,level.Kit.Count(p=>p.Kind==PartKind.DownhillLongBlock));
                    Assert.AreEqual(number>=4,level.Kit.Single(p=>p.Kind==PartKind.DownhillDrill).gameObject.activeSelf);
                }
                SandboxCampaignSession.Complete(level.CurrentCourse,true); yield return null;
            }
            Assert.IsTrue(SandboxCampaignSession.AllComplete);
            Assert.IsFalse(level.TryAdvanceLevel());
        }
    }
}
