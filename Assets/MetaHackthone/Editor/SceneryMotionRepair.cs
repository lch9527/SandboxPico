using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor {
 public static class SceneryMotionRepair {
  const string MaterialPath="Assets/CreekDiorama/Materials/Ground palette.mat";
  public static Material GroundMaterial(){
   var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/CreekDiorama/Materials/Horizon palette.mat");
   var ground=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
   if(!ground){ground=new Material(source){name="Ground palette"};AssetDatabase.CreateAsset(ground,MaterialPath);}
   ground.SetFloat("_FloatHeight",0);EditorUtility.SetDirty(ground);
   source.SetFloat("_FloatHeight",.2f);EditorUtility.SetDirty(source);return ground;
  }
  static bool Visible(Transform t){while(t){if(!t.gameObject.activeSelf)return false;t=t.parent;}return true;}
  static string Path(Transform t)=>AnimationUtility.CalculateTransformPath(t,null)+" ["+t.GetSiblingIndex()+"]";
  static Bounds Box(Vector3 a,Vector3 b,Vector3 c){var box=new Bounds(a,Vector3.zero);box.Encapsulate(b);box.Encapsulate(c);return box;}
  static bool Touch(Vector3 a,Vector3 b,Vector3 c,Vector3 x,Vector3 y,Vector3 z)=>
   SandboxMeshContact.DistanceSquared(a,b,x,y,z)<1e-10f||SandboxMeshContact.DistanceSquared(b,c,x,y,z)<1e-10f||SandboxMeshContact.DistanceSquared(c,a,x,y,z)<1e-10f||
   SandboxMeshContact.DistanceSquared(x,y,a,b,c)<1e-10f||SandboxMeshContact.DistanceSquared(y,z,a,b,c)<1e-10f||SandboxMeshContact.DistanceSquared(z,x,a,b,c)<1e-10f;
  sealed class Surface {
   public Matrix4x4 Matrix;public Mesh Mesh; public Vector3[] Vertices;public int[] Indices;public Bounds[] Boxes;
   public Surface(MeshFilter f){Matrix=f.transform.localToWorldMatrix;Mesh=f.sharedMesh;Vertices=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();Indices=f.sharedMesh.triangles;Boxes=new Bounds[Indices.Length/3];
    for(int i=0;i<Indices.Length;i+=3)Boxes[i/3]=Box(Vertices[Indices[i]],Vertices[Indices[i+1]],Vertices[Indices[i+2]]);}
  }
  static readonly Dictionary<MeshFilter,Surface> surfaces=new();
  static Surface Cached(MeshFilter f){if(!surfaces.TryGetValue(f,out var s)||s.Matrix!=f.transform.localToWorldMatrix||s.Mesh!=f.sharedMesh)surfaces[f]=s=new Surface(f);return s;}
  static bool Support(Transform pole,Transform env,Vector3 origin,float scale,out float height){
   height=float.NegativeInfinity;
   foreach(var f in env.GetComponentsInChildren<MeshFilter>(false)){
    if(!f.sharedMesh||f.transform.IsChildOf(pole))continue;
    string path=Path(f.transform).ToLowerInvariant();
    if(!(path.Contains("ground turf")||path.Contains("limestone")||path.Contains("rock")||path.Contains("display base")))continue;
    var r=f.GetComponent<Renderer>();if(!r||!r.enabled)continue;
    var bounds=r.bounds;if(origin.x<bounds.min.x||origin.x>bounds.max.x||origin.z<bounds.min.z||origin.z>bounds.max.z)continue;
    var s=Cached(f);
    for(int i=0;i<s.Indices.Length;i+=3){var box=s.Boxes[i/3];if(origin.x<box.min.x||origin.x>box.max.x||origin.z<box.min.z||origin.z>box.max.z)continue;
     var a=s.Vertices[s.Indices[i]];var b=s.Vertices[s.Indices[i+1]];var c=s.Vertices[s.Indices[i+2]];
     var normal=Vector3.Cross(b-a,c-a);if(Mathf.Abs(normal.y)<1e-8f)continue;
     float y=a.y-(normal.x*(origin.x-a.x)+normal.z*(origin.z-a.z))/normal.y;
     if(Mathf.Abs(y-origin.y)>1.5f*scale||y<=height)continue;
     var p=new Vector3(origin.x,y,origin.z);
     if(Vector3.Dot(Vector3.Cross(b-a,p-a),normal)>=0&&Vector3.Dot(Vector3.Cross(c-b,p-b),normal)>=0&&Vector3.Dot(Vector3.Cross(a-c,p-c),normal)>=0)height=y;
    }
   }
   return !float.IsNegativeInfinity(height);
  }
  // Triangle checks avoid false positives from large, sparse botanical render batches.
  public static List<string> Contacts(CarSceneryBreeze motion,Transform environment){
   var result=new List<string>();var frame=motion.transform;var rotor=motion.name=="Windmill rotor";
   var filters=frame.GetComponentsInChildren<MeshFilter>().Where(f=>f.sharedMesh).ToArray();
   var sweep=new Bounds(frame.position,Vector3.zero);
   foreach(var f in filters){var v=f.sharedMesh.vertices;var m=frame.worldToLocalMatrix*f.transform.localToWorldMatrix;
    float radius=v.Select(p=>m.MultiplyPoint3x4(p).magnitude).DefaultIfEmpty(0).Max()*Mathf.Max(frame.lossyScale.x,frame.lossyScale.y,frame.lossyScale.z);
    sweep.Encapsulate(new Bounds(frame.position,Vector3.one*radius*2));}
   foreach(var target in environment.GetComponentsInChildren<MeshFilter>(true)){
    var renderer=target.GetComponent<Renderer>();if(!target.sharedMesh||!renderer||!renderer.enabled||!Visible(target.transform)||target.transform.IsChildOf(frame))continue;
    // A flag's attached edge is intentionally embedded in its own mast.
    if(!rotor&&target.transform.IsChildOf(frame.parent))continue;
    if(renderer.sharedMaterials.Any(m=>m&&(m.shader.name.Contains("Cloud")||m.shader.name.Contains("Water")||m.shader.name.Contains("Spray"))))continue;
    if(!sweep.Intersects(renderer.bounds))continue;
    var surface=Cached(target);var near=Enumerable.Range(0,surface.Boxes.Length).Where(i=>sweep.Intersects(surface.Boxes[i])).ToArray();if(near.Length==0)continue;
    bool hit=false;
    foreach(var f in filters){
     var vertices=f.sharedMesh.vertices.Select(p=>frame.InverseTransformPoint(f.transform.TransformPoint(p))).ToArray();var indices=f.sharedMesh.triangles;
     int steps=rotor?72:24;
     for(int s=0;s<=steps&&!hit;s++){
      float angle=rotor?s*360f/steps:Mathf.Lerp(-motion.SwayDegrees,motion.SwayDegrees,s/(float)steps);
      var matrix=frame.parent.localToWorldMatrix*Matrix4x4.TRS(frame.localPosition,frame.localRotation*Quaternion.AngleAxis(angle,motion.Axis),frame.localScale);
      var moved=vertices.Select(matrix.MultiplyPoint3x4).ToArray();var axis=motion.Axis.normalized;
      for(int k=0;k<indices.Length&&!hit;k+=3){
       if(rotor&&Enumerable.Range(k,3).All(j=>Vector3.Cross(vertices[indices[j]],axis).magnitude<.045f))continue;
       var a=moved[indices[k]];var b=moved[indices[k+1]];var c=moved[indices[k+2]];var bounds=Box(a,b,c);
       foreach(int j in near){if(!bounds.Intersects(surface.Boxes[j]))continue;int q=j*3;
        if(Touch(a,b,c,surface.Vertices[surface.Indices[q]],surface.Vertices[surface.Indices[q+1]],surface.Vertices[surface.Indices[q+2]])){hit=true;break;}}
      }
     }
     if(hit)break;
    }
    if(hit)result.Add(Path(target.transform));
   }
   return result;
  }
  public static string Repair(GameObject root,bool fixFlags){
   surfaces.Clear();var report=new System.Text.StringBuilder();var ground=GroundMaterial();
   foreach(var r in root.GetComponentsInChildren<Renderer>(true)){
    if(r.name!="Palette mesh"||!r.sharedMaterial||r.sharedMaterial.shader.name!="Creek Diorama/Horizon Palette"||Path(r.transform).Contains("Horizon transition islands"))continue;
    r.sharedMaterial=ground;EditorUtility.SetDirty(r);
   }
   foreach(var b in root.GetComponentsInChildren<CarSceneryBreeze>(true).Where(b=>b.name.StartsWith("Wheel ")||b.name=="Winch drum and crank").ToArray()){
    report.AppendLine("Idle: "+Path(b.transform));Object.DestroyImmediate(b);}
   if(fixFlags){var env=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Environment");
    foreach(var b in env.GetComponentsInChildren<CarSceneryBreeze>(true).Where(b=>b.name=="Breeze pennant"&&Visible(b.transform))){
     var contacts=Contacts(b,env);if(contacts.Count==0)continue;var pole=b.transform.parent;var rotation=pole.localRotation;bool clear=false;
     var worldRotation=pole.rotation;
     for(int step=1;step<24;step++){pole.rotation=Quaternion.AngleAxis(step*15,env.up)*worldRotation;if(Contacts(b,env).Count==0){clear=true;break;}}
     var origin=pole.position;float scale=Mathf.Max(pole.lossyScale.x,pole.lossyScale.y,pole.lossyScale.z);
     for(int ring=1;ring<=12&&!clear;ring++)for(int direction=0;direction<16&&!clear;direction++){
      float angle=direction*Mathf.PI/8;var p=origin+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*ring*.08f*scale;
      if(!Support(pole,env,p,scale,out float y))continue;p.y=y;
      // All four corners of the small pedestal must remain on solid support.
      bool supported=true;for(int corner=0;corner<4;corner++){var q=p+new Vector3((corner&1)==0?-.06f:.06f,0,(corner&2)==0?-.06f:.06f)*scale;
       if(!Support(pole,env,q,scale,out float cy)||Mathf.Abs(cy-y)>.025f*scale){supported=false;break;}}
      if(!supported)continue;pole.position=p;pole.rotation=worldRotation;
      if(Contacts(b,env).Count==0)clear=true;
     }
     if(!clear)pole.position=origin;
     if(!clear){pole.localRotation=rotation;report.AppendLine("UNRESOLVED: "+Path(pole)+" -> "+string.Join("; ",contacts));}
     else {EditorUtility.SetDirty(pole);if(PrefabUtility.IsPartOfPrefabInstance(pole))PrefabUtility.RecordPrefabInstancePropertyModifications(pole);report.AppendLine("Placed pennant: "+Path(pole)+" position "+pole.localPosition.ToString("R")+" rotation "+pole.localEulerAngles);}
    }
   }
   return report.ToString();
  }
  public static string ApplyAll(){
   if(Application.isPlaying)throw new InvalidOperationException("Stop Play first");
   var report=new System.Text.StringBuilder();var active=SceneManager.GetActiveScene();bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
   try{
    for(int n=1;n<=5;n++){
     string path="Assets/MetaHackthone/Scenes/Level"+n+".unity";var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;
     if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
     try{var f=SandboxFactoryAuthoring.Find(scene);string before=CarSceneryAuthoring.GameplaySnapshot(f);report.AppendLine(path);report.Append(Repair(f.gameObject,true));
      if(before!=CarSceneryAuthoring.GameplaySnapshot(f))throw new Exception("Gameplay changed");EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
     }finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
     path="Assets/MetaHackthone/Generated/Campaign/Level"+n+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
     try{report.AppendLine(path);report.Append(Repair(root,true));PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    foreach(string folder in new[]{"Assets/CreekDiorama/AnimatedScenery/Prefabs","Assets/CreekDiorama/Prefabs/CarScenery/Transitions"})
     foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{folder})){string path=AssetDatabase.GUIDToAssetPath(guid);var root=PrefabUtility.LoadPrefabContents(path);try{Repair(root,false);PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}}
    AssetDatabase.SaveAssets();Directory.CreateDirectory("Logs/SceneryMotion");File.WriteAllText("Logs/SceneryMotion/Repair-report.txt",report.ToString());return report.ToString();
   }finally{SandboxTrackEditing.Suspended=suspended;if(active.IsValid())SceneManager.SetActiveScene(active);}
  }
 }
}
