using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PhysicalAssembly.Editor;

namespace PhysicalAssembly.Tests {
 public sealed class CarSceneryTests {
  [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
  public void SceneAndCampaignHaveMatchingDecorationsOutsideGameplayHierarchy(int n) {
   var prefab=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>($"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab");
   Assert.IsNotNull(prefab);Check(prefab,n);
   var setup=EditorSceneManager.GetSceneManagerSetup();bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
   try {
    var scene=EditorSceneManager.OpenScene($"Assets/MetaHackthone/Scenes/Level{n}.unity",OpenSceneMode.Additive);
    var authored=SandboxFactoryAuthoring.Find(scene);Check(authored,n);
    var a=Decoration(authored);var b=Decoration(prefab);
    Assert.AreEqual(b.GetComponentsInChildren<MeshFilter>(true).Length,a.GetComponentsInChildren<MeshFilter>(true).Length);
    Assert.AreEqual(b.GetComponentsInChildren<CarSceneryBreeze>(true).Length,a.GetComponentsInChildren<CarSceneryBreeze>(true).Length);
    var expected=b.GetComponentsInChildren<Transform>(true);var actual=a.GetComponentsInChildren<Transform>(true);Assert.AreEqual(expected.Length,actual.Length);
    for(int i=0;i<expected.Length;i++) {
     Assert.AreEqual(expected[i].name,actual[i].name);
     Assert.Less(Vector3.Distance(expected[i].localPosition,actual[i].localPosition),.0001f,actual[i].name);
     Assert.Less(Quaternion.Angle(expected[i].localRotation,actual[i].localRotation),.01f,actual[i].name);
     Assert.Less(Vector3.Distance(expected[i].localScale,actual[i].localScale),.0001f,actual[i].name);
    }
    EditorSceneManager.CloseScene(scene,true);
   }finally{SandboxTrackEditing.Suspended=suspended;if(setup.Any(s=>s.isLoaded))EditorSceneManager.RestoreSceneManagerSetup(setup);}
  }
  static Transform Decoration(SandboxFactoryLevel f)=>f.Layout.Course.SandboxFrame.Find("Environment/"+CarSceneryAuthoring.GroupName);
  static void Check(SandboxFactoryLevel f,int n) {
   var root=Decoration(f);Assert.IsNotNull(root);Assert.IsFalse(root.IsChildOf(f.TrackRoot));Assert.Contains(root,f.Scenery);
   Assert.IsEmpty(root.GetComponentsInChildren<Collider>(true));Assert.IsEmpty(root.GetComponentsInChildren<Rigidbody>(true));
   Assert.IsTrue(root.GetComponentsInChildren<Transform>(true).Any(t=>t.name==CarSceneryAuthoring.Landmarks[n-1]));
   foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true)) {
    Assert.IsNotNull(r.GetComponent<MeshFilter>().sharedMesh,r.name);
    foreach(var mat in r.sharedMaterials){Assert.IsNotNull(mat,r.name);Assert.IsTrue(mat.shader.isSupported,mat.name);}
   }
   Assert.Greater(root.GetComponentsInChildren<CarSceneryBreeze>(true).Length,0);
   var horizon=root.Find("Horizon transition islands");Assert.IsNotNull(horizon);
   var distant=horizon.GetComponentsInChildren<MeshRenderer>(true);Assert.AreEqual(2,distant.Length);
   Assert.Less(horizon.GetComponentsInChildren<MeshFilter>().Sum(m=>m.sharedMesh.triangles.Length/3),50000,"Distant scenery triangle budget");
   foreach(var r in distant){Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off,r.shadowCastingMode);Assert.Less(r.sharedMaterial.renderQueue,2500);}
  }
  [Test] public void CloudBodyAndShellKeepIdenticalMotionAndConservativeBounds() {
   foreach(int i in Enumerable.Range(0,10)) {
    var body=AssetDatabase.LoadAssetAtPath<Material>($"Assets/CreekDiorama/Materials/Cloud puff {i:00}.mat");
    var shell=AssetDatabase.LoadAssetAtPath<Material>($"Assets/CreekDiorama/Materials/Cloud shell {i:00}.mat");
    foreach(var property in new[]{"_Billow","_BillowScale","_RollSpeed","_Swell","_Drift","_Seed"})Assert.AreEqual(body.GetFloat(property),shell.GetFloat(property),property);
    Assert.GreaterOrEqual(body.GetFloat("_Drift"),.09f);
   }
   foreach(char letter in "ABCD") {
    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>($"Assets/CreekDiorama/Meshes/Effects/CloudPuff {letter}.asset");
    foreach(var vertex in mesh.vertices)for(int axis=0;axis<3;axis++)Assert.Less(Mathf.Abs(vertex[axis])+.29f,mesh.bounds.extents[axis],$"Cloud {letter} axis {axis}");
   }
  }

 }
}
