using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    // Design Mode Shift + left click: AssemblyWorld.TryBreakConnections.
    public sealed class SandboxDesignBreakTests
    {
        DownhillController level;
        [UnitySetUp] public IEnumerator Setup()
        {
            SandboxCampaignSession.Reset();
            DesignModeSession.SetEnabled(true); DesignModeSession.SelectLevel(DesignLevelId.BeautifulCorner);
            SceneManager.LoadScene("DownhillSandbox"); yield return null; yield return null;
            level=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
            Assert.IsTrue(level.TryLoadDesignPreset());
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(level) level.RestoreBuildImmediately();
            yield return null;
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("MainMenu"); yield return null;
        }
        [UnityTest] public IEnumerator BreaksEveryConnectionOfTheClickedPartOnly()
        {
            var world=level.Workshop.World;
            // A part joined on both sides, so breaking it splits the car.
            var middle=level.Kit.Where(p=>p!=level.Core).OrderByDescending(p=>world.Connections.Count(c=>c.a.partId==p.InstanceId || c.b.partId==p.InstanceId)).First();
            int own=world.Connections.Count(c=>c.a.partId==middle.InstanceId || c.b.partId==middle.InstanceId);
            Assert.Greater(own,1);
            int total=world.Connections.Count;
            var pose=middle.MainBody.Rigidbody.position;
            int presented=level.Vfx.PresentedCount;
            Assert.IsTrue(world.TryBreakConnections(middle));
            Assert.AreEqual(total-own,world.Connections.Count,"Only the clicked part's connections break");
            Assert.IsFalse(world.Connections.Any(c=>c.a.partId==middle.InstanceId || c.b.partId==middle.InstanceId));
            Assert.IsFalse(level.IsCoreConnected(middle));
            Assert.Less(Vector3.Distance(pose,middle.MainBody.Rigidbody.position),.0001f,"The part stays where it was");
            Assert.Greater(level.Vfx.PresentedCount,presented,"Detach feedback plays");
            Assert.IsFalse(world.TryBreakConnections(middle),"Nothing left to break");
            yield return null;
        }
        [UnityTest] public IEnumerator CoreAndTestRunsAreProtected()
        {
            var world=level.Workshop.World;
            int total=world.Connections.Count;
            Assert.IsFalse(world.TryBreakConnections(level.Core));
            Assert.IsTrue(level.TryStart()); yield return null;
            var part=level.Kit.First(p=>p!=level.Core && level.IsCoreConnected(p));
            Assert.IsFalse(world.TryBreakConnections(part),"No edits while testing");
            Assert.AreEqual(total,world.Connections.Count);
        }
    }
}
