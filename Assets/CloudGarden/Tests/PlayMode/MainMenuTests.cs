using System.Collections;
using System.Linq;
using CloudGarden.Integration;
using NUnit.Framework;
using Oculus.Interaction;
using PhysicalAssembly;
using PhysicalAssembly.MetaXR;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CloudGarden.Tests
{
    public sealed class MainMenuTests
    {
        [UnitySetUp] public IEnumerator Setup()
        {
            PlayAreaPose.Reset();SessionViewPose.Reset();
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null; yield return null; yield return new WaitForSeconds(.3f);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            PlayAreaPose.Reset();SessionViewPose.Reset();
            yield return SceneManager.LoadSceneAsync("MainMenu"); yield return null;
        }
        static IEnumerator Wait(GardenNavigation nav)
        {
            float deadline=Time.realtimeSinceStartup+25;
            while(nav.IsTransitioning && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.IsFalse(nav.IsTransitioning,"Transition timed out"); yield return null; yield return null;
        }
        static void OnePlayer()
        {
            Assert.AreEqual(1,Object.FindObjectsByType<MetaPlayerRig>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length);
            Assert.AreEqual(1,Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l=>l.isActiveAndEnabled));
        }
        [UnityTest] public IEnumerator TurningLeftAndReloadingMenuDoesNotRotateSession()
        {
            var original=SessionViewPose.Pose;var nav=GardenNavigation.Current;
            var panel=new Pose(nav.Menu.Panel.position,nav.Menu.Panel.rotation);
            for(int i=0;i<3;i++) {
                nav.Player.ViewCamera.transform.Rotate(0,-50,0);nav.Menu.Show();
                Assert.Less(Vector3.Distance(panel.position,nav.Menu.Panel.position),.0001f);
                Assert.Less(Quaternion.Angle(panel.rotation,nav.Menu.Panel.rotation),.001f);
                yield return SceneManager.LoadSceneAsync("MainMenu");yield return null;yield return null;nav=GardenNavigation.Current;
                Assert.Less(Vector3.Distance(panel.position,nav.Menu.Panel.position),.0001f);
                Assert.Less(Quaternion.Angle(original.rotation,SessionViewPose.Pose.rotation),.001f);
            }
            DesignModeSession.SetEnabled(true);DesignModeSession.SelectLevel(DesignLevelCatalog.Levels[0]);
            yield return SceneManager.LoadSceneAsync("DownhillSandbox");yield return null;yield return null;
            var workshop=Object.FindFirstObjectByType<WorkshopBootstrap>();var head=new GameObject("Turned head");
            try{head.transform.SetPositionAndRotation(original.position,original.rotation*Quaternion.Euler(0,-80,0));workshop.PositionAroundPlayer(head.transform,original.position.y-1.6f);var pose=workshop.Downhill.ActiveLayout.PlayerStartPose(workshop.DownhillSettings.PlayerDistance);Assert.Less(Quaternion.Angle(original.rotation,pose.rotation),.01f);Assert.Less(Vector3.Distance(original.position,pose.position),.01f);}
            finally{Object.Destroy(head);DesignModeSession.SetEnabled(false);}
        }
        [UnityTest] public IEnumerator ColdStartupContainsOnlyWorkbench()
        {
            var nav=GardenNavigation.Current; Assert.IsTrue(nav.InMainMenu); Assert.IsTrue(nav.Menu.Visible);
            var buttons=nav.Menu.Panel.GetComponentsInChildren<WorkshopButton>();
            CollectionAssert.AreEquivalent(new[]{"WORKBENCH BUILD","Place / Move Play Area"},buttons.Select(b=>b.Command));
            Assert.IsNull(Object.FindFirstObjectByType<WorkshopBootstrap>());
            Assert.IsEmpty(Object.FindObjectsByType<AssemblyPart>(FindObjectsInactive.Include,FindObjectsSortMode.None));
            Assert.AreEqual(1,SceneManager.sceneCount); OnePlayer(); yield return null;
            Assert.AreEqual("Assets/MetaHackthone/Scenes/MainMenu.unity",SceneUtility.GetScenePathByBuildIndex(0));
            foreach(var mode in new[]{GameMode.Sandbox}) Assert.IsTrue(Application.CanStreamedLevelBeLoaded(GardenNavigation.SceneFor(mode)));
        }
        [UnityTest] public IEnumerator EveryModeRoundTripsAndRepeatedSelectDoesNotDuplicateScenes()
        {
            var nav=GardenNavigation.Current; var player=nav.Player;
            foreach(var mode in new[]{GameMode.Sandbox})
            {
                nav.EnterMode(mode); nav.EnterMode(mode); yield return Wait(nav);
                Assert.AreEqual(GardenNavigation.SceneFor(mode),SceneManager.GetActiveScene().name);
                Assert.IsFalse(nav.Menu.Visible); Assert.AreSame(player,nav.Player); OnePlayer();
                nav.ReturnToMainMenu(); nav.ReturnToMainMenu(); yield return Wait(nav);
                Assert.IsTrue(nav.InMainMenu); Assert.IsTrue(nav.Menu.Visible);
                Assert.IsNull(Object.FindFirstObjectByType<WorkshopBootstrap>()); OnePlayer();
            }
        }
        [UnityTest] public IEnumerator WorkbenchButtonResumesConstructionAndPreservesRig()
        {
            var nav=GardenNavigation.Current;
            var buttons=nav.Menu.Panel.GetComponentsInChildren<MetaMenuButton>();
            var bench=buttons.Single(b=>b.name=="WORKBENCH BUILD");
            Assert.AreEqual(0f,bench.transform.localPosition.x,.0001f);
            var rig=nav.Player.XRRig; var pose=new Pose(rig.position,rig.rotation);
            bench.Ray.PublishPointerEvent(new PointerEvent(500,PointerEventType.Select,default)); yield return Wait(nav);
            var level=nav.DownhillWorkshop.Downhill;
            Assert.IsTrue(level.Presentation.WorkbenchVisuals.gameObject.activeInHierarchy);
            DesignModeSession.SetEnabled(true); Assert.IsTrue(level.TryLoadDesignerExample()); DesignModeSession.SetEnabled(false);
            var ids=level.Kit.Select(p=>p.InstanceId).ToArray();
            var partIds=nav.DownhillWorkshop.World.Parts.Keys.OrderBy(id=>id).ToArray();
            var connectionIds=nav.DownhillWorkshop.World.Connections.Select(c=>c.id).OrderBy(id=>id).ToArray();
            Assert.IsNotEmpty(partIds); Assert.IsNotEmpty(connectionIds);
            nav.ReturnToMainMenu(); yield return Wait(nav); yield return new WaitForSecondsRealtime(.35f);
            bench.Poke.PublishPointerEvent(new PointerEvent(501,PointerEventType.Select,default)); yield return Wait(nav);
            Assert.IsTrue(level.Presentation.WorkbenchVisuals.gameObject.activeInHierarchy);
            CollectionAssert.AreEqual(partIds,nav.DownhillWorkshop.World.Parts.Keys.OrderBy(id=>id));
            CollectionAssert.AreEqual(connectionIds,nav.DownhillWorkshop.World.Connections.Select(c=>c.id).OrderBy(id=>id));
            CollectionAssert.AreEqual(ids,level.Kit.Select(p=>p.InstanceId));
            Assert.That(level.TransferScale,Is.EqualTo(level.TestScale/level.Core.GeometryScale).Within(.0001f));
            Assert.AreEqual(pose.position,rig.position); Assert.AreEqual(pose.rotation,rig.rotation); OnePlayer();
        }
        [UnityTest] public IEnumerator RayAndPokeSelectModesThroughActualMenuButtons()
        {
            var nav=GardenNavigation.Current;
            var button=nav.Menu.Panel.GetComponentsInChildren<MetaMenuButton>().Single(b=>b.name=="WORKBENCH BUILD");
            button.Ray.PublishPointerEvent(new PointerEvent(90,PointerEventType.Select,default)); yield return Wait(nav);
            Assert.IsTrue(nav.InDownhill);
            nav.ReturnToMainMenu(); yield return Wait(nav); Assert.IsTrue(nav.InMainMenu);
            yield return new WaitForSeconds(.35f);
            button.Poke.PublishPointerEvent(new PointerEvent(92,PointerEventType.Select,default)); yield return Wait(nav);
            Assert.IsTrue(nav.InDownhill);
        }
        [UnityTest] public IEnumerator PlacementRequiresReleaseAndCancelPreservesPreviousPose()
        {
            var nav=GardenNavigation.Current; var menu=nav.Menu;
            menu.TogglePlacement(); var point=new Vector3(1.2f,0,2);
            menu.Placement.Target.PublishPointerEvent(new PointerEvent(93,PointerEventType.Select,new Pose(point,Quaternion.identity)));
            Assert.IsTrue(menu.PlacementActive); Assert.IsFalse(PlayAreaPose.IsSet);
            nav.EnterMode(GameMode.Sandbox); Assert.IsFalse(nav.IsTransitioning);
            yield return null; yield return null;
            menu.Placement.Target.PublishPointerEvent(new PointerEvent(93,PointerEventType.Unselect,default));
            menu.Placement.Target.PublishPointerEvent(new PointerEvent(93,PointerEventType.Select,new Pose(point,Quaternion.identity)));
            Assert.IsFalse(menu.PlacementActive); Assert.IsTrue(PlayAreaPose.IsSet);
            Assert.That(Vector3.Distance(point,PlayAreaPose.Pose.position),Is.LessThan(.001f));
            var saved=PlayAreaPose.Pose; menu.TogglePlacement(); menu.TogglePlacement();
            Assert.AreEqual(saved.position,PlayAreaPose.Pose.position); Assert.IsNull(Object.FindFirstObjectByType<WorkshopBootstrap>());
        }
        [UnityTest] public IEnumerator MissingModeLeavesMenuUsable()
        {
            var nav=GardenNavigation.Current;
            nav.SendMessage("EnterScene","MissingModeForNavigationTest"); yield return null;
            Assert.IsFalse(nav.IsTransitioning); Assert.IsTrue(nav.InMainMenu); StringAssert.Contains("unavailable",nav.Status);
            nav.EnterMode(GameMode.Sandbox); yield return Wait(nav); Assert.IsTrue(nav.InDownhill);
        }
    }
}
