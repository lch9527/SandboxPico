using System.Collections;
using System.IO;
using System.Linq;
using CloudGarden.Integration;
using NUnit.Framework;
using PhysicalAssembly;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CloudGarden.Tests
{
    public sealed class DesignModeTests
    {
        [UnitySetUp] public IEnumerator Setup()
        {
            DesignModeSession.SetEnabled(false); PlayAreaPose.Reset();
            SceneManager.LoadScene("MainMenu"); yield return null; yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            var level=GardenNavigation.Current?GardenNavigation.Current.DownhillWorkshop:null;
            if(level && level.Downhill) level.Downhill.RestoreBuildImmediately();
            yield return null; // Finish deferred factory MeshCollider destruction before scene replacement.
            DesignModeSession.SetEnabled(false); PlayAreaPose.Reset();
            SceneManager.LoadScene("MainMenu"); yield return null;
        }
        static IEnumerator NavigationReady(GardenNavigation nav)
        {
            float deadline=Time.realtimeSinceStartup+20;
            while(nav.IsTransitioning && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.IsFalse(nav.IsTransitioning); yield return null; yield return null;
        }
        static IEnumerator Enable(GardenNavigation nav)
        {
            Assert.IsTrue(nav.TrySetDesignMode(true)); yield return NavigationReady(nav);
            Assert.IsTrue(DesignModeSession.Enabled); Assert.IsTrue(nav.Menu.Visible);
            Assert.IsFalse(nav.Player.IsXR); Assert.IsTrue(nav.Player.DesktopCamera.isActiveAndEnabled);
            Assert.IsFalse(nav.Player.XRRig.gameObject.activeSelf);
        }
        [UnityTest] public IEnumerator ExplicitModeSwitchResetsTemporaryLevelsAndNormalModeKeepsItsKit()
        {
            var nav=GardenNavigation.Current;
            nav.EnterMode(GameMode.CarWorkshop); yield return NavigationReady(nav);
            Assert.AreEqual(15,nav.CarWorkshop.World.Parts.Count);
            nav.ReturnToMainMenu(); yield return NavigationReady(nav);
            yield return Enable(nav);
            Assert.IsFalse(SceneManager.GetSceneByName("Workshop").isLoaded);
            nav.EnterMode(GameMode.CarWorkshop); yield return NavigationReady(nav);
            Assert.AreEqual(6,nav.CarWorkshop.World.Parts.Count);
            Assert.AreEqual(5,nav.CarWorkshop.World.Connections.Count);
            Assert.AreEqual(WorkshopMode.Build,nav.CarWorkshop.Session.Mode);
            Assert.IsFalse(nav.TrySetDesignMode(false),"Mode changes must go through the main menu.");
            nav.ReturnToMainMenu(); yield return NavigationReady(nav);
            Assert.IsTrue(nav.TrySetDesignMode(false)); yield return NavigationReady(nav);
            nav.EnterMode(GameMode.CarWorkshop); yield return NavigationReady(nav);
            Assert.AreEqual(15,nav.CarWorkshop.World.Parts.Count); Assert.IsEmpty(nav.CarWorkshop.World.Connections);
        }
        [UnityTest] public IEnumerator WorkshopResetUsesEditedSpawnAndCannotWritePlayerSave()
        {
            var nav=GardenNavigation.Current; yield return Enable(nav);
            nav.EnterMode(GameMode.CarWorkshop); yield return NavigationReady(nav);
            var owner=nav.CarWorkshop; var marker=owner.Layout.DesignCarSpawn;
            Assert.NotNull(marker);
            marker.SetPositionAndRotation(owner.Arena.TransformPoint(new Vector3(.15f,.70f,-.25f)),owner.Arena.rotation*Quaternion.Euler(0,23,0));
            for(int i=0;i<3;i++)
            {
                Assert.IsTrue(owner.TryLoadDesignPreset());
                var car=owner.Session.Selected;
                Assert.Less(Vector3.Distance(car.MainBody.Rigidbody.position,marker.position),.001f);
                Assert.Less(Quaternion.Angle(car.MainBody.Rigidbody.rotation,marker.rotation),.01f);
                Assert.AreEqual(6,owner.World.Parts.Count); Assert.AreEqual(5,owner.World.Connections.Count);
            }
            owner.GetComponent<DesktopInputAdapter>().SetInputFocus(true);
            var selected=owner.Session.Selected;
            Assert.IsTrue(owner.World.TryGrab(2,selected,new Pose(selected.MainBody.Rigidbody.position,selected.MainBody.Rigidbody.rotation)));
            Assert.IsFalse(owner.TryLoadDesignPreset());
            owner.GetComponent<DesktopInputAdapter>().SetInputFocus(false); Assert.IsFalse(owner.World.HasHolds);
            string before=File.Exists(owner.Saves.SavePath)?File.ReadAllText(owner.Saves.SavePath):null;
            owner.InteractionButtons.Single(b=>b.Command=="Save construction").Press();
            string after=File.Exists(owner.Saves.SavePath)?File.ReadAllText(owner.Saves.SavePath):null;
            Assert.AreEqual(before,after);
            var ids=owner.World.Parts.Keys.OrderBy(id=>id).ToArray();
            nav.ReturnToMainMenu(); yield return NavigationReady(nav);
            nav.EnterMode(GameMode.CarWorkshop); yield return NavigationReady(nav);
            CollectionAssert.AreEqual(ids,nav.CarWorkshop.World.Parts.Keys.OrderBy(id=>id));
        }
        [UnityTest] public IEnumerator DesignerExampleTestsAndReturnsThreeTimesAtWorkbench()
        {
            var nav=GardenNavigation.Current; yield return Enable(nav);
            nav.EnterDesignLevel(DesignLevelId.GravityGarage); yield return NavigationReady(nav);
            var owner=nav.DownhillWorkshop; var level=owner.Downhill;
            Assert.AreEqual(6,owner.World.Parts.Count); Assert.AreEqual(5,owner.World.Connections.Count);
            Assert.IsTrue(owner.TryLoadDesignPreset());
            var ids=owner.World.Parts.Keys.OrderBy(id=>id).ToArray();
            float clock=Time.timeScale,step=Time.fixedDeltaTime;
            FastLaunch(level);
            for(int attempt=0;attempt<3;attempt++)
            {
                Assert.IsTrue(level.TryStart());
                Assert.IsFalse(owner.TryLoadDesignPreset(),"Reset must not replace a running vehicle.");
                float deadline=Time.realtimeSinceStartup+5;
                while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline) yield return null;
                Assert.AreEqual(DownhillState.Running,level.State);
                Assert.NotNull(level.FactoryLevel); Assert.AreEqual(5,level.SimulationWorld.Connections.Count);
                var simulation=level.SimulationCore;
                Assert.IsTrue(level.TryRetry()); Assert.AreSame(simulation,level.SimulationCore);
                Assert.IsTrue(level.TryReturn());
                deadline=Time.realtimeSinceStartup+5;
                while(!level.IsBuilding && Time.realtimeSinceStartup<deadline) yield return null;
                Assert.IsTrue(level.IsBuilding); yield return null;
                Assert.IsNull(level.SimulationWorld);
                CollectionAssert.AreEqual(ids,owner.World.Parts.Keys.OrderBy(id=>id));
                Assert.AreEqual(clock,Time.timeScale,.0001f); Assert.AreEqual(step,Time.fixedDeltaTime,.0001f);
            }
            nav.ReturnToMainMenu(); yield return NavigationReady(nav);
        }
        static void FastLaunch(DownhillController level)
        {
            level.Settings.CountdownStep=.01f; level.Settings.GoDuration=.01f; level.Settings.TransitionDuration=0;
            level.LaunchDecomposeSeconds=level.LaunchTravelSeconds=level.LaunchReconstructSeconds=.01f;
        }
        [UnityTest] public IEnumerator CrabBridgeTestAndRetryPreserveAuthoredRoads()
        {
            var nav=GardenNavigation.Current; yield return Enable(nav);
            nav.EnterDesignLevel(DesignLevelId.WaterfallValley); yield return NavigationReady(nav);
            var owner=nav.DownhillWorkshop; var level=owner.Downhill; var factory=level.FactoryLevel;
            Assert.NotNull(factory);
            Assert.That(level.TestScale,Is.EqualTo(level.Presentation.TestScale).Within(.0001f),"The replacement factory course uses its authored scale.");
            var roads=factory.Tracks.SelectMany(t=>t.Generated.GetComponentsInChildren<MeshFilter>()).ToArray();
            var roadIds=roads.Select(r=>r.GetInstanceID()).ToArray();
            var meshes=roads.Select(r=>r.sharedMesh).ToArray();
            for(int reset=0;reset<3;reset++) {
                Assert.IsTrue(owner.TryLoadDesignPreset());
                var parts=owner.World.GetConnectedParts(level.Core.InstanceId);
                Assert.AreEqual(8,parts.Count); Assert.AreEqual(7,owner.World.Connections.Count);
                Assert.AreEqual(4,parts.Count(p=>DownhillParts.IsWheel(p.Kind)));
                Assert.AreEqual(3,parts.Count(p=>p.Kind==PartKind.DownhillLongBlock));
                Assert.IsFalse(parts.Any(p=>p.Kind==PartKind.DownhillDrill));
            }
            FastLaunch(level); Assert.IsTrue(level.TryStart());
            float deadline=Time.realtimeSinceStartup+5;
            while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.AreEqual(DownhillState.Running,level.State);
            Assert.AreEqual(7,level.SimulationWorld.Connections.Count);
            Assert.IsTrue(level.SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies).All(b=>!b.Rigidbody.isKinematic && b.Rigidbody.useGravity));
            Assert.IsTrue(level.TryRetry());
            CollectionAssert.AreEqual(roadIds,factory.Tracks.SelectMany(t=>t.Generated.GetComponentsInChildren<MeshFilter>()).Select(r=>r.GetInstanceID()));
            Assert.IsTrue(level.TryReturn());
            deadline=Time.realtimeSinceStartup+5;
            while(!level.IsBuilding && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.IsTrue(level.IsBuilding);
            CollectionAssert.AreEqual(roadIds,factory.Tracks.SelectMany(t=>t.Generated.GetComponentsInChildren<MeshFilter>()).Select(r=>r.GetInstanceID()));
            CollectionAssert.AreEqual(meshes,roads.Select(r=>r.sharedMesh));
        }
        [UnityTest] public IEnumerator PuzzlePreviewsSwitchCoursesWithoutCampaignProgress()
        {
            SandboxCampaignSession.Reset();
            var nav=GardenNavigation.Current; yield return Enable(nav);
            foreach(var id in new[]{DesignLevelId.WaterfallValley,DesignLevelId.GravityGarage,DesignLevelId.WaterfallValley}) {
                nav.EnterDesignLevel(id); yield return NavigationReady(nav);
                var level=nav.DownhillWorkshop.Downhill;
                Assert.AreEqual(id,DesignModeSession.SelectedLevel);
                Assert.NotNull(level.FactoryLevel); Assert.IsFalse(level.Settings.Campaign);
                var authored=nav.DownhillWorkshop.Layout.GetComponent<AuthoredSandboxLayout>();
                Assert.IsFalse(authored.Level1.gameObject.activeInHierarchy);
                Assert.IsFalse(authored.Level2.gameObject.activeInHierarchy);
                bool crab=id==DesignLevelId.WaterfallValley;
                Assert.AreEqual(crab?3:2,level.Presentation.Interface.CourseNumber);
                Assert.AreEqual(crab?"Crab Bridge":"Wheel Size Challenge",level.Presentation.Interface.CourseTitle);
                Assert.IsTrue(level.IsBuilding); Assert.AreEqual(crab?8:6,nav.DownhillWorkshop.World.Parts.Count);
                Assert.IsFalse(level.Kit.Any(p=>p.Kind==PartKind.DownhillDrill));
                foreach(var course in SandboxCourseCatalog.Courses) Assert.IsFalse(SandboxCampaignSession.IsComplete(course));
                nav.ReturnToMainMenu(); yield return NavigationReady(nav);
            }
            Assert.IsTrue(nav.TrySetDesignMode(false)); yield return NavigationReady(nav);
            Assert.AreEqual(DesignLevelId.GravityGarage,DesignModeSession.SelectedLevel);
            nav.EnterSandbox(); yield return NavigationReady(nav);
            Assert.AreEqual(SandboxCourseId.BeautifulCorner,nav.DownhillWorkshop.Downhill.CurrentCourse);
            Assert.IsEmpty(nav.DownhillWorkshop.World.Connections);
        }
        [Test] public void CatalogOrdersAllFiveDesignLevelsWithoutChangingSerializedIds()
        {
            CollectionAssert.AreEqual(new[]{DesignLevelId.BeautifulCorner,DesignLevelId.GravityGarage,DesignLevelId.WaterfallValley,DesignLevelId.DrillDropBridge,DesignLevelId.FactoryFinalRun},DesignLevelCatalog.Levels);
            CollectionAssert.AreEqual(new[]{"Level 1 - Beautiful Corner","Level 2 - Wheel Size Challenge","Level 3 - Crab Bridge","Level 4 - Drill Drop Bridge","Level 5 - Factory Final Run"},DesignLevelCatalog.Levels.Select(DesignLevelCatalog.Title));
            Assert.IsFalse(DesignLevelCatalog.IsValid((DesignLevelId)5));
            CollectionAssert.AreEqual(new[]{1,2,3,4,5},DesignLevelCatalog.Levels.Select(DesignLevelCatalog.Number));
            Assert.AreEqual(0,(int)DesignLevelId.GravityGarage); Assert.AreEqual(1,(int)DesignLevelId.WaterfallValley);
        }
        [UnityTest] public IEnumerator FactoryPreviewUsesPublishedCourseAndSwitchesWithoutCampaignProgress()
        {
            SandboxCampaignSession.Reset();
            var nav=GardenNavigation.Current;yield return Enable(nav);
            nav.EnterDesignLevel(DesignLevelId.FactoryFinalRun);yield return NavigationReady(nav);
            var owner=nav.DownhillWorkshop;var level=owner.Downhill;var factory=level.FactoryLevel;
            Assert.NotNull(factory);Assert.IsFalse(level.Settings.Campaign);
            Assert.AreEqual(SandboxCourseId.FactoryLevel4,level.CurrentCourse);
            Assert.AreEqual(5,level.Presentation.Interface.CourseNumber);
            Assert.AreEqual(5,level.Presentation.Interface.CourseCount);
            Assert.AreEqual(factory.Title,level.Presentation.Interface.CourseTitle);
            var authored=owner.Layout.GetComponent<AuthoredSandboxLayout>();
            Assert.IsFalse(authored.Level1.gameObject.activeInHierarchy);Assert.IsFalse(authored.Level2.gameObject.activeInHierarchy);
            var meshes=factory.Tracks.SelectMany(t=>t.Generated.GetComponentsInChildren<MeshFilter>()).Select(f=>f.sharedMesh).ToArray();
            CollectionAssert.AreEqual(level.Settings.Level4.Tracks.SelectMany(t=>t.Generated.GetComponentsInChildren<MeshFilter>()).Select(f=>f.sharedMesh),meshes);
            Assert.IsTrue(factory.Tracks.All(t=>t.DeckStyle==SandboxDeckStyle.CreekDeck));
            Assert.AreEqual(factory.TestCar.Assembly.parts.Count,level.Kit.Count);
            Assert.IsNotEmpty(owner.World.Connections);
            Assert.IsTrue(owner.TryLoadDesignPreset());
            Assert.AreEqual(factory.TestCar.Assembly.parts.Count,level.Kit.Count);
            level.Settings.CountdownStep=.01f;level.Settings.GoDuration=.01f;level.Settings.TransitionDuration=0;
            level.LaunchDecomposeSeconds=level.LaunchTravelSeconds=level.LaunchReconstructSeconds=.01f;
            Assert.IsTrue(level.TryStart());
            float deadline=Time.realtimeSinceStartup+5;
            while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline)yield return null;
            Assert.AreEqual(DownhillState.Running,level.State);Assert.IsTrue(factory.Tracks.All(t=>t.Generated.activeInHierarchy));
            Assert.IsTrue(level.TryRetry());yield return null;
            Assert.IsTrue(level.TryReturn());yield return null;Assert.IsTrue(level.IsBuilding);
            CollectionAssert.AreEqual(meshes,factory.Tracks.SelectMany(t=>t.Generated.GetComponentsInChildren<MeshFilter>()).Select(f=>f.sharedMesh));
            typeof(DownhillController).GetMethod("Resolve",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(level,new object[]{true});
            Assert.IsFalse(SandboxCampaignSession.AllComplete);Assert.IsFalse(SandboxCampaignSession.SecondComplete);
            Assert.IsFalse(SandboxCampaignSession.FirstComplete);Assert.IsFalse(SandboxCampaignSession.DrillClaimed);
            Assert.AreEqual(SandboxCourseId.BeautifulCorner,SandboxCampaignSession.Current);
            Assert.IsFalse(level.CanAdvanceLevel);
            foreach(var target in DesignLevelCatalog.Levels) {
                nav.ReturnToMainMenu();yield return NavigationReady(nav);
                nav.EnterDesignLevel(target);yield return NavigationReady(nav);
                var next=nav.DownhillWorkshop.Downhill;
                Assert.AreEqual(DesignLevelCatalog.Number(target),next.Presentation.Interface.CourseNumber);
                Assert.AreEqual(5,next.Presentation.Interface.CourseCount);
                Assert.AreEqual(DesignLevelCatalog.UsesFactory(target),(bool)next.FactoryLevel);
                Assert.IsTrue(next.IsBuilding);Assert.IsNotEmpty(nav.DownhillWorkshop.World.Connections);
            }
            nav.ReturnToMainMenu();yield return NavigationReady(nav);
            Assert.IsTrue(nav.TrySetDesignMode(false));yield return NavigationReady(nav);
            nav.EnterSandbox();yield return NavigationReady(nav);
            Assert.AreEqual("Beautiful Corner",nav.DownhillWorkshop.Downhill.FactoryLevel.Title);
            Assert.AreEqual(SandboxCourseId.BeautifulCorner,nav.DownhillWorkshop.Downhill.CurrentCourse);
        }
        [UnityTest] public IEnumerator BeautifulCornerDefaultLoadsCopyAndPreservesArtDuringTestRetryReturn()
        {
            SandboxCampaignSession.Reset();
            var nav=GardenNavigation.Current;yield return Enable(nav);
            Assert.AreEqual(DesignLevelId.BeautifulCorner,DesignModeSession.SelectedLevel);
            nav.EnterSandbox();yield return NavigationReady(nav);
            var owner=nav.DownhillWorkshop;var level=owner.Downhill;var factory=level.FactoryLevel;
            Assert.NotNull(factory);Assert.AreEqual("Beautiful Corner",factory.Title);
            Assert.AreEqual(1,level.Presentation.Interface.CourseNumber);Assert.AreEqual(5,level.Presentation.Interface.CourseCount);
            Assert.IsFalse(level.Settings.Campaign);Assert.IsEmpty(factory.Puzzles);
            var art=factory.Scenery.Single();Assert.NotNull(art);
            var renderers=art.GetComponentsInChildren<Renderer>(true);Assert.Greater(renderers.Length,20);
            var artIds=renderers.Select(r=>r.GetInstanceID()).ToArray();
            var road=factory.Tracks.Single().Generated.GetComponentInChildren<MeshCollider>();
            Assert.NotNull(road.sharedMesh);Assert.IsFalse(road.GetComponent<Renderer>().enabled);
            Assert.IsTrue(factory.Tracks[0].ShowGeneratedVisuals);
            Assert.IsTrue(factory.Tracks[0].Generated.GetComponentsInChildren<Renderer>().Any(r=>r.enabled));
            Assert.AreSame(level.Settings.Level1.Tracks[0].Generated.GetComponentInChildren<MeshCollider>().sharedMesh,road.sharedMesh);
            Assert.IsTrue(owner.TryLoadDesignPreset());Assert.IsNotEmpty(owner.World.Connections);
            level.Settings.CountdownStep=.01f;level.Settings.GoDuration=.01f;level.Settings.TransitionDuration=0;
            level.LaunchDecomposeSeconds=level.LaunchTravelSeconds=level.LaunchReconstructSeconds=.01f;
            Assert.IsTrue(level.TryStart());
            float deadline=Time.realtimeSinceStartup+5;
            while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.AreEqual(DownhillState.Running,level.State);Assert.IsTrue(art.gameObject.activeInHierarchy);
            Assert.IsTrue(road.gameObject.activeInHierarchy);
            Assert.IsTrue(level.TryRetry());yield return null;
            Assert.IsTrue(level.TryReturn());yield return null;Assert.IsTrue(level.IsBuilding);
            CollectionAssert.AreEqual(artIds,art.GetComponentsInChildren<Renderer>(true).Select(r=>r.GetInstanceID()));
            Assert.IsFalse(SandboxCampaignSession.FirstComplete);Assert.IsFalse(SandboxCampaignSession.AllComplete);
        }
        [UnityTest] public IEnumerator UnsupportedModesAreNotEnteredFromDesignSession()
        {
            var nav=GardenNavigation.Current; yield return Enable(nav);
            nav.EnterMode(GameMode.NarrowGap); yield return null;
            Assert.IsTrue(nav.InMainMenu); Assert.IsFalse(nav.IsTransitioning);
            Assert.IsFalse(SceneManager.GetSceneByName("NarrowGap").isLoaded);
        }
    }
}
