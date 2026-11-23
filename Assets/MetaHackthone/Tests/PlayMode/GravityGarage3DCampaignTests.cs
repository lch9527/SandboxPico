using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    [Ignore("Legacy 3D valley carry-over is no longer reachable: the campaign is five factory courses and advancing requires a published factory prefab.")]
    public sealed class GravityGarage3DCampaignTests
    {
        GameObject root;WorkshopBootstrap workshop;DownhillSettings settings;GravityGarage3DLevelAsset valley;
        [UnityTest]public IEnumerator CampaignCarryoverRetryAndBuildRestoration()
        {
            SandboxCampaignSession.Reset(); SandboxCampaignSession.Complete(SandboxCourseId.BeautifulCorner); SandboxCampaignSession.Advance();DesignModeSession.SetEnabled(false);
            settings=ScriptableObject.CreateInstance<DownhillSettings>();valley=ScriptableObject.CreateInstance<GravityGarage3DLevelAsset>();valley.Scenery=false;
            settings.Level3D=valley;settings.Campaign=settings.DesignerBuilder=settings.UseGravityGarage=settings.SandboxDrillCourse=true;settings.TransitionDuration=0;settings.CountdownStep=settings.GoDuration=.01f;
            root=new GameObject("Valley campaign test");root.SetActive(false);workshop=root.AddComponent<WorkshopBootstrap>();workshop.DownhillSettings=settings;root.SetActive(true);yield return null;
            var level=workshop.Downhill;Assert.AreEqual(SandboxCourseId.GravityGarage,level.CurrentCourse);Assert.IsNull(level.Course3D);Assert.IsNotNull(level.GarageCourse);
            float firstScale=level.TestScale;
            Assert.That(firstScale,Is.EqualTo(DownhillSettings.VehicleScale*settings.PresentationScale).Within(.00001f));
            DesignModeSession.SetEnabled(true);Assert.IsTrue(level.TryLoadDesignerExample());DesignModeSession.SetEnabled(false);
            var ids=level.Kit.Select(p=>p.InstanceId).ToArray();var buildScales=level.Kit.Select(p=>p.GeometryScale).ToArray();var edges=workshop.World.Connections.Select(e=>e.id).ToArray();
            level.SpawnPreview.Refresh();var preview=level.SpawnPreview.GetComponentsInChildren<MeshFilter>()[0];var firstPreviewScale=preview.transform.localScale;
            SandboxCampaignSession.Complete(SandboxCourseId.GravityGarage);
            Assert.IsTrue(level.TryAdvanceLevel());Assert.AreEqual(SandboxCourseId.TeleportPipes,level.CurrentCourse);Assert.AreSame(valley,settings.Level3D);Assert.NotNull(level.Course3D);
            CollectionAssert.AreEqual(ids,level.Kit.Select(p=>p.InstanceId));CollectionAssert.AreEqual(edges,workshop.World.Connections.Select(e=>e.id));
            Assert.That(level.TestScale,Is.EqualTo(firstScale*.6f).Within(.00001f));
            CollectionAssert.AreEqual(buildScales,level.Kit.Select(p=>p.GeometryScale));
            Assert.That(level.Course3D.WorldScale,Is.EqualTo(GravityGarage25DCourse.UnitScale*firstScale).Within(.00001f),"The course must keep its original scale.");
            Assert.Less(Vector3.Distance(preview.transform.localScale,firstPreviewScale*.6f),.00001f,"Preview must shrink even when advancing preserves topology.");
            for(int i=0;i<3;i++){
                Assert.IsTrue(level.TryStart());yield return WaitForRun(level);Assert.NotNull(level.Run3D);Assert.AreEqual(6,level.SimulationWorld.Connections.Count);
                Assert.That(level.SimulationCore.GeometryScale,Is.EqualTo(firstScale*.6f).Within(.00001f));
                Assert.IsTrue(level.TryRetry());Assert.IsFalse(level.Course3D.WallBroken);yield return WaitForRun(level);Assert.IsTrue(level.TryReturn());yield return null;
                Assert.AreEqual(DownhillState.Build,level.State);CollectionAssert.AreEqual(ids,level.Kit.Select(p=>p.InstanceId));CollectionAssert.AreEqual(edges,workshop.World.Connections.Select(e=>e.id));
                CollectionAssert.AreEqual(buildScales,level.Kit.Select(p=>p.GeometryScale));
            }
        }
        static IEnumerator WaitForRun(DownhillController level)
        {
            float deadline=Time.realtimeSinceStartup+5;
            while((level.State==DownhillState.Transition || level.State==DownhillState.Countdown) && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.AreEqual(DownhillState.Running,level.State,"Countdown must finish before testing retry or return.");
        }
        [TearDown]public void Cleanup()
        {
            DesignModeSession.SetEnabled(false);SandboxCampaignSession.Reset();if(workshop&&workshop.Downhill)workshop.Downhill.RestoreBuildImmediately();if(root)Object.DestroyImmediate(root);
            foreach(var part in Object.FindObjectsByType<AssemblyPart>(FindObjectsSortMode.None))Object.DestroyImmediate(part.gameObject);
            foreach(var environment in Object.FindObjectsByType<WorkshopEnvironment>(FindObjectsSortMode.None))Object.DestroyImmediate(environment.gameObject);
            if(settings)Object.DestroyImmediate(settings);if(valley)Object.DestroyImmediate(valley);
        }
    }
}
