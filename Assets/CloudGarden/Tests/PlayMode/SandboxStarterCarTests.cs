using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    // Level 1 starts with a half-built car: core, long block and two small wheels connected,
    // and two small wheels loose for the player to attach.
    public sealed class SandboxStarterCarTests
    {
        DownhillController level;
        [UnitySetUp] public IEnumerator Setup()
        {
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("DownhillSandbox"); yield return null; yield return null;
            level=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(level) level.RestoreBuildImmediately();
            yield return null;
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("MainMenu"); yield return null;
        }
        void AssertStarterCar(string when)
        {
            var world=level.Workshop.World;
            Assert.IsTrue(level.StarterCarBuilt,"Starter car built "+when);
            var car=world.GetConnectedParts(level.Core.InstanceId);
            Assert.AreEqual(1,car.Count(p=>p.Kind==PartKind.DownhillLongBlock),"Long block on the car "+when);
            Assert.AreEqual(2,car.Count(p=>p.Kind==PartKind.DownhillSmallWheel),"Two wheels on the car "+when);
            Assert.AreEqual(4,car.Count,"Core, long block and two wheels only "+when);
            var loose=level.Kit.Where(p=>p.gameObject.activeInHierarchy && p.Kind==PartKind.DownhillSmallWheel && !car.Contains(p)).ToArray();
            Assert.AreEqual(2,loose.Length,"Two loose wheels for the player "+when);
            foreach(var wheel in loose) Assert.AreEqual(1,world.GetConnectedParts(wheel.InstanceId).Count,"Loose wheel is not connected "+when);
        }
        [UnityTest] public IEnumerator LevelOneStartsWithTwoWheelsAttachedAndTwoLoose()
        {
            Assert.AreEqual(SandboxCourseId.BeautifulCorner,level.CurrentCourse);
            AssertStarterCar("at level start");
            // Reset gives the same half-built car back.
            Assert.IsTrue(level.TryResetBuild()); yield return null;
            AssertStarterCar("after reset");
            // The example car is the starter car with the two loose wheels added.
            Assert.IsTrue(level.TryLoadDesignerExample()); yield return null;
            var car=level.Workshop.World.GetConnectedParts(level.Core.InstanceId);
            Assert.AreEqual(4,car.Count(p=>p.Kind==PartKind.DownhillSmallWheel));
        }
    }
}
