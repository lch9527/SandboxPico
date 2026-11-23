using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    // Clearing Level 1 unlocks the long blocks only (big wheels come from Level 2): the long block is
    // revealed at the end point and then beside the menu's Next Level button.
    public sealed class SandboxUnlockRevealTests
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
            if(level) { if(level.Reward) level.Reward.Cancel(); level.RestoreBuildImmediately(); }
            yield return null;
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("MainMenu"); yield return null;
        }
        // Bounds in the preview's own frame: world AABBs grow when the preview turns and would overlap falsely.
        static Bounds GroupBounds(Transform preview,Transform group)
        {
            bool any=false; var bounds=new Bounds();
            foreach(var filter in group.GetComponentsInChildren<MeshFilter>()) {
                var m=filter.sharedMesh.bounds;
                for(int i=0;i<8;i++) {
                    var corner=preview.InverseTransformPoint(filter.transform.TransformPoint(m.center+Vector3.Scale(m.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
                    if(!any) { bounds=new Bounds(corner,Vector3.zero); any=true; } else bounds.Encapsulate(corner);
                }
            }
            return bounds;
        }
        static void AssertLongBlockOnly(Transform preview,string where)
        {
            Assert.IsNotNull(preview.Find("Reward part "+PartKind.DownhillLongBlock),"Long block shown "+where);
            Assert.IsNull(preview.Find("Reward part "+PartKind.DownhillLargeWheel),"Big wheel is not a Level 1 reward, "+where);
        }
        [UnityTest] public IEnumerator FirstLevelRevealsLongBlocksOnly()
        {
            Assert.AreEqual(SandboxCourseId.BeautifulCorner,level.CurrentCourse);
            CollectionAssert.AreEqual(new[]{PartKind.DownhillLongBlock},level.RewardKinds);
            SandboxCampaignSession.Complete(SandboxCourseId.BeautifulCorner);
            float deadline=Time.realtimeSinceStartup+3;
            while((!level.Reward || !level.Reward.Preview) && Time.realtimeSinceStartup<deadline) yield return null;
            var reward=level.Reward; Assert.IsNotNull(reward); Assert.IsNotNull(reward.Preview);
            var preview=reward.Preview;
            // Reveal at the end point: dimmed environment, the part above the goal.
            reward.Tick(reward.RevealSeconds+.01f);
            AssertLongBlockOnly(preview,"at the end point");
            Assert.AreEqual(SandboxRewardState.ToMenu,reward.State);
            Assert.Less(Vector3.Distance(preview.position,reward.GoalPose.position),.001f,"Reveal finishes at the authored reveal point");
            // Open the menu: the part travels beside the Next Level button.
            // Reward travel begins automatically after reveal.
            reward.Tick(reward.PreviewTravelSeconds+.01f);
            Assert.AreEqual(SandboxRewardState.AtMenu,reward.State);
            AssertLongBlockOnly(preview,"beside the menu");
            var button=level.Presentation.Interface.NextLevelButton.transform;
            float side=Vector3.Dot(preview.position-button.position,button.right);
            Assert.Greater(side,button.lossyScale.x*.5f,"The part sits to the side of the Next Level button, not on it");
            // The part spins in place: its centre stays in its slot while it turns, and the row stays square to the button.
            var parts=new[]{preview.Find("Reward part "+PartKind.DownhillLongBlock)};
            var centres=parts.Select(p=>{var b=GroupBounds(preview,p);return new Vector2(b.center.x,b.center.z);}).ToArray();
            var turns=parts.Select(p=>p.localRotation).ToArray();
            reward.Tick(reward.RotationSeconds*.25f);
            for(int i=0;i<parts.Length;i++) {
                var b=GroupBounds(preview,parts[i]);
                Assert.Less(Vector2.Distance(centres[i],new Vector2(b.center.x,b.center.z)),.002f,parts[i].name+" spins in place");
                Assert.Greater(Quaternion.Angle(turns[i],parts[i].localRotation),45f,parts[i].name+" turns");
            }
            Assert.Less(Quaternion.Angle(preview.rotation,button.rotation),.5f,"The row itself does not rotate");
            AssertLongBlockOnly(preview,"while spinning");
        }
    }
}
