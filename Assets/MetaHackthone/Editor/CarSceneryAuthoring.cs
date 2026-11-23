using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor {
 public static class CarSceneryAuthoring {
  const string Root="Assets/CreekDiorama", Kit=Root+"/Prefabs/CarScenery", Meshes=Root+"/Meshes/CarScenery";
  public const string GroupName="Car campaign scenery";
  public static readonly string[] Landmarks={"Trail_Rest_Stop","Riverside_Mooring","Wheelwright_Bench","Quarry_Winch","Freight_Windmill"};
  static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+name+".mat");
  static Transform Group(Transform parent,string name){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
  [MenuItem("MetaHackthone/Creek Art/Apply car campaign scenery")]
  public static void Apply() {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play first.");
   if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
   Directory.CreateDirectory(CarSceneryReview.Output);
   BuildProps();TuneClouds();CarSceneryBackdropAuthoring.BuildKit();
   var setup=EditorSceneManager.GetSceneManagerSetup();var report=new StringBuilder();
   bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
   try {
    for(int n=1;n<=5;n++) {
     var scene=EditorSceneManager.OpenScene($"Assets/MetaHackthone/Scenes/Level{n}.unity");
     var f=SandboxFactoryAuthoring.Find(scene);var before=GameplaySnapshot(f);
     Dress(f,n);if(GameplaySnapshot(f)!=before)throw new InvalidOperationException($"Level {n} scene gameplay changed");
     EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
     string path=$"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab";
     var contents=PrefabUtility.LoadPrefabContents(path);
     try {
      var exported=contents.GetComponent<SandboxFactoryLevel>();var original=GameplaySnapshot(exported);
      Dress(exported,n);if(GameplaySnapshot(exported)!=original)throw new InvalidOperationException($"Level {n} prefab gameplay changed");
      PrefabUtility.SaveAsPrefabAsset(contents,path);
      report.AppendLine($"Level {n}: {f.Title}; scene and campaign updated; original colliders, tracks, puzzles, player and building poses preserved.");
     }finally{PrefabUtility.UnloadPrefabContents(contents);}
    }
    AssetDatabase.SaveAssets();File.WriteAllText(CarSceneryReview.Output+"/Validation.txt",report.ToString());
    Debug.Log("CAR_SCENERY_APPLIED: five scenes and campaign prefabs; gameplay snapshots unchanged.");
   }finally{SandboxTrackEditing.Suspended=suspended;if(setup.Any(s=>s.isLoaded))EditorSceneManager.RestoreSceneManagerSetup(setup);}
  }
  public static string GameplaySnapshot(SandboxFactoryLevel f) {
   var result=new StringBuilder();
   void Pose(Transform t){if(t)result.Append(t.name).Append(t.localToWorldMatrix.ToString("R"));}
   foreach(var c in f.GetComponentsInChildren<Collider>(true)){Pose(c.transform);result.Append(EditorJsonUtility.ToJson(c));}
   foreach(var t in f.Tracks){Pose(t.transform);result.Append(EditorJsonUtility.ToJson(t));}
   foreach(var p in f.Puzzles){Pose(p.transform);result.Append(EditorJsonUtility.ToJson(p));}
   Pose(f.Start);Pose(f.Finish.transform);Pose(f.Layout.PlayerStart);Pose(f.Layout.BuildingRoot);Pose(f.Layout.SandboxRoot);
   if(f.Layout.Course.BuildArea){Pose(f.Layout.Course.BuildArea.transform);result.Append(f.Layout.Course.BuildArea.Size.ToString("R"));}
   foreach(var scenery in f.Scenery)if(scenery && scenery.name!=GroupName)Pose(scenery);
   return result.ToString();
  }
  static void NewMaterial(string name,Color color,float metallic=0) {
   if(Mat(name))return;var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.25f);m.SetFloat("_Metallic",metallic);AssetDatabase.CreateAsset(m,Root+"/Materials/"+name+".mat");
  }
  static void BuildProps() {
   Directory.CreateDirectory(Kit);Directory.CreateDirectory(Meshes);AssetDatabase.Refresh();
   NewMaterial("Car scenery teal",new Color(.07f,.30f,.34f));NewMaterial("Car scenery rope",new Color(.48f,.35f,.18f));NewMaterial("Car scenery rubber",new Color(.055f,.073f,.08f));
   var path=Root+"/Models/CarSceneryProps.fbx";var importer=(ModelImporter)AssetImporter.GetAtPath(path);
   importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;importer.isReadable=true;importer.SaveAndReimport();
   var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
   foreach(var name in Landmarks.Concat(new[]{"Creek_Wind_Pennant"})) {
    var src=model.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
    var instance=Object.Instantiate(src.gameObject);instance.transform.position=Vector3.zero;
    var target=new GameObject(name);
    try {
     foreach(var special in instance.GetComponentsInChildren<Transform>().Where(t=>t.name=="Windmill rotor"||t.name=="Breeze pennant").ToArray()) {
      var moving=Group(target.transform,special.name);moving.SetPositionAndRotation(special.position,special.rotation);
      Batch(special,moving,name+"_moving");
      var breeze=moving.gameObject.AddComponent<CarSceneryBreeze>();
      bool rotor=special.name=="Windmill rotor";breeze.Axis=moving.InverseTransformDirection(rotor?Vector3.forward:Vector3.up);breeze.DegreesPerSecond=rotor?24:0;breeze.SwayDegrees=rotor?2:11;breeze.Frequency=rotor?.35f:1.1f;
      Object.DestroyImmediate(special.gameObject);
     }
     Batch(instance.transform,target.transform,name);PrefabUtility.SaveAsPrefabAsset(target,Kit+"/"+name+".prefab");
    }finally{Object.DestroyImmediate(instance);Object.DestroyImmediate(target);}
   }
  }
  static void Batch(Transform source,Transform target,string id) {
   var batches=new Dictionary<Material,List<CombineInstance>>();
   foreach(var filter in source.GetComponentsInChildren<MeshFilter>()) {
    var materials=filter.GetComponent<Renderer>().sharedMaterials;
    for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++) {
     var mat=Mat(materials[sub].name);if(!mat)throw new InvalidOperationException("Missing Creek material: "+materials[sub].name);
     if(!batches.TryGetValue(mat,out var list))batches[mat]=list=new List<CombineInstance>();
     list.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=target.worldToLocalMatrix*filter.transform.localToWorldMatrix});
    }
   }
   foreach(var batch in batches) {
    var mesh=new Mesh{name=id+" "+batch.Key.name,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(batch.Value.ToArray());mesh.RecalculateBounds();
    string path=Meshes+"/"+id+"_"+batch.Key.name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(old){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);mesh=old;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);
    var part=Group(target,batch.Key.name);part.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;part.gameObject.AddComponent<MeshRenderer>().sharedMaterial=batch.Key;
   }
  }
  static Transform Put(string name,Transform parent,Vector3 position,float scale,float yaw=0,bool botanical=false) {
   var asset=AssetDatabase.LoadAssetAtPath<GameObject>((botanical?Root+"/Prefabs/Botanical":Kit)+"/"+name+".prefab");
   var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent);go.transform.localPosition=position;go.transform.localRotation=Quaternion.Euler(0,yaw,0);go.transform.localScale=Vector3.one*scale;return go.transform;
  }
  static void Dress(SandboxFactoryLevel f,int n) {
   var environment=f.Layout.Course.SandboxFrame.Find("Environment");if(!environment)environment=Group(f.Layout.Course.SandboxFrame,"Environment");
   var previous=environment.Find(GroupName);if(previous)Object.DestroyImmediate(previous.gameObject);
   var root=Group(environment,GroupName);
   var creek=f.Scenery.FirstOrDefault(t=>t && t.name.StartsWith("Beautiful Corner art"));
   if(creek) {
    root.SetPositionAndRotation(creek.position,creek.rotation);root.localScale=creek.localScale;
    // Clear centres of the existing grass terraces, beyond the driving corridor.
    Put(Landmarks[n-1],root,new Vector3(1.02f,.677f,-.20f),.52f,160);
    Put(Landmarks[n-1],root,new Vector3(-1.04f,.613f,.16f),.42f,-25);
    Put("Creek_Wind_Pennant",root,new Vector3(1.23f,.677f,-.02f),.5f,60);
    Put("Creek_Wind_Pennant",root,new Vector3(-1.20f,.613f,.0f),.42f,40);
    Put(n==2?"Fern_Rosette":n==4?"Rock_Pebble_Cluster":"Flowers_Daisies",root,new Vector3(.86f,.68f,-.25f),.7f,35,true);
    Put(n==3?"Prop_Crate":n==4?"Rock_Stratum_C":"Flowers_Lupin",root,new Vector3(-1.20f,.615f,.25f),.45f,20,true);
   }else DressFinal(f,root);
   CarSceneryBackdropAuthoring.Dress(f,root,n,creek);
   // Include the new art in runtime scenery activation, as well as in the authoring hierarchy.
   f.Scenery=f.Scenery.Where(t=>t && t!=previous).Concat(new[]{root}).ToArray();EditorUtility.SetDirty(f);
   CarSceneryClearance.Clear(f,new StringBuilder());
   if(n==4)QuarryIslandAuthoring.Dress(f);else CampaignLandingAuthoring.Repair(f,n);
   if(root.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidOperationException("Decorations must have no physics");
  }
  static void DressFinal(SandboxFactoryLevel f,Transform root) {
   var track=f.Tracks[0];root.SetPositionAndRotation(track.transform.position,track.transform.rotation);root.localScale=Vector3.one*(track.transform.lossyScale.x/root.parent.lossyScale.x);
   var pts=track.Samples;var bounds=new Bounds(pts[0],Vector3.zero);foreach(var p in pts)bounds.Encapsulate(p);
   float waterY=bounds.min.y-3;
   foreach(var index in new[]{0,pts.Length/3,pts.Length*2/3,pts.Length-1}) {
    Vector3 p=pts[index];float side=index==0?-1:1;
    var island=Group(root,"Freight terrace "+index);island.localPosition=p+new Vector3(side*(track.DefaultWidth*.5f+2.7f),-.4f,0);
    float height=p.y-.4f-waterY;int layers=Mathf.Max(2,Mathf.CeilToInt(height/3));
    for(int j=0;j<layers;j++) {
     float layerHeight=height/layers;
     var rock=Put("Rock_Stratum_A",island,new Vector3((j%2)*.16f,-height+j*layerHeight,0),1,25+(j%2)*8,true);
     rock.localScale=new Vector3(17+(layers-j)*.3f,layerHeight/.32f,17+(layers-j)*.3f);
    }
    var moss=Put("Moss_Overhang",island,Vector3.zero,15,0,true);
    Put(index==0?"Quarry_Winch":"Freight_Windmill",island,new Vector3(0,0,0),5.5f,-25);
    Put("Creek_Wind_Pennant",island,new Vector3(1.35f,0,1.2f),4.5f,30);
    Put("Pine_Branching",island,new Vector3(1.7f,0,1.3f),5,20,true);
    Put("Fern_Rosette",island,new Vector3(-1.3f,0,-1.1f),6,0,true);
    Put("Ivy_Cascade",island,new Vector3(0,-.2f,-1.75f),7,0,true);
   }
   // River tiles below the curved course; the driving path and puzzle gaps stay open.
   var riverMat=Mat("Freight river");if(!riverMat){riverMat=new Material(Mat("Modular river")){name="Freight river"};AssetDatabase.CreateAsset(riverMat,Root+"/Materials/Freight river.mat");}
   riverMat.SetFloat("_RippleScale",9);riverMat.SetFloat("_FlowSpeed",.18f);riverMat.SetFloat("_UseDepth",0);riverMat.SetFloat("_UseOpaqueTexture",0);EditorUtility.SetDirty(riverMat);
   Basin(root,bounds,waterY,riverMat);
   // Share the same 10 world-fixed puff meshes used by the other campaign levels.
   var clouds=Group(root,"Layered realistic cloud banks");
   var behind=bounds.center-root.InverseTransformPoint(f.Layout.PlayerStart.position);behind.y=0;behind.Normalize();
   for(int i=0;i<10;i++) {
    var puff=Group(clouds,"Cloud puff "+i.ToString("00"));var direction=Quaternion.Euler(0,-58+i/9f*116,0)*behind;
    puff.localPosition=bounds.center+direction*(bounds.size.magnitude+28)+Vector3.up*(4+i%3*4);puff.localScale=Vector3.one*(10+i%3*2);puff.localRotation=Quaternion.Euler(0,i*37,0);
    puff.gameObject.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Effects/CloudPuff "+(char)('A'+i%4)+".asset");
    var r=puff.gameObject.AddComponent<MeshRenderer>();r.sharedMaterials=new[]{Mat("Cloud puff "+i.ToString("00")),Mat("Cloud shell "+i.ToString("00"))};r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
   }
  }
  static void Basin(Transform parent,Bounds bounds,float height,Material water) {
   float x=bounds.extents.x+6,z=bounds.extents.z+5,c=2.5f;
   Vector2[] outline={new(-x+c,-z),new(x-c,-z),new(x,-z+c),new(x,z-c),new(x-c,z),new(-x+c,z),new(-x,z-c),new(-x,-z+c)};
   var v=outline.Select(p=>new Vector3(p.x+bounds.center.x,height,p.y+bounds.center.z)).ToArray();var tris=new List<int>();
   for(int i=1;i<7;i++)tris.AddRange(new[]{0,i+1,i});
   Mesh mesh=new Mesh{name="Freight creek surface"};mesh.vertices=v;mesh.triangles=tris.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
   SavePart(parent,"Freight creek",mesh,water);
   var walls=new List<Vector3>();tris.Clear();
   for(int i=0;i<8;i++) {
    int k=walls.Count;Vector3 a=v[i],b=v[(i+1)%8];walls.AddRange(new[]{a,b,b-Vector3.up*.9f,a-Vector3.up*.9f});tris.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});
   }
   mesh=new Mesh{name="Freight creek stone rim"};mesh.SetVertices(walls);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();SavePart(parent,"Freight creek stone rim",mesh,Mat("Botanical limestone"));
  }
  static void SavePart(Transform parent,string name,Mesh mesh,Material mat) {
   string path=Meshes+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(old){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);mesh=old;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);
   var part=Group(parent,name);part.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;part.gameObject.AddComponent<MeshRenderer>().sharedMaterial=mat;
  }
  static void TuneClouds() {
   for(int j=0;j<10;j++)foreach(var prefix in new[]{"Cloud puff ","Cloud shell "}) {
    var m=Mat(prefix+j.ToString("00"));m.SetFloat("_Drift",j<3?.09f:j<7?.12f:.15f);m.SetFloat("_RollSpeed",.035f+j*.002f);m.SetFloat("_WispSpeed",.065f+j*.004f);EditorUtility.SetDirty(m);
   }
   for(int i=0;i<4;i++){var m=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Effects/CloudPuff "+(char)('A'+i)+".asset");m.bounds=new Bounds(Vector3.zero,new Vector3(1.9f,1.2f,1.4f));EditorUtility.SetDirty(m);}
  }
 }
}
