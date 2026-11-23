using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    public sealed class CampaignSpawnPhysicsTests
    {
        [UnityTest] public IEnumerator NewlyUnlockedLevelTwoBlocksStayAtSpawn()
        {
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("DownhillSandbox");
            yield return null; yield return null;
            var workshop=Object.FindFirstObjectByType<WorkshopBootstrap>();
            var level=workshop.Downhill;
            var load=typeof(DownhillController).GetMethod("LoadFactoryCourse",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.IsTrue((bool)load.Invoke(level,new object[]{SandboxCourseId.GravityGarage,true}));
            var blocks=workshop.World.Parts.Values.Where(p=>p.Kind==PartKind.DownhillLongBlock).ToArray();
            Assert.IsNotEmpty(blocks);
            var bodies=blocks.SelectMany(p=>p.Bodies).ToArray();
            var poses=bodies.Select(b=>b.Rigidbody.position).ToArray();
            foreach(var body in bodies) {
                Assert.IsTrue(body.Rigidbody.isKinematic,"New palette block must float");
                Assert.IsFalse(body.Rigidbody.useGravity);
            }
            for(int i=0;i<30;i++) yield return new WaitForFixedUpdate();
            for(int i=0;i<bodies.Length;i++) Assert.Less(Vector3.Distance(poses[i],bodies[i].Rigidbody.position),.001f);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            SandboxCampaignSession.Reset(); DesignModeSession.SetEnabled(false);
            SceneManager.LoadScene("MainMenu"); yield return null;
        }
    }
}
