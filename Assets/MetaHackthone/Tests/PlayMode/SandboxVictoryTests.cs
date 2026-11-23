using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    public sealed partial class SandboxVictoryTests
    {
        GameObject root;
        WorkshopBootstrap workshop;
        DownhillController level;
        DownhillSettings settings;
        GravityGarageLevelAsset course;
        static void Resolve(DownhillController l) => typeof(DownhillController).GetMethod("Resolve",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(l,new object[]{true});
        [UnitySetUp] public IEnumerator Setup()
        {
            SceneManager.LoadScene("MainMenu"); yield return null;
            // Start on Wheel Size Challenge (Level 3), the course whose clear unlocks the drill.
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SandboxCampaignSession.Complete(SandboxCourseId.BeautifulCorner); SandboxCampaignSession.Advance();
            SandboxCampaignSession.Complete(SandboxCourseId.TeleportPipes); SandboxCampaignSession.Advance();
            Assert.AreEqual(SandboxCourseId.GravityGarage,SandboxCampaignSession.Current);
            var scene=SceneManager.GetSceneByName("Victory fixture"); if(!scene.IsValid()) scene=SceneManager.CreateScene("Victory fixture"); SceneManager.SetActiveScene(scene);
            settings=ScriptableObject.CreateInstance<DownhillSettings>();
            settings.DesignerBuilder=settings.UseGravityGarage=settings.Campaign=settings.SandboxDrillCourse=true;
            settings.TransitionDuration=0; settings.CountdownStep=.01f; settings.GoDuration=.01f;
            // Advancing needs the published next course: Drill Drop Bridge follows Wheel Size Challenge.
            settings.DrillBridgeCourse=UnityEditor.AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>("Assets/MetaHackthone/Generated/Campaign/Level4.prefab");
            Assert.IsNotNull(settings.DrillBridgeCourse,"Published Drill Drop Bridge course is missing");
            course=ScriptableObject.CreateInstance<GravityGarageLevelAsset>(); course.parameters=GravityGarage25DParameters.UnityCalibrated(); settings.Level25D=course;
            root=new GameObject("Victory test"); root.SetActive(false);
            workshop=root.AddComponent<WorkshopBootstrap>(); workshop.DownhillSettings=settings; root.SetActive(true);
            level=workshop.Downhill; level.Course.SandboxPolish=true;
            // Existing lifecycle tests use a short transfer; dedicated timing tests cover defaults.
            level.LaunchDecomposeSeconds=level.LaunchTravelSeconds=level.LaunchReconstructSeconds=.01f;
            level.VictoryPauseSeconds=0;
            yield return null;
        }
        [TearDown] public void Cleanup()
        {
            DesignModeSession.SetEnabled(false);
            if(level) level.RestoreBuildImmediately();
            if(root) Object.DestroyImmediate(root);
            foreach(var part in Object.FindObjectsByType<AssemblyPart>(FindObjectsInactive.Include,FindObjectsSortMode.None)) Object.DestroyImmediate(part.gameObject);
            foreach(var env in Object.FindObjectsByType<WorkshopEnvironment>(FindObjectsSortMode.None)) Object.DestroyImmediate(env.gameObject);
            Object.DestroyImmediate(settings); Object.DestroyImmediate(course); SandboxCampaignSession.Reset();
        }
        [UnityTest] public IEnumerator LaunchTransfersInOneSecondWithoutCountdownAndCancelsSafely()
        {
            // Shipping defaults: 0.15 + 0.5 + 0.2 s transfer, then a 0.15 s rest. No 3-2-1.
            level.LaunchDecomposeSeconds=.15f;level.LaunchTravelSeconds=.5f;level.LaunchReconstructSeconds=.2f;settings.CountdownStep=1;settings.LaunchPause=.15f;
            var edges=workshop.World.Connections.Count;
            Assert.IsTrue(level.TryStart());Assert.AreEqual(DownhillState.Transition,level.State);
            Assert.Greater(root.GetComponentsInChildren<SandboxReturnEffect>().Length,0);
            Assert.IsTrue(level.SimulationCore.MainBody.Rigidbody.isKinematic);
            yield return new WaitForSecondsRealtime(.4f);Assert.AreEqual(DownhillState.Transition,level.State);
            level.RestoreBuildImmediately();Assert.IsTrue(level.IsBuilding);Assert.AreEqual(edges,workshop.World.Connections.Count);
            Assert.AreEqual(0,SandboxFragments.ActiveCount);
            Assert.IsTrue(level.TryStart());
            float started=Time.realtimeSinceStartup;
            while(level.State!=DownhillState.Running && Time.realtimeSinceStartup-started<2) {
                Assert.AreNotEqual("3",level.Presentation.Countdown?level.Presentation.Countdown.text:"","No 3-2-1 countdown in the campaign");
                yield return null;
            }
            Assert.AreEqual(DownhillState.Running,level.State);
            Assert.That(Time.realtimeSinceStartup-started,Is.InRange(.9f,1.25f),"Start to running takes about one second");
            Assert.IsFalse(level.SimulationCore.MainBody.Rigidbody.isKinematic);
            Assert.AreEqual(level.Vfx.ActiveParticles,SandboxFragments.ActiveCount,"Only the short arrival sparkle may outlive the transfer");
        }
        [UnityTest] public IEnumerator VictoryWaitsBeforeDecomposingFrozenCar()
        {
            level.VictoryPauseSeconds=1.5f;
            Assert.IsTrue(level.TryStart());yield return new WaitForSecondsRealtime(.12f);Resolve(level);
            var body=level.SimulationCore.MainBody;var position=body.Rigidbody.position;
            Assert.IsTrue(body.Rigidbody.isKinematic);Assert.IsTrue(level.SimulationCore.VisualRenderers.Any(r=>r.enabled));
            yield return new WaitForSecondsRealtime(1);
            Assert.AreEqual(0,root.GetComponentsInChildren<SandboxReturnEffect>().Length);Assert.Less(Vector3.Distance(position,body.Rigidbody.position),.001f);
            yield return new WaitForSecondsRealtime(.6f);Assert.Greater(root.GetComponentsInChildren<SandboxReturnEffect>().Length,0);
            yield return new WaitForSecondsRealtime(1.6f);Assert.IsTrue(level.IsBuilding);
        }
        [UnityTest] public IEnumerator WheelAppliesFiveDegreeStepsWithOneTickEach()
        {
            var center=level.RotationWheel.transform.position;var radius=level.RotationWheel.transform.right*DownhillRotationWheel.Radius;var up=level.Course.WorkbenchFrame.up;
            var start=level.Core.MainBody.Rigidbody.rotation;
            Assert.IsTrue(level.BeginRotation(0,center+radius));int count=level.WheelStepsPlayed;
            level.Rotate(0,center+Quaternion.AngleAxis(4.9f,up)*radius);
            for(int i=0;i<8;i++)yield return new WaitForFixedUpdate();Assert.AreEqual(count,level.WheelStepsPlayed);
            level.Rotate(0,center+Quaternion.AngleAxis(5.1f,up)*radius);
            for(int i=0;i<8;i++)yield return new WaitForFixedUpdate();Assert.AreEqual(count+1,level.WheelStepsPlayed);Assert.That(Quaternion.Angle(start,level.Core.MainBody.Rigidbody.rotation),Is.EqualTo(5).Within(.1));
            level.Rotate(0,center+Quaternion.AngleAxis(4.9f,up)*radius);
            for(int i=0;i<8;i++)yield return new WaitForFixedUpdate();Assert.AreEqual(count+1,level.WheelStepsPlayed);
            level.Rotate(0,center+Quaternion.AngleAxis(-.1f,up)*radius);
            for(int i=0;i<8;i++)yield return new WaitForFixedUpdate();Assert.AreEqual(count+2,level.WheelStepsPlayed);
            level.CancelRotation();Assert.Less(Quaternion.Angle(start,level.Core.MainBody.Rigidbody.rotation),.1f);
            Assert.IsNotNull(Resources.Load<AudioClip>("SandboxWheelTick"));
        }
        // Campaign wins keep the winning car assembled: every part comes back to where it was built.
        [UnityTest] public IEnumerator WinningCarReturnsAssembledAndLeavesLoosePartsUntouched()
        {
            DesignModeSession.SetEnabled(true); Assert.IsTrue(level.TryLoadDesignerExample()); DesignModeSession.SetEnabled(false);
            Assert.IsTrue(workshop.World.TryDetachPart(level.Kit[8]));
            var loose=level.Kit.First(p=>p.gameObject.activeSelf && !level.IsCoreConnected(p));
            var loosePosition=loose.MainBody.Rigidbody.position+Vector3.up*.015f;
            loose.MainBody.Rigidbody.position=loosePosition; loose.MainBody.transform.position=loosePosition;
            var built=level.Kit.ToDictionary(p=>p,p=>p.Bodies.Select(b=>new Pose(b.transform.position,b.transform.rotation)).ToArray());
            var edges=workshop.World.Connections.Select(e=>e.id).ToArray();
            Assert.That(level.VictoryDecomposeSeconds+level.VictoryTravelSeconds+level.VictoryReconstructSeconds,Is.EqualTo(1.5f).Within(.001));
            Assert.IsTrue(level.TryStart()); yield return new WaitForSecondsRealtime(.08f);
            var mapping=level.SimulationOriginals.ToArray(); Assert.Greater(mapping.Length,1);
            Assert.IsTrue(mapping.All(p=>p.Key.GeometryScale!=p.Value.GeometryScale));
            Resolve(level); Resolve(level);
            Assert.IsTrue(level.VictoryInProgress); Assert.AreEqual(mapping.Length,root.GetComponentsInChildren<SandboxReturnEffect>().Length);
            Assert.IsFalse(level.TryRetry()); Assert.IsFalse(level.TryStart()); Assert.IsFalse(level.TryReturn()); Assert.IsFalse(level.TryResetBuild()); Assert.IsFalse(level.TryAdvanceLevel());
            Assert.IsTrue(level.Presentation.ExitButton.Interactable);
            Assert.IsTrue(level.SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies).All(b=>b.Rigidbody.isKinematic));
            Assert.LessOrEqual(SandboxFragments.ActiveCount,SandboxFragments.Budget);
            yield return new WaitForSecondsRealtime(1.65f);
            Assert.IsTrue(level.IsBuilding); Assert.IsFalse(level.VictoryInProgress); Assert.IsNull(level.SimulationWorld);
            foreach(var pair in mapping) for(int i=0;i<pair.Value.Bodies.Length;i++) {
                var b=pair.Value.Bodies[i]; var expected=built[pair.Value][i];
                Assert.Less(Vector3.Distance(expected.position,b.transform.position),.001f,pair.Value.name);
                Assert.Less(Quaternion.Angle(expected.rotation,b.transform.rotation),.1f);
                Assert.IsTrue(b.Colliders.All(c=>c.enabled)); Assert.IsTrue(b.Rigidbody.isKinematic);
            }
            CollectionAssert.AreEquivalent(edges,workshop.World.Connections.Select(e=>e.id),"The winning car stays connected");
            Assert.Less(Vector3.Distance(loosePosition,loose.MainBody.transform.position),.001f);
            Assert.IsTrue(level.CanAdvanceLevel); Assert.IsTrue(workshop.Session.Status.Contains("COMPLETE"));
            Assert.IsFalse(level.Kit.Single(p=>p.Kind==PartKind.DownhillDrill).gameObject.activeSelf);
            Assert.IsNotNull(level.Reward.Preview);
        }
        [UnityTest] public IEnumerator RewardAutomaticallyTravelsAndClaimsOnlyOnSuccessfulAdvance()
        {
            SandboxCampaignSession.Complete(SandboxCourseId.GravityGarage); yield return null;
            var reward=level.Reward; Assert.IsNotNull(reward); var preview=reward.Preview;
            Assert.IsEmpty(preview.GetComponentsInChildren<Collider>()); Assert.IsEmpty(preview.GetComponentsInChildren<AssemblySocket>());
            Assert.IsEmpty(preview.GetComponentsInChildren<AssemblyPart>()); Assert.IsEmpty(preview.GetComponentsInChildren<Rigidbody>());
            reward.Tick(reward.RevealSeconds+.01f);
            Assert.AreEqual(SandboxRewardState.ToMenu,reward.State);
            Assert.IsFalse(SandboxCampaignSession.DrillClaimed);
            reward.Tick(.46f); Assert.AreEqual(SandboxRewardState.AtMenu,reward.State);
            Assert.That(preview.lossyScale.x,Is.EqualTo(1).Within(.001));
            // The unlocked part spins on its own beside the button; the preview row itself stays still.
            var part=preview.GetChild(0); var rotation=part.rotation; var row=preview.rotation; reward.Tick(1);
            Assert.That(Quaternion.Angle(rotation,part.rotation),Is.EqualTo(180).Within(.1));
            Assert.That(Quaternion.Angle(row,preview.rotation),Is.LessThan(.1));
            reward.Tick(1); Assert.That(Quaternion.Angle(rotation,part.rotation),Is.LessThan(.1));
            Assert.IsTrue(level.Presentation.Interface.NextLevelButton.GetComponent<FloatingStartButton>().RewardPulse);
            Assert.IsFalse(reward.HaloVisible);
            Assert.AreEqual(SandboxRewardState.AtMenu,reward.State);
            var brightness=preview.GetComponent<SandboxRewardPresentation>();
            Assert.That(brightness.Amount,Is.EqualTo(0).Within(.001f));
            var drill=level.Kit.Single(p=>p.Kind==PartKind.DownhillDrill);
            Assert.IsFalse(drill.gameObject.activeSelf); Assert.IsFalse(workshop.World.Parts.ContainsKey(drill.InstanceId));
            Assert.IsTrue(level.TryAdvanceLevel()); Assert.IsFalse(level.TryAdvanceLevel());
            Assert.AreEqual(SandboxRewardState.Claimed,reward.State); Assert.IsNull(reward.Preview);
            Assert.AreEqual(SandboxCourseId.DrillDropBridge,level.CurrentCourse);
            // Loading the next course may rebuild the kit, so look the drill up again.
            Assert.IsTrue(SandboxCampaignSession.DrillClaimed);
            Assert.IsTrue(level.Kit.Any(p=>p && p.Kind==PartKind.DownhillDrill && p.gameObject.activeSelf && workshop.World.Parts.ContainsKey(p.InstanceId)),"The claimed drill is available on the next course");
            Assert.IsFalse(level.Presentation.Interface.NextLevelButton.GetComponent<FloatingStartButton>().RewardPulse);
        }
        [UnityTest] public IEnumerator RewardRisesAtBuildSizeToTheLevelRevealPoint()
        {
            // The bare fixture course has no layout; published levels always do.
            var layout=level.Course.gameObject.AddComponent<SandboxLevelLayout>();
            layout.Course=level.Course; layout.BuildingRoot=level.Course.WorkbenchFrame; layout.SandboxRoot=level.Course.SandboxFrame;
            Assert.AreSame(layout,level.ActiveLayout);
            SandboxCampaignSession.Complete(SandboxCourseId.GravityGarage); yield return null;
            var reward=level.Reward; Assert.IsNotNull(reward); var preview=reward.Preview;
            Assert.AreEqual(SandboxRewardState.Revealing,reward.State);
            // Default: above the assembly spot, rising from below.
            Assert.Less(Vector3.Distance(layout.RewardRevealPose().position,reward.GoalPose.position),.001f);
            Assert.That(reward.GoalPose.position.y-level.Presentation.AssemblyAnchor.position.y,Is.EqualTo(layout.RewardRevealHeight).Within(.001));
            reward.Tick(.01f); float low=preview.position.y; Assert.Less(low,reward.GoalPose.position.y-reward.RiseDistance*.5f);
            reward.Tick(reward.RevealSeconds*.4f); Assert.Greater(preview.position.y,low);
            reward.Tick(reward.RevealSeconds); Assert.AreEqual(SandboxRewardState.ToMenu,reward.State);
            Assert.Less(Vector3.Distance(reward.GoalPose.position,preview.position),.001f);
            Assert.That(preview.lossyScale.x,Is.EqualTo(1).Within(.001));
            // A point placed in the level overrides the default.
            var point=new GameObject(SandboxLevelLayout.RewardRevealPointName).transform; point.SetParent(layout.BuildingRoot,false);
            point.position=reward.GoalPose.position+new Vector3(.3f,-.15f,.2f); layout.RewardRevealPoint=point;
            Assert.Less(Vector3.Distance(point.position,reward.GoalPose.position),.001f);
            // The reminder stream has its own points: the delivery comets keep theirs.
            Assert.LessOrEqual(SandboxFragments.ActiveCount,193+level.Vfx.ActiveParticles);
        }
        [UnityTest] public IEnumerator QuitDuringVictoryCleansEffectsAndFinalLevelHasNoReward()
        {
            Assert.IsTrue(level.TryStart()); yield return new WaitForSecondsRealtime(.08f);
            Resolve(level); Assert.IsTrue(level.VictoryInProgress); level.Exit(); yield return null;
            Assert.IsFalse(level.VictoryInProgress); Assert.IsTrue(level.IsBuilding); Assert.AreEqual(0,root.GetComponentsInChildren<SandboxReturnEffect>().Length);
            Assert.IsTrue(!level.Reward || !level.Reward.Preview,"Quit must not recreate a pending reward");
            Assert.IsTrue(level.TryAdvanceLevel());
            // The published Drill Drop Bridge has a finish crowd, so its win takes longer than the bare
            // return: the car rests while the crowd swarms it, then whoever is left lifts the core.
            var crowd=level.FinishCrowd; Assert.IsNotNull(crowd,"Published Drill Drop Bridge is missing its finish crowd");
            Assert.IsTrue(level.TryStart()); yield return new WaitForSecondsRealtime(.08f);
            int helpers=SandboxFinishCrowd.HelperCount(level.SimulationOriginals.Count-1,crowd.Total,crowd.AlwaysStay);
            Resolve(level);
            Assert.AreEqual(SandboxFinishCrowd.CrowdPhase.Rush,crowd.Phase,"The crowd rushes the winning car");
            float deadline=Time.realtimeSinceStartup+10;
            while(level.VictoryInProgress && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.AreEqual(crowd.Total-helpers,crowd.Remaining,"One helper leaves with each part of the winning car");
            Assert.IsTrue(level.IsBuilding); Assert.IsFalse(SandboxCampaignSession.AllComplete); Assert.IsFalse(level.CanAdvanceLevel);
            Assert.IsTrue(!level.Reward || !level.Reward.Preview);
        }
        [UnityTest] public IEnumerator ReminderResumesAfterCoordinatorRecreationWithoutDuplicates()
        {
            SandboxCampaignSession.Complete(SandboxCourseId.GravityGarage); yield return null;
            var old=level.Reward; old.Cancel(); Object.Destroy(old); yield return null; yield return null;
            Assert.IsNotNull(level.Reward.Preview); level.Reward.Reveal(); level.Reward.Reveal();
            Assert.AreEqual(1,root.GetComponents<SandboxRewardCoordinator>().Length);
            Assert.IsFalse(SandboxCampaignSession.DrillClaimed);
            level.Exit(); yield return null; Assert.IsNull(level.Reward.Preview);
        }
        [UnityTest] public IEnumerator RepeatedWinsReusePendingRewardAndReleaseFragmentBudget()
        {
            Transform preview=null;
            for(int i=0;i<2;i++) {
                Assert.IsTrue(level.TryStart()); yield return new WaitForSecondsRealtime(.08f); Resolve(level);
                yield return new WaitForSecondsRealtime(1.65f);
                Assert.IsTrue(level.IsBuilding); Assert.IsNotNull(level.Reward.Preview);
                if(preview) Assert.AreSame(preview,level.Reward.Preview); else preview=level.Reward.Preview;
                Assert.AreEqual(1,root.GetComponents<SandboxRewardCoordinator>().Length);
                // Halo 49, reveal/delivery 96, reminder stream 48.
                Assert.LessOrEqual(SandboxFragments.ActiveCount,193+level.Vfx.ActiveParticles);
                yield return new WaitForSecondsRealtime(level.Vfx.Settings.RewardSeconds+.05f);
                Assert.AreEqual(0,level.Vfx.ActiveParticles,"The reveal sparkle must release its allocation");
                Assert.LessOrEqual(SandboxFragments.ActiveCount,193);
            }
            level.Reward.Cancel(); Assert.AreEqual(0,SandboxFragments.ActiveCount);
        }
        [UnityTest] public IEnumerator SceneReloadRestoresUnclaimedReminderWithoutKeepingOldVisuals()
        {
            SandboxCampaignSession.Complete(SandboxCourseId.GravityGarage); yield return null;
            var oldPreview=level.Reward.Preview;
            var previous=root.scene;
            var fresh=SceneManager.CreateScene("Victory reload fixture"); SceneManager.SetActiveScene(fresh);
            yield return SceneManager.UnloadSceneAsync(previous);
            Assert.IsFalse(oldPreview); Assert.AreEqual(0,SandboxFragments.ActiveCount);
            settings.Level25D=course;
            root=new GameObject("Reloaded victory test"); root.SetActive(false);
            workshop=root.AddComponent<WorkshopBootstrap>(); workshop.DownhillSettings=settings; root.SetActive(true);
            level=workshop.Downhill; level.Course.SandboxPolish=true;
            yield return null; yield return null;
            Assert.IsTrue(SandboxCampaignSession.FirstComplete); Assert.IsFalse(SandboxCampaignSession.DrillClaimed);
            Assert.IsNotNull(level.Reward.Preview); Assert.IsFalse(level.Kit.Single(p=>p.Kind==PartKind.DownhillDrill).gameObject.activeSelf);
        }
    }
}
