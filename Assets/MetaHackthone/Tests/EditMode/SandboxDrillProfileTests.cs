using NUnit.Framework;
using UnityEngine;
namespace PhysicalAssembly.Tests {
 public sealed class SandboxDrillProfileTests {
  [Test] public void LowerProfileHasOpenGapAndResettableSandstone() {
   var go=new GameObject("Drill recovery course test");
   try { var c=go.AddComponent<GravityGarage25DCourse>();c.Build(GravityGarage25DParameters.UnityCalibrated(),.1f,true);
    Assert.AreEqual(2.3f,c.LowerSurface(1.6f),.0001f);Assert.IsTrue(float.IsNaN(c.LowerSurface(13)));
    Assert.AreEqual(.56f,c.LowerSurface(17.35f),.0001f);Assert.IsFalse(float.IsNaN(c.LowerSurface(GravityGarage25DCourse.LowerGapEnd)));
    Assert.AreNotSame(c.LowerRoad,c.LowerLanding);Assert.IsNotNull(c.Sandstone);c.Sandstone.SetActive(false);c.ResetSandstone();Assert.IsTrue(c.Sandstone.activeSelf);
   } finally {Object.DestroyImmediate(go);}
  }
  [TestCase(1f)] [TestCase(.4f)] [TestCase(.12f)] public void LargeConeKeepsRearSocketAndOneByTwoCollision(float scale) {
   var root=new GameObject("Drill recovery part test");AssemblyPart part=null;
   try {var world=root.AddComponent<AssemblyWorld>();part=DownhillParts.Create(world,PartKind.DownhillDrill,new Pose(Vector3.zero,Quaternion.identity),scale,gridVersion:10);
    Assert.AreEqual(1,part.Sockets.Length);var socket=part.Sockets[0];var center=SandboxDrill.BaseCenter(BuildingGeometry.Unit,scale);
    Assert.Less(Vector3.Distance(center,socket.transform.localPosition),.00001f);Assert.Greater(Vector3.Dot(socket.Normal,Vector3.back),.999f);
    // The look may be the factory cone or the art kit's housing and bit; together they must span base to tip.
    var look=new Bounds();bool any=false;foreach(var renderer in part.GetComponentsInChildren<MeshRenderer>()){if(any)look.Encapsulate(renderer.bounds);else look=renderer.bounds;any=true;}
    Assert.IsTrue(any);var collider=part.GetComponentInChildren<BoxCollider>();
    Assert.IsNotNull(collider);Assert.IsNull(part.GetComponentInChildren<MeshCollider>());
    Assert.Less(Vector3.Distance(new Vector3(1,1,SandboxDrill.CollisionLength)*BuildingGeometry.Unit*scale,collider.size),.000001f);
    // The box covers the whole bit, so it cannot clip into neighbouring parts or scenery.
    Assert.GreaterOrEqual(collider.center.z+collider.size.z*.5f,SandboxDrill.Tip(BuildingGeometry.Unit,scale).z-.000001f);
    Assert.Less(Vector3.Distance(center,collider.center-Vector3.forward*collider.size.z*.5f),.000001f);
    Assert.AreEqual(center.z,look.min.z,.00001f);Assert.AreEqual(SandboxDrill.Tip(BuildingGeometry.Unit,scale).z,look.max.z,.00001f);
    Assert.AreEqual(1,part.GetComponentsInChildren<Collider>().Length);
   } finally {if(part)Object.DestroyImmediate(part.gameObject);Object.DestroyImmediate(root);}
  }
 }
}
