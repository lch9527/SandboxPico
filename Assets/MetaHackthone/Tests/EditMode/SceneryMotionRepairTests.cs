using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using PhysicalAssembly.Editor;
namespace PhysicalAssembly.Tests {
 public sealed class SceneryMotionRepairTests {
  [TestCase(1),TestCase(2),TestCase(3),TestCase(4),TestCase(5)]
  public void GroundIsStaticAndOnlyBackgroundIslandsFloat(int level){
   var root=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab");int grounded=0,floating=0;
   foreach(var r in root.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Palette mesh")){
    bool horizon=AnimationUtility.CalculateTransformPath(r.transform,null).Contains("Horizon transition islands");
    Assert.AreEqual(horizon?.2f:0,r.sharedMaterial.GetFloat("_FloatHeight"),r.name);
    if(horizon)floating++;else grounded++;
   }
   Assert.AreEqual(2,floating);Assert.GreaterOrEqual(grounded,3);
   Assert.IsFalse(root.GetComponentsInChildren<CarSceneryBreeze>(true).Any(b=>b.name.StartsWith("Wheel ")||b.name=="Winch drum and crank"));
  }
  [TestCase(1),TestCase(2),TestCase(3),TestCase(4),TestCase(5)]
  public void FlagsAndRotorsClearSolidSceneryThroughoutTheirMotion(int level){
   var root=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab");
   var env=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Environment");
   foreach(var b in env.GetComponentsInChildren<CarSceneryBreeze>(false).Where(b=>b.name=="Breeze pennant"||b.name=="Windmill rotor"))
    Assert.IsEmpty(SceneryMotionRepair.Contacts(b,env),AnimationUtility.CalculateTransformPath(b.transform,env));
  }
 }
}
