using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor {
 public static class CarSceneryPartsAuthoring {
  const string Root="Assets/CreekDiorama";
  public const string Parts=Root+"/Prefabs/CarScenery/Parts", Editable=Root+"/Prefabs/CarScenery/Editable", CloudParts=Root+"/Prefabs/CarScenery/Clouds";
  const string Meshes=Root+"/Meshes/CarSceneryParts";
  public const string Gallery=Root+"/Scenes/CarSceneryPartsGallery.unity";
  static readonly string[] Themes={"Trail_Rest_Stop","Riverside_Mooring","Wheelwright_Bench","Quarry_Winch","Freight_Windmill","Creek_Wind_Pennant"};
  static string Name(Transform t)=>Regex.Replace(t.name,@"\.\d+$","");
  static bool IsWheel(string name)=>new[]{"Rubber tyre","Oak wheel rim","Axle cap","Wheel spoke"}.Contains(name);
  static string Classify(string theme,MeshFilter f) {
   string n=Name(f.transform);var p=f.transform.position;
   if(n.StartsWith("Deck "))return "Deck";
   if(theme=="Trail_Rest_Stop") {
    if(n.StartsWith("Crate "))return "Supply_Crate";
    if(n.StartsWith("Trail map")||n.StartsWith("Map "))return "Trail_Map";
    if(n.StartsWith("Bench ")||n.StartsWith("Seat ")||n.StartsWith("Back "))return "Bench";
   }
   if(theme=="Riverside_Mooring") {
    if(n=="Mooring post"||n=="Rope binding")return "Mooring_Post_"+(p.x<0?"L":"R")+(p.z<0?"Front":"Back");
    if(n=="Coiled mooring rope")return "Rope_Coil";
    if(n=="Harbour pole")return "Life_Ring_Stand";
    if(n=="Life ring")return "Life_Ring";
    if(n=="Mooring rope")return "Mooring_Rope_"+(p.z<0?"Front":"Back");
    if(n.StartsWith("Crab ")||n.StartsWith("Basket "))return "Crab_Pot_"+(p.x<.135f?"A":"B");
   }
   if(theme=="Wheelwright_Bench") {
    if(n.StartsWith("Bench ")||n.StartsWith("Workshop "))return "Workbench";
    if(n.StartsWith("Rack "))return "Wheel_Rack";
    if(IsWheel(n))return "Wheel_"+(p.y>.35f?"Rack_":"Ground_")+(p.x<0?"Small":"Large");
    if(n.StartsWith("Toolbox")||n=="Handle mount")return "Toolbox";
    if(n=="Spanner")return "Spanner";
   }
   if(theme=="Quarry_Winch") {
    if(n.StartsWith("Crate "))return "Crate";
    if(n=="Cut limestone")return "Limestone_Blocks";
    if(new[]{"A-frame foot","Timber frame","Winch axle","Drum rim","Cable drum","Wound cable","Overhead beam","Crank","Crank grip"}.Contains(n))return "Winch";
   }
   if(theme=="Freight_Windmill") {
    if(f.GetComponentsInParent<Transform>().Any(t=>Name(t)=="Windmill rotor"))return "Rotor";
    if(n.StartsWith("Crate "))return p.x<0?"Crate_Left":p.y>.225f?"Crate_Top":"Crate_Right";
    if(new[]{"Tower leg","Tower tie","Cross brace","Rotor axle"}.Contains(n))return "Tower";
   }
   if(theme=="Creek_Wind_Pennant") {
    if(n=="Woven pennant")return "Flag";
    if(new[]{"Pennant foot","Flag mast","Mast finial"}.Contains(n))return "Mast";
   }
   throw new InvalidOperationException($"Unclassified source geometry: {theme}/{f.name}");
  }
  [MenuItem("MetaHackthone/Creek Art/Build manual scenery parts and gallery")]
  public static void Build() {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play first.");
   if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
   var setup=EditorSceneManager.GetSceneManagerSetup();
   foreach(var dir in new[]{Parts,Editable,CloudParts,Meshes})Directory.CreateDirectory(dir);AssetDatabase.Refresh();
   var report=new StringBuilder("# Manual scenery parts\n\nEvery static pivot is bottom-centred, Y-up, scale 1. Animated pivots stay on the shaft/flag hinge. No physics components.\n\n| Assembly | Parts | Source triangles | Export triangles |\n|---|---:|---:|---:|\n");
   var assets=new List<GameObject>();
   try {
    var model=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Models/CarSceneryProps.fbx");
    foreach(var theme in Themes) {
     var source=Object.Instantiate(model.GetComponentsInChildren<Transform>().Single(t=>t.name==theme).gameObject);source.transform.position=Vector3.zero;
     var assembly=new GameObject(theme+"_Editable");
     try {
      var filters=source.GetComponentsInChildren<MeshFilter>();var groups=filters.GroupBy(f=>Classify(theme,f)).OrderBy(g=>g.Key).ToArray();int exportedTriangles=0;
      foreach(var group in groups) {
       var bounds=GeometryBounds(group);
       bool rotor=group.Key=="Rotor",flag=group.Key=="Flag";
       var pivot=rotor||flag?source.GetComponentsInChildren<Transform>().Single(t=>Name(t)==(rotor?"Windmill rotor":"Breeze pennant")).position:new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
       string id=theme+"__"+group.Key;var part=new GameObject(id);part.transform.position=pivot;
       try {
        Combine(group.ToArray(),part.transform,id);
        part.transform.position=Vector3.zero;
        if(rotor||flag) {var wind=part.AddComponent<CarSceneryBreeze>();wind.Axis=rotor?Vector3.forward:Vector3.up;wind.DegreesPerSecond=rotor?24:0;wind.SwayDegrees=rotor?2:11;wind.Frequency=rotor?.35f:1.1f;}
        var asset=PrefabUtility.SaveAsPrefabAsset(part,Parts+"/"+id+".prefab");assets.Add(asset);
        var nested=(GameObject)PrefabUtility.InstantiatePrefab(asset,assembly.transform);nested.transform.localPosition=pivot;
        exportedTriangles+=nested.GetComponentsInChildren<MeshFilter>().Sum(m=>m.sharedMesh.triangles.Length/3);
       }finally{Object.DestroyImmediate(part);}
      }
      int sourceTriangles=filters.Sum(m=>m.sharedMesh.triangles.Length/3);
      if(sourceTriangles!=exportedTriangles)throw new InvalidOperationException(theme+": lost or duplicated triangles");
      var a=Bounds(source.GetComponentsInChildren<Renderer>());var b=Bounds(assembly.GetComponentsInChildren<Renderer>());
      if(Vector3.Distance(a.min,b.min)>.0001f||Vector3.Distance(a.max,b.max)>.0001f)throw new InvalidOperationException(theme+": reconstructed bounds changed");
      PrefabUtility.SaveAsPrefabAsset(assembly,Editable+"/"+theme+"_Editable.prefab");
      report.AppendLine($"| {theme} | {groups.Length} | {sourceTriangles} | {exportedTriangles} |");
     }finally{Object.DestroyImmediate(source);Object.DestroyImmediate(assembly);}
    }
    BuildClouds();BuildEditableLandscape();UpgradeCloudNoise();AssetDatabase.SaveAssets();BuildGallery(assets);
    Directory.CreateDirectory("Docs/CreekDiorama");File.WriteAllText("Docs/CreekDiorama/Scenery-Parts-Inventory.md",report.ToString());
    Debug.Log("CREEK_MANUAL_PARTS_BUILT: "+assets.Count+" independent parts, 6 nested assemblies, 3 cloud prefabs.");
   }finally{if(setup.Any(s=>s.isLoaded))EditorSceneManager.RestoreSceneManagerSetup(setup);}
  }
  [MenuItem("MetaHackthone/Creek Art/Open manual scenery gallery")]
  public static void OpenGallery(){if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(Gallery);}
  static void BuildEditableLandscape() {
   var path=Root+"/Prefabs/BeautifulCorner.prefab";var source=PrefabUtility.LoadPrefabContents(path);
   try {
    source.name="Creek_Environment_Editable";
    var botanical=source.transform.Find("10 Botanical dressing");
    if(!botanical)throw new Exception("Missing editable botanical source");
    var placements=botanical.Cast<Transform>().Single(t=>t.name.StartsWith("Editable placement"));placements.gameObject.SetActive(true);placements.name="Editable trees rocks plants and props";
    var batches=botanical.Find("Render batches");if(batches)Object.DestroyImmediate(batches.gameObject);
    foreach(var child in source.transform.Cast<Transform>().ToArray()) {
     if(!child.gameObject.activeSelf && new[]{"02 ","03 ","05 ","06 "}.Any(p=>child.name.StartsWith(p)))Object.DestroyImmediate(child.gameObject);
    }
    // The new standalone cloud prefabs can be placed freely; omit the old
    // billboard atmosphere and workbench hologram from the editable art sample.
    var atmosphere=source.transform.Find("Atmosphere and assembly area");if(atmosphere)Object.DestroyImmediate(atmosphere.gameObject);
    PrefabUtility.SaveAsPrefabAsset(source,Editable+"/Creek_Environment_Editable.prefab");
   }finally{PrefabUtility.UnloadPrefabContents(source);}
  }
  public static Bounds Bounds(IEnumerable<Renderer> items){var renderers=items.ToArray();var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;}
  // Rotated source AABBs contain empty corners; use the actual geometry so a
  // manually placed part rests on the ground and its XZ pivot is centred.
  static Bounds GeometryBounds(IEnumerable<MeshFilter> items) {
   bool first=true;var bounds=new Bounds();
   foreach(var filter in items)foreach(var vertex in filter.sharedMesh.vertices) {
    var point=filter.transform.TransformPoint(vertex);
    if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
   }
   return bounds;
  }
  static void Combine(MeshFilter[] source,Transform target,string id) {
   var batches=new Dictionary<Material,List<CombineInstance>>();
   foreach(var f in source)for(int i=0;i<f.sharedMesh.subMeshCount;i++) {
    var imported=f.GetComponent<Renderer>().sharedMaterials[i];var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+imported.name+".mat");if(!material)throw new Exception("Material missing: "+imported.name);
    if(!batches.TryGetValue(material,out var list))batches[material]=list=new List<CombineInstance>();
    list.Add(new CombineInstance{mesh=f.sharedMesh,subMeshIndex=i,transform=target.worldToLocalMatrix*f.transform.localToWorldMatrix});
   }
   foreach(var batch in batches.OrderBy(p=>p.Key.name)) {
    var mesh=new Mesh{name=id+"_"+batch.Key.name,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(batch.Value.ToArray());mesh.RecalculateBounds();
    string path=Meshes+"/"+mesh.name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);
    var part=new GameObject(batch.Key.name);part.transform.SetParent(target,false);part.AddComponent<MeshFilter>().sharedMesh=mesh;part.AddComponent<MeshRenderer>().sharedMaterial=batch.Key;
   }
  }
  static void BuildClouds() {
   for(int i=0;i<3;i++) {
    int materialIndex=new[]{1,4,8}[i];var go=new GameObject(new[]{"Cloud_Far","Cloud_Middle","Cloud_Near"}[i]);
    try {
     go.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Effects/CloudPuff "+(char)('A'+i)+".asset");
     var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=new[]{AssetDatabase.LoadAssetAtPath<Material>(Root+$"/Materials/Cloud puff {materialIndex:00}.mat"),AssetDatabase.LoadAssetAtPath<Material>(Root+$"/Materials/Cloud shell {materialIndex:00}.mat")};r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
     PrefabUtility.SaveAsPrefabAsset(go,CloudParts+"/"+go.name+".prefab");
    }finally{Object.DestroyImmediate(go);}
   }
  }
  static void UpgradeCloudNoise() {
   var old=AssetDatabase.LoadAssetAtPath<Texture3D>(Root+"/Textures/Clouds/CloudVolumeNoise.asset");if(old.mipmapCount>1)return;
   var filtered=new Texture3D(old.width,old.height,old.depth,old.format,true){name=old.name,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Repeat};
   filtered.SetPixelData(old.GetPixelData<byte>(0),0);filtered.Apply(true,false);EditorUtility.CopySerialized(filtered,old);Object.DestroyImmediate(filtered);EditorUtility.SetDirty(old);
   if(old.mipmapCount<2)throw new Exception("Cloud noise mip chain was not saved");
  }
  static void BuildGallery(List<GameObject> parts) {
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var root=new GameObject("Manual scenery kit - drag prefabs from Parts, Editable, Botanical or Modules").transform;
   for(int i=0;i<parts.Count;i++){var g=(GameObject)PrefabUtility.InstantiatePrefab(parts[i],root);g.transform.localPosition=new Vector3(i%8*1.0f,0,i/8*1.2f);}
   var combined=new GameObject("Complete editable arrangements").transform;
   for(int i=0;i<Themes.Length;i++){var g=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Editable+"/"+Themes[i]+"_Editable.prefab"),combined);g.transform.localPosition=new Vector3(i*1.25f,0,-1.5f);}
   var camera=new GameObject("Gallery camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=4.6f;camera.transform.position=new Vector3(6,9,-7);camera.transform.LookAt(new Vector3(3.4f,0,1.6f));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.17f,.2f);camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
   var sun=new GameObject("Gallery sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.3f;sun.transform.rotation=Quaternion.Euler(50,-30,0);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.6f,.65f,.7f);
   EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Gallery);
   var rt=new RenderTexture(1800,1200,24);rt.Create();var previous=RenderTexture.active;var image=new Texture2D(1800,1200,TextureFormat.RGB24,false);
   try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1800,1200),0,0);image.Apply();File.WriteAllBytes("Docs/CreekDiorama/Scenery-Parts-Gallery.png",image.EncodeToPNG());}
   finally{RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
  }
 }
}
