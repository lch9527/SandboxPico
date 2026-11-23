using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    // Plays the real campaign loop: build, drive to the finish, victory return, press NEXT LEVEL.
    // The construction must arrive on the next workbench exactly as built and stay usable.
    public sealed class SandboxCarryOverTests
    {
        DownhillController level;
        [UnitySetUp] public IEnumerator Setup()
        {
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("DownhillSandbox"); yield return null;
            level=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
            level.Settings.CountdownStep=.01f; level.Settings.GoDuration=.01f; level.Settings.TransitionDuration=0;
            level.LaunchDecomposeSeconds=level.LaunchTravelSeconds=level.LaunchReconstructSeconds=.01f;
            level.VictoryPauseSeconds=0;
            level.VictoryDecomposeSeconds=level.VictoryTravelSeconds=level.VictoryReconstructSeconds=.01f;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(level) level.RestoreBuildImmediately();
            yield return null;
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("MainMenu"); yield return null;
        }
        IEnumerator WinCurrentCourse()
        {
            Assert.IsTrue(level.TryStart(),"Start "+level.CurrentCourse);
            float deadline=Time.realtimeSinceStartup+5;
            while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.AreEqual(DownhillState.Running,level.State);
            var finish=level.FactoryLevel.Finish;
            var offset=finish.transform.TransformPoint(finish.center)-level.SimulationCore.MainBody.Rigidbody.position;
            foreach(var body in level.SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies)) { body.Rigidbody.isKinematic=true; body.Rigidbody.position+=offset; }
            typeof(DownhillController).GetMethod("UpdateFactoryRun",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(level,null);
            deadline=Time.realtimeSinceStartup+10;
            while((!level.IsBuilding || level.VictoryInProgress) && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.IsTrue(level.IsBuilding); Assert.IsTrue(SandboxCampaignSession.IsComplete(level.CurrentCourse));
        }
        [UnityTest] public IEnumerator RetryReplaysTheArrivalEffect()
        {
            Assert.IsTrue(level.TryLoadDesignerExample());
            Assert.IsTrue(level.TryStart());
            float deadline=Time.realtimeSinceStartup+5;
            while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.AreEqual(DownhillState.Running,level.State);
            int presented=level.Vfx.PresentedCount;
            Assert.IsTrue(level.TryRetry());
            Assert.Greater(level.Vfx.PresentedCount,presented,"Retry shows the arrival burst at the start");
            Assert.Greater(level.Vfx.ActiveRings,0);
        }
        Vector3[] LocalPositions(AssemblyPart[] parts) => parts.SelectMany(p=>p.Bodies)
            .Select(b=>level.Course.WorkbenchFrame.InverseTransformPoint(b.Rigidbody.position)).ToArray();
        Quaternion[] LocalRotations(AssemblyPart[] parts) => parts.SelectMany(p=>p.Bodies)
            .Select(b=>Quaternion.Inverse(level.Course.WorkbenchFrame.rotation)*b.Rigidbody.rotation).ToArray();

        [UnityTest] public IEnumerator BuiltCarCarriesThroughEveryNextLevelPress() => CarryThroughLevels(false);
        // On headset the level is placed around the player and re-placed after every level change.
        [UnityTest] public IEnumerator BuiltCarCarriesWhenLevelIsPlacedAroundPlayer() => CarryThroughLevels(true);
        IEnumerator CarryThroughLevels(bool placedAroundPlayer)
        {
            if(placedAroundPlayer) {
                var workshop=level.Workshop;
                Assert.IsTrue(workshop.AlignBuildingArea(new Pose(new Vector3(1.3f,1.55f,-.8f),Quaternion.Euler(0,37,0)),true));
                yield return null;
            }
            Assert.AreEqual(SandboxCourseId.BeautifulCorner,level.CurrentCourse);
            Assert.IsTrue(level.TryLoadDesignerExample(),"Finish the Level 1 car");
            for(int number=2;number<=5;number++) {
                var built=level.Kit.Where(level.IsCoreConnected).ToArray();
                Assert.Greater(built.Length,1);
                var ids=built.Select(p=>p.InstanceId).ToArray();
                var edges=level.Workshop.World.Connections.Select(e=>e.id).OrderBy(e=>e).ToArray();
                yield return WinCurrentCourse();
                var positions=LocalPositions(built); var rotations=LocalRotations(built);
                var next=level.Presentation.Interface.NextLevelButton;
                Assert.IsTrue(next.gameObject.activeInHierarchy && next.Interactable,"Next Level offered after course "+(number-1));
                if(!level.HasReward) Assert.IsTrue(next.GetComponent<FloatingStartButton>().RewardPulse,"Next Level highlights when course "+(number-1)+" unlocks nothing");
                next.Press(); yield return null; yield return new WaitForFixedUpdate(); yield return null;
                Assert.AreEqual(number,SandboxCourseCatalog.Number(level.CurrentCourse));
                Assert.IsTrue(level.IsBuilding);
                var carried=ids.Select(id=>level.Kit.SingleOrDefault(p=>p.InstanceId==id)).ToArray();
                Assert.IsTrue(carried.All(p=>p),"Every built part survives into Level "+number);
                CollectionAssert.AreEquivalent(edges,level.Workshop.World.Connections.Select(e=>e.id),"Connections survive into Level "+number);
                foreach(var part in carried) {
                    Assert.IsTrue(part.gameObject.activeInHierarchy,part.Kind+" visible in Level "+number);
                    Assert.IsTrue(level.Workshop.World.Parts.ContainsKey(part.InstanceId),part.Kind+" interactive in Level "+number);
                    Assert.IsTrue(level.IsCoreConnected(part),part.Kind+" still part of the car in Level "+number);
                }
                var after=LocalPositions(carried); var turned=LocalRotations(carried);
                for(int i=0;i<after.Length;i++) {
                    Assert.Less(Vector3.Distance(positions[i],after[i]),.01f,$"Body {i} keeps its workbench pose in Level {number}: {positions[i]:F3} -> {after[i]:F3}");
                    Assert.Less(Quaternion.Angle(rotations[i],turned[i]),.5f);
                }
            }
        }
    }
}
