using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace PhysicalAssembly.Tests
{
    public sealed partial class DownhillTests
    {
        [TestCase(0)] [TestCase(1)]
        public void TinyHandleStartsWheelRotationForEitherHand(int hand)
        {
            var wheel=level.RotationWheel; wheel.ApplyEmblem();
            var art=wheel.transform.Find(CarPartArtLibrary.HandwheelName);
            Assert.IsNotNull(art);
            var mesh=CarPartArtLibrary.Load().Handwheel[0];
            var tip=mesh.vertices.OrderByDescending(v=>new Vector2(v.x,v.z).sqrMagnitude).First();
            var point=art.TransformPoint(tip);
            Assert.Greater(new Vector2(tip.x,tip.z).magnitude,DownhillRotationWheel.Radius+.035f,"Use a handle tip outside the old grab ring");
            Assert.IsTrue(wheel.IsNearRim(point));
            Assert.IsFalse(level.SampleWheel(hand,true,.06f,false,0,new Pose(point,Quaternion.identity),true,0));
            Assert.IsTrue(level.SampleWheel(hand,true,GrabLatch.CloseDistance*.5f,true,0,new Pose(point,Quaternion.identity),true,.01f));
            Assert.IsTrue(level.IsRotatingWithHand(hand));
            level.EndRotation(hand);
        }
        [TestCase(.5f)] [TestCase(1f)] [TestCase(2f)]
        public void HandleGrabFollowsWheelPoseAndKeepsCenterEmpty(float scale)
        {
            var wheel=level.RotationWheel; wheel.ApplyEmblem();
            wheel.transform.SetPositionAndRotation(new Vector3(2,3,4),Quaternion.Euler(15,61,8));
            wheel.transform.localScale=Vector3.one*scale;
            var art=wheel.transform.Find(CarPartArtLibrary.HandwheelName);
            var mesh=CarPartArtLibrary.Load().Handwheel[0];
            var tips=mesh.vertices.Where(v=>new Vector2(v.x,v.z).magnitude>.21f).ToArray();
            Assert.IsNotEmpty(tips);
            foreach(var tip in tips) Assert.IsTrue(wheel.IsNearRim(art.TransformPoint(tip)),"Visible outer handle must be grabbable");
            Assert.IsTrue(wheel.IsNearRim(wheel.transform.TransformPoint(Vector3.forward*DownhillRotationWheel.Radius)));
            Assert.IsFalse(wheel.IsNearRim(wheel.transform.position),"The wheel hole is not a grab target");
            Assert.IsFalse(wheel.IsNearRim(art.TransformPoint(new Vector3(.3f,0,0))),"Distant space is not a grab target");
            Assert.IsFalse(wheel.IsNearRim(art.TransformPoint(new Vector3(.22f,.1f,0))),"Space above the handle is excluded");
            art.gameObject.SetActive(false);
            Assert.IsFalse(wheel.IsNearRim(art.TransformPoint(tips[0])),"Hidden handles must not add a grab target");
        }
    }
}
