using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PhysicalAssembly.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Tests {
 public sealed class SceneryMotionTests {
  static bool Visible(Transform t,Transform root){while(t){if(!t.gameObject.activeSelf)return false;if(t==root)break;t=t.parent;}return true;}
  static SandboxFactoryLevel Level(int n)=>AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>("Assets/MetaHackthone/Generated/Campaign/Level"+n+".prefab");
  [TestCase(1),TestCase(2),TestCase(3),TestCase(4),TestCase(5)]
  public void SavedLevelsHaveCompleteMotionAndPersistedResources(int n){
   var f=Level(n);var env=f.Layout.Course.SandboxFrame.Find("Environment");
   Assert.IsTrue(f.AuthoredObjects);Assert.IsTrue(f.Validate(out var error),error);
   var names=new[]{"Pine_Branching","Tree_Round_Canopy","Fern_Rosette","Ivy_Cascade","Flowers_Daisies","Flowers_Lupin"};
   foreach(var plant in env.GetComponentsInChildren<Transform>(true).Where(t=>names.Contains(t.name)&&Visible(t,env))){
    foreach(var filter in plant.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.GetComponent<Renderer>().enabled&&Visible(f.transform,plant))){
     Assert.IsTrue(AssetDatabase.Contains(filter.sharedMesh));
     var data=new List<Vector4>();filter.sharedMesh.GetUVs(3,data);
     Assert.AreEqual(filter.sharedMesh.vertexCount,data.Count,plant.name);
     Assert.IsTrue(data.Any(d=>d.y>0),plant.name);
     var rope=new List<Vector4>();filter.sharedMesh.GetUVs(4,rope);
     Assert.AreEqual(filter.sharedMesh.vertexCount,rope.Count,"Populate unused shader attributes explicitly");
     Assert.IsTrue(rope.All(v=>v==Vector4.zero));
     Assert.IsTrue(filter.GetComponent<Renderer>().sharedMaterials.All(m=>m.shader.name==SceneryWindClock.ShaderName),plant.name);
    }
   }
   var machines=new[]{"Wheelwright_Bench","Quarry_Winch","Riverside_Mooring"};
   foreach(var t in env.GetComponentsInChildren<Transform>(true).Where(t=>machines.Contains(t.name)&&Visible(t,env))){
    Assert.IsNotNull(t.GetComponent<SceneryMotionAuthored>(),t.name);
    int expected=t.name=="Riverside_Mooring"?1:0;
    Assert.AreEqual(expected,t.GetComponentsInChildren<CarSceneryBreeze>(true).Length,t.name);
   }
   if(n==5)Assert.AreEqual(3,env.GetComponentsInChildren<CarSceneryBreeze>(true).Count(b=>b.name=="Windmill rotor"&&b.enabled&&b.DegreesPerSecond==24));
  }
  static double Area(Transform root){double area=0;foreach(var f in root.GetComponentsInChildren<MeshFilter>(true)){
   var v=f.sharedMesh.vertices.Select(p=>root.InverseTransformPoint(f.transform.TransformPoint(p))).ToArray();var t=f.sharedMesh.triangles;
   for(int i=0;i<t.Length;i+=3)area+=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).magnitude*.5;
  }return area;}
  [TestCase("Wheelwright_Bench",3),TestCase("Quarry_Winch",4),TestCase("Riverside_Mooring",2)]
  public void SeparatingMovingPartsPreservesRestSurface(string name,int n){
   var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CreekDiorama/Prefabs/CarScenery/"+name+".prefab");
   var t=Level(n).GetComponentsInChildren<Transform>(true).First(x=>x.name==name);
   Assert.That(Area(t),Is.EqualTo(Area(original.transform)).Within(.0001));
   var moving=t.GetComponentsInChildren<CarSceneryBreeze>(true);
   foreach(var group in moving.GroupBy(b=>b.name)){
    Assert.AreEqual(1,group.Select(b=>b.Phase).Distinct().Count(),"All material pieces must move together");
    foreach(var b in group){Assert.AreEqual(0,b.GetComponentsInChildren<Collider>(true).Length);Assert.AreEqual(0,(int)GameObjectUtility.GetStaticEditorFlags(b.gameObject));}
   }
  }
  [Test] public void RopesHaveFixedEndpointsInteriorVerticesAndExpandedBounds(){
   var ropes=Level(2).GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("Flexible mooring rope")).ToArray();Assert.IsNotEmpty(ropes,"Authored mooring ropes must retain their motion data; their count follows scene placement.");
   foreach(var f in ropes){
    var data=new List<Vector4>();f.sharedMesh.GetUVs(4,data);var p=data[0];var vertices=f.sharedMesh.vertices;
    Assert.IsTrue(vertices.Any(v=>v.x>p.y+p.z*.25f && v.x<p.y+p.z*.75f),"Rope needs vertices between endpoints");
    Assert.That(p.w,Is.EqualTo(p.z*.02f).Within(.000001f));
    Assert.IsTrue(vertices.All(v=>v.x>=p.y-.00001f&&v.x<=p.y+p.z+.00001f));
    Assert.GreaterOrEqual(f.sharedMesh.bounds.extents.z,vertices.Max(v=>Mathf.Abs(v.z-f.sharedMesh.bounds.center.z))+p.w-.00001f);
   }
  }
  [Test] public void DeletingMovingPiecesDoesNotRegenerateOnConversion(){
   var preview=EditorSceneManager.NewPreviewScene();var root=Object.Instantiate(Level(2).GetComponentsInChildren<Transform>(true).First(t=>t.name=="Riverside_Mooring").gameObject);
   SceneManager.MoveGameObjectToScene(root,preview);
   try{var piece=root.GetComponentInChildren<CarSceneryBreeze>(true);Object.DestroyImmediate(piece.gameObject);int count=root.GetComponentsInChildren<Transform>(true).Length;
    Assert.AreEqual(0,SceneryMotionAuthoring.AnimateMachine(root.transform,"Riverside_Mooring"));Assert.AreEqual(count,root.GetComponentsInChildren<Transform>(true).Length);
   }finally{EditorSceneManager.ClosePreviewScene(preview);}
  }
  [Test] public void ShaderCompilesWithoutErrors(){var shader=Shader.Find(SceneryWindClock.ShaderName);Assert.IsNotNull(shader);Assert.IsTrue(shader.isSupported);Assert.IsEmpty(ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error));}
 }
}
