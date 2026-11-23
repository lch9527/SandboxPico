using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
using static PhysicalAssembly.Editor.EnvironmentPolishAuthoring;
namespace PhysicalAssembly.Editor {
 public static class SceneFocusAuthoring {
  public static void Veil(SandboxFactoryLevel f,string key){
   var art=f.Layout.Course.SandboxFrame.Find("Environment/Car campaign scenery");var old=art.Find("Soft sky backdrop");if(old)Object.DestroyImmediate(old.gameObject);
   var pose=f.Layout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance);
   var far=art.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
   float radius=far.Max(r=>Vector3.Distance(pose.position,r.bounds.center)+r.bounds.extents.magnitude)*1.12f;
   radius=Mathf.Clamp(radius,10,85);
   var root=Group(art,"Soft sky backdrop");root.SetPositionAndRotation(pose.position,pose.rotation);root.localScale=Vector3.one/art.lossyScale.x;
   var v=new List<Vector3>();var uv=new List<Vector2>();var ix=new List<int>();const int segments=48;
   for(int j=0;j<=segments;j++){float u=j/(float)segments,a=Mathf.Lerp(-108,108,u)*Mathf.Deg2Rad;foreach(float y in new[]{-.72f,1.6f}){v.Add(new Vector3(Mathf.Sin(a),y,Mathf.Cos(a))*radius);uv.Add(new Vector2(u,y<0?0:1));}if(j<segments){int k=j*2;ix.AddRange(new[]{k,k+1,k+3,k,k+3,k+2});}}
   var mesh=new Mesh{name="Curved translucent sky veil"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(ix,0);mesh.RecalculateBounds();
   const string path="Assets/CreekDiorama/Materials/Soft horizon veil.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(Shader.Find("Creek Diorama/Soft Horizon Veil"));AssetDatabase.CreateAsset(mat,path);}
   RenderMesh(root,"Sky haze",Save(mesh,key+"-SkyVeil"),mat);var r=root.GetComponentInChildren<Renderer>();r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;
  }
  public static void Garden(SandboxFactoryLevel f,string key){
   var art=f.Layout.Course.SandboxFrame.Find("Environment/Car campaign scenery");var old=art.Find("Freight wildflower borders");if(old)Object.DestroyImmediate(old.gameObject);
   var root=Group(art,"Freight wildflower borders");root.localScale=Vector3.one/art.lossyScale.x;
   var source=Group(root,"Editable planting");var batch=Group(root,"Flower and grass batches");var lanes=CarSceneryClearance.Lanes(f);
   var grounds=ContactPolishAuthoring.VisibleGround(f.gameObject).Where(m=>m.name=="Campaign island ground turf").ToArray();var rng=new System.Random(275);
   var crowd=f.GetComponentInChildren<SandboxFinishCrowd>(true);var accepted=new List<Vector3>();int id=0;
   foreach(var ground in grounds){var surface=new ContactPolishAuthoring.Surface(new[]{ground});var bounds=ground.GetComponent<Renderer>().bounds;int planted=0;
    for(int attempt=0;attempt<1600&&planted<38;attempt++){
     var p=new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,(float)rng.NextDouble()),0,Mathf.Lerp(bounds.min.z,bounds.max.z,(float)rng.NextDouble()));
     if(!surface.Height(p,out float y))continue;p.y=y;
     if(accepted.Any(q=>(q-p).sqrMagnitude<.065f))continue;
     if(crowd){var local=crowd.transform.InverseTransformPoint(p);if(Mathf.Abs(local.x)<crowd.Area.x*.65f&&Mathf.Abs(local.z)<crowd.Area.y*.75f)continue;}
     string name=planted%7==0?"Flowers_Lupin":planted%3==0?"Flowers_Daisies":planted%8==0?"Fern_Rosette":"Grass_Soft_Tuft";
     var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CreekDiorama/Prefabs/Botanical/"+name+".prefab");var t=((GameObject)PrefabUtility.InstantiatePrefab(prefab,source)).transform;
     t.SetPositionAndRotation(p,Quaternion.Euler(0,(float)rng.NextDouble()*360,0));t.localScale=Vector3.one*(1.0f+(float)rng.NextDouble()*.55f);
     var rr=t.GetComponentsInChildren<Renderer>();var box=rr[0].bounds;foreach(var r in rr)box.Encapsulate(r.bounds);
     float lift=0;bool supported=true;foreach(float x in new[]{-.7f,.7f})foreach(float z in new[]{-.7f,.7f}){var q=box.center+new Vector3(box.extents.x*x,0,box.extents.z*z);if(!surface.Height(q,out float h)||Mathf.Abs(h-y)>.07f)supported=false;else lift=Mathf.Max(lift,h-y);}
     box.Expand(.055f);
     if(!supported||CarSceneryClearance.Overlaps(box,lanes)){Object.DestroyImmediate(t.gameObject);continue;}
     t.position+=Vector3.up*(lift+.003f);accepted.Add(t.position);t.name=name+" "+id++;planted++;
    }
    if(planted<12)throw new InvalidOperationException("Insufficient supported planting on "+ground.name+": "+planted);
   }
   Combine(source,batch,key+"-FreightGarden");source.gameObject.SetActive(false);
   foreach(var r in batch.GetComponentsInChildren<Renderer>())if(CarSceneryClearance.Intrudes(r,lanes))throw new InvalidOperationException("Garden enters road: "+r.name);
   Debug.Log("FREIGHT_GARDEN plants="+id+" renderers="+batch.childCount);
  }
  public static string Apply(int n){bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;try{
   var scene=EditorSceneManager.OpenScene($"Assets/MetaHackthone/Scenes/Level{n}.unity");var f=SandboxFactoryAuthoring.Find(scene);if(n==5)Garden(f,"Scene5");Veil(f,"Scene"+n);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   var path=$"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab";var root=PrefabUtility.LoadPrefabContents(path);try{f=root.GetComponent<SandboxFactoryLevel>();if(n==5)Garden(f,"Level5");Veil(f,"Level"+n);PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}AssetDatabase.SaveAssets();return "Updated focus scenery "+n;
  }finally{SandboxTrackEditing.Suspended=suspended;}}
 }
}
