using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor {
 public static class SceneryMotionAuthoring {
  public const string Folder="Assets/CreekDiorama/AnimatedScenery";
  static readonly string[] Plants={"Pine_Branching","Tree_Round_Canopy","Fern_Rosette","Ivy_Cascade","Flowers_Daisies","Flowers_Lupin"};
  static readonly string[] Machines={"Wheelwright_Bench","Quarry_Winch","Riverside_Mooring"};
  static readonly Dictionary<string,Mesh> meshes=new();
  static readonly Dictionary<Material,Material> materials=new();
  sealed class Part {public string Name;public Vector3 Pivot,Axis;public float Speed,Amplitude,Frequency;public bool Rope;}
  static string Clean(string s)=>Regex.Replace(s,@"\.\d+$","");
  static string Point(Vector3 p)=>Mathf.RoundToInt(p.x*100000)+","+Mathf.RoundToInt(p.y*100000)+","+Mathf.RoundToInt(p.z*100000);
  static string Triangle(Vector3 a,Vector3 b,Vector3 c){var keys=new[]{Point(a),Point(b),Point(c)};Array.Sort(keys,StringComparer.Ordinal);return string.Join(";",keys);}
  public static float Phase(string name){uint h=2166136261;foreach(char c in name)h=(h^c)*16777619;return (h%10000)/10000f*Mathf.PI*2;}
  static bool Visible(Transform t,Transform root){while(t){if(!t.gameObject.activeSelf)return false;if(t==root)break;t=t.parent;}return true;}
  static Material WindMaterial(Material source){
   if(source.shader.name==SceneryWindClock.ShaderName)return source;
   if(materials.TryGetValue(source,out var saved))return saved;
   string key=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
   string path=Folder+"/Wind_"+key+".mat";
   saved=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(!saved){saved=new Material(source){name=source.name+" animated",shader=Shader.Find(SceneryWindClock.ShaderName)};
    if(source.shader.name=="Creek Diorama/Soft Foliage"){saved.SetFloat("_Cull",0);saved.SetFloat("_Metallic",0);}
    AssetDatabase.CreateAsset(saved,path);}
   materials[source]=saved;return saved;
  }
  static Mesh SaveMesh(Mesh mesh,string key){
   if(meshes.TryGetValue(key,out var saved)){Object.DestroyImmediate(mesh);return saved;}
   string path=Folder+"/"+Hash128.Compute(key)+".asset";
   saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(!saved){saved=mesh;AssetDatabase.CreateAsset(saved,path);}else Object.DestroyImmediate(mesh);
   meshes[key]=saved;return saved;
  }
  static string MeshKey(Mesh mesh)=>AssetDatabase.GetAssetPath(mesh)+"|"+mesh.name+"|"+GlobalObjectId.GetGlobalObjectIdSlow(mesh);
  static void ClearStatic(GameObject go){GameObjectUtility.SetStaticEditorFlags(go,0);}
  static Dictionary<string,Part> SourceParts(string theme){
   var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CreekDiorama/Models/CarSceneryProps.fbx");
   var root=model.GetComponentsInChildren<Transform>().Single(t=>t.name==theme);
   var filters=root.GetComponentsInChildren<MeshFilter>();
   var wheels=filters.Where(f=>Clean(f.name)=="Axle cap").Select(f=>f.transform.TransformPoint(f.sharedMesh.bounds.center)-root.position).ToArray();
   var definitions=new Dictionary<string,Part>();var triangles=new Dictionary<string,Part>();
   foreach(var f in filters){
    string name=Clean(f.name),group=null;var center=f.transform.TransformPoint(f.sharedMesh.bounds.center)-root.position;
    Vector3 pivot=Vector3.zero,axis=Vector3.forward;float speed=0,amplitude=0,frequency=0;bool rope=false;
    if(theme=="Wheelwright_Bench" && new[]{"Rubber tyre","Oak wheel rim","Axle cap","Wheel spoke"}.Contains(name)){
     int index=Enumerable.Range(0,wheels.Length).OrderBy(i=>(wheels[i]-center).sqrMagnitude).First();
     group="Wheel "+index;pivot=wheels[index];speed=12;
    }
    if(theme=="Quarry_Winch" && new[]{"Winch axle","Drum rim","Cable drum","Wound cable","Crank","Crank grip"}.Contains(name)){
     group="Winch drum and crank";pivot=new Vector3(0,.4f,0);axis=Vector3.right;amplitude=60;frequency=Mathf.PI/6;
    }
    if(theme=="Riverside_Mooring" && name=="Life ring"){
     group="Swinging life ring";pivot=center+Vector3.up*.081f;axis=Vector3.forward;amplitude=5;frequency=Mathf.PI/3;
    }
    if(theme=="Riverside_Mooring" && name=="Mooring rope"){
     group="Flexible mooring rope "+(center.z<0?"front":"back");rope=true;frequency=Mathf.PI*2/5;
    }
    if(group==null)continue;
    if(!definitions.TryGetValue(group,out var part))definitions[group]=part=new Part{Name=group,Pivot=pivot,Axis=axis,Speed=speed,Amplitude=amplitude,Frequency=frequency,Rope=rope};
    var vertices=f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v)-root.position).ToArray();var indices=f.sharedMesh.triangles;
    for(int i=0;i<indices.Length;i+=3)triangles[Triangle(vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]])]=part;
   }
   return triangles;
  }
  static Mesh Portion(Mesh source,List<int> triangles,Vector3 pivot){
   var used=triangles.Distinct().ToArray();var map=used.Select((v,i)=>(v,i)).ToDictionary(p=>p.v,p=>p.i);
   var mesh=new Mesh{name=source.name+" editable motion",indexFormat=source.indexFormat};
   var vertices=source.vertices;var normals=source.normals;var uv=source.uv;var tangents=source.tangents;
   mesh.vertices=used.Select(i=>vertices[i]-pivot).ToArray();
   if(normals.Length==vertices.Length)mesh.normals=used.Select(i=>normals[i]).ToArray();
   if(uv.Length==vertices.Length)mesh.uv=used.Select(i=>uv[i]).ToArray();
   if(tangents.Length==vertices.Length)mesh.tangents=used.Select(i=>tangents[i]).ToArray();
   var colors=source.colors;if(colors.Length==vertices.Length)mesh.colors=used.Select(i=>colors[i]).ToArray();
   for(int channel=1;channel<8;channel++){var values=new List<Vector4>();source.GetUVs(channel,values);if(values.Count==vertices.Length)mesh.SetUVs(channel,used.Select(i=>values[i]).ToList());}
   mesh.triangles=triangles.Select(i=>map[i]).ToArray();mesh.RecalculateBounds();return mesh;
  }
  // Midpoint subdivision preserves the rest surface, adding vertices so a rope can flex.
  static void Subdivide(Mesh mesh,int rounds){
   for(int round=0;round<rounds;round++){
    var positions=mesh.vertices.ToList();var normals=mesh.normals.ToList();var uv=mesh.uv.ToList();
    if(normals.Count!=positions.Count)normals=positions.Select(p=>Vector3.up).ToList();
    if(uv.Count!=positions.Count)uv=positions.Select(p=>Vector2.zero).ToList();
    var edges=new Dictionary<(int,int),int>();
    int Mid(int a,int b){var edge=a<b?(a,b):(b,a);if(edges.TryGetValue(edge,out int i))return i;i=positions.Count;edges[edge]=i;
     positions.Add((positions[a]+positions[b])*.5f);normals.Add((normals[a]+normals[b]).normalized);uv.Add((uv[a]+uv[b])*.5f);return i;}
    var result=new List<int>();var old=mesh.triangles;
    for(int i=0;i<old.Length;i+=3){int a=old[i],b=old[i+1],c=old[i+2],ab=Mid(a,b),bc=Mid(b,c),ca=Mid(c,a);result.AddRange(new[]{a,ab,ca,ab,b,bc,ca,bc,c,ab,bc,ca});}
    mesh.Clear();mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(result,0);
   }
   mesh.RecalculateBounds();
  }
  public static int AnimateMachine(Transform root,string theme){
   // Unpowered workshop displays and idle winches stay in their authored rest pose.
   if(theme=="Wheelwright_Bench" || theme=="Quarry_Winch")return 0;
   if(root.GetComponent<SceneryMotionAuthored>())return 0;
   var lookup=SourceParts(theme);int changed=0;
   foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true).ToArray()){
    var source=filter.sharedMesh;var renderer=filter.GetComponent<MeshRenderer>();if(!source || !renderer || source.subMeshCount!=1)continue;
    var vertices=source.vertices;var indices=source.triangles;
    var toRoot=root.worldToLocalMatrix*filter.transform.localToWorldMatrix;
    var mapped=vertices.Select(toRoot.MultiplyPoint3x4).ToArray();
    var groups=new Dictionary<Part,List<int>>();var remaining=new List<int>();
    for(int i=0;i<indices.Length;i+=3){
     if(lookup.TryGetValue(Triangle(mapped[indices[i]],mapped[indices[i+1]],mapped[indices[i+2]]),out var part)){
      if(!groups.TryGetValue(part,out var selected))groups[part]=selected=new List<int>();selected.AddRange(new[]{indices[i],indices[i+1],indices[i+2]});
     }else remaining.AddRange(new[]{indices[i],indices[i+1],indices[i+2]});
    }
    foreach(var pair in groups){
     var part=pair.Key;var pivot=toRoot.inverse.MultiplyPoint3x4(part.Pivot);
     var go=new GameObject(part.Name);go.transform.SetParent(filter.transform,false);go.transform.localPosition=pivot;
     var mesh=Portion(source,pair.Value,pivot);string key=MeshKey(source)+"|"+part.Name+"|"+pivot.ToString("R");
     if(part.Rope){
      Subdivide(mesh,3);var b=mesh.bounds;float span=b.size.x;
      mesh.SetUVs(3,mesh.vertices.Select(v=>new Vector4(0,0,part.Frequency,Phase(root.name+part.Name))).ToList());
      mesh.SetUVs(4,mesh.vertices.Select(v=>new Vector4(1,b.min.x,span,span*.02f)).ToList());b.Expand(span*.04f);mesh.bounds=b;
     }
     go.AddComponent<MeshFilter>().sharedMesh=SaveMesh(mesh,key);
     var r=go.AddComponent<MeshRenderer>();EditorUtility.CopySerialized(renderer,r);
     if(part.Rope)r.sharedMaterials=renderer.sharedMaterials.Select(WindMaterial).ToArray();
     else {var breeze=go.AddComponent<CarSceneryBreeze>();breeze.Axis=toRoot.inverse.MultiplyVector(part.Axis).normalized;breeze.DegreesPerSecond=part.Speed;breeze.SwayDegrees=part.Amplitude;breeze.Frequency=part.Frequency;
      // Split materials of the same machine must share a phase.
      breeze.Phase=Phase(AnimationUtility.CalculateTransformPath(root,null)+part.Name);}
     ClearStatic(go);changed++;
    }
    if(groups.Count>0){filter.sharedMesh=SaveMesh(Portion(source,remaining,Vector3.zero),MeshKey(source)+"|static remainder");EditorUtility.SetDirty(filter);}
   }
   if(changed>0)root.gameObject.AddComponent<SceneryMotionAuthored>();
   return changed;
  }
  static bool IsPlant(Transform t)=>Plants.Contains(t.name.Replace("_Editable",""));
  static Vector4 Wind(Transform plant,Transform meshSpace){
   float degrees=plant.name.Contains("Ivy")?4:plant.name.Contains("Pine")||plant.name.Contains("Tree")?2:3;
   float phase=Phase(AnimationUtility.CalculateTransformPath(plant,null));
   return new Vector4(meshSpace.InverseTransformPoint(plant.position).y,Mathf.Tan(degrees*Mathf.Deg2Rad),Mathf.PI*2/(4+phase/Mathf.PI*1.5f),phase);
  }
  static void ApplyWind(MeshFilter filter,List<Vector4> data){
   if(!data.Any(v=>v.y>0))return;
   var source=filter.sharedMesh;var mesh=Object.Instantiate(source);
   mesh.name=source.name+" wind";mesh.SetUVs(3,data);
   // Missing vertex attributes are not guaranteed to read as zero on every GPU.
   // Explicitly populate the rope channel so plants cannot take the rope branch.
   mesh.SetUVs(4,Enumerable.Repeat(Vector4.zero,mesh.vertexCount).ToList());
   UpdateMotionBounds(mesh);
   string key=MeshKey(source)+"|wind|"+Hash128.Compute(string.Join(";",data.Select(d=>d.ToString("R"))));
   filter.sharedMesh=SaveMesh(mesh,key);var renderer=filter.GetComponent<Renderer>();renderer.sharedMaterials=renderer.sharedMaterials.Select(WindMaterial).ToArray();
   ClearStatic(filter.gameObject);EditorUtility.SetDirty(filter);EditorUtility.SetDirty(renderer);
  }
  public static void UpdateMotionBounds(Mesh mesh){
   var data=new List<Vector4>();mesh.GetUVs(3,data);if(data.Count!=mesh.vertexCount||!data.Any(v=>v.y>0))return;
   var vertices=mesh.vertices;var bounds=new Bounds(vertices[0],Vector3.zero);
   for(int i=0;i<vertices.Length;i++){
    float amount=Mathf.Abs(vertices[i].y-data[i].x)*data[i].y;
    var extent=new Vector3(amount,0,amount*.35f);
    bounds.Encapsulate(vertices[i]-extent);bounds.Encapsulate(vertices[i]+extent);
   }
   mesh.bounds=bounds;
  }
  public static int AnimatePlants(Transform environment){
   int changed=0;var plants=environment.GetComponentsInChildren<Transform>(true).Where(IsPlant).ToArray();
   var hidden=new Dictionary<string,(Transform plant,Vector3 world)>();
   foreach(var plant in plants){
    bool visible=Visible(plant,environment);
    foreach(var filter in plant.GetComponentsInChildren<MeshFilter>(true)){
     if(!filter.sharedMesh)continue;
     if(visible){
      var existing=new List<Vector4>();filter.sharedMesh.GetUVs(3,existing);if(existing.Any(v=>v.y>0))continue;
      var wind=Wind(plant,filter.transform);ApplyWind(filter,filter.sharedMesh.vertices.Select(v=>wind).ToList());changed++;
     }else foreach(var vertex in filter.sharedMesh.vertices){var world=filter.transform.TransformPoint(vertex);hidden[Point(world)]=(plant,world);}
    }
   }
   // Existing botanical batches retain their layout and draw batching. Transfer
   // only wind anchors from matching hidden source placements, never their geometry.
   foreach(var filter in environment.GetComponentsInChildren<MeshFilter>(true)){
    if(!Visible(filter.transform,environment)||filter.GetComponentsInParent<Transform>().Any(IsPlant)||!filter.sharedMesh)continue;
    var renderer=filter.GetComponent<Renderer>();if(!renderer || !renderer.sharedMaterials.Any(m=>m && m.name.StartsWith("Botanical")))continue;
    var old=new List<Vector4>();filter.sharedMesh.GetUVs(3,old);if(old.Any(v=>v.y>0))continue;
    var data=filter.sharedMesh.vertices.Select(v=>hidden.TryGetValue(Point(filter.transform.TransformPoint(v)),out var match)?Wind(match.plant,filter.transform):Vector4.zero).ToList();
    if(data.Any(v=>v.y>0)){ApplyWind(filter,data);changed++;}
   }
   return changed;
  }
  public static string ApplyLevel(SandboxFactoryLevel factory){
   SceneryMotionPreview.Stop();SandboxTrackBaker.EnsureFolder(Folder);
   var env=factory.Layout.Course.SandboxFrame.Find("Environment");if(!env)throw new InvalidOperationException("Missing environment");
   if(!env.GetComponent<SceneryWindSettings>())env.gameObject.AddComponent<SceneryWindSettings>();
   int machine=0;foreach(var t in env.GetComponentsInChildren<Transform>(true).ToArray())if(Machines.Contains(t.name)&&Visible(t,env))machine+=AnimateMachine(t,t.name);
   int plants=AnimatePlants(env);
   LimitNewWindOverlap(factory);
   foreach(var breeze in env.GetComponentsInChildren<CarSceneryBreeze>(true)){
    if(breeze.name=="Breeze pennant")breeze.Phase=Phase(AnimationUtility.CalculateTransformPath(breeze.transform,null));
    if(breeze.name=="Windmill rotor"){breeze.enabled=true;breeze.DegreesPerSecond=24;ClearStatic(breeze.gameObject);foreach(var r in breeze.GetComponentsInChildren<Renderer>(true))ClearStatic(r.gameObject);}
    EditorUtility.SetDirty(breeze);
   }
   EditorSceneManager.MarkSceneDirty(factory.gameObject.scene);
   return factory.Title+": "+machine+" moving mesh pieces; "+plants+" wind meshes";
  }
  public static int LimitNewWindOverlap(SandboxFactoryLevel factory){
   int changed=0;var lanes=CarSceneryClearance.Lanes(factory);
   var env=factory.Layout.Course.SandboxFrame.Find("Environment");
   foreach(var plant in env.GetComponentsInChildren<Transform>(true).Where(t=>IsPlant(t)&&Visible(t,env))){
    var filters=plant.GetComponentsInChildren<MeshFilter>(true);
    bool Hits(MeshFilter f,Bounds bounds)=>CarSceneryClearance.Overlaps(CarSceneryClearance.TransformBounds(bounds,f.transform.localToWorldMatrix),lanes);
    Bounds Rest(Mesh mesh){var v=mesh.vertices;var b=new Bounds(v[0],Vector3.zero);foreach(var p in v)b.Encapsulate(p);return b;}
    if(filters.Any(f=>Hits(f,Rest(f.sharedMesh))) || !filters.Any(f=>Hits(f,f.sharedMesh.bounds)))continue;
    var copies=filters.Select(f=>Object.Instantiate(f.sharedMesh)).ToArray();
    int step=0;
    while(filters.Where((f,i)=>Hits(f,copies[i].bounds)).Any() && step++<12){
     foreach(var mesh in copies){var data=new List<Vector4>();mesh.GetUVs(3,data);for(int i=0;i<data.Count;i++){var d=data[i];d.y*=.5f;data[i]=d;}mesh.SetUVs(3,data);UpdateMotionBounds(mesh);}
    }
    if(filters.Where((f,i)=>Hits(f,copies[i].bounds)).Any()){foreach(var mesh in copies)Object.DestroyImmediate(mesh);throw new InvalidOperationException("Cannot keep plant wind outside the road: "+plant.name);}
    for(int i=0;i<filters.Length;i++){filters[i].sharedMesh=SaveMesh(copies[i],MeshKey(filters[i].sharedMesh)+"|clearance limit|"+step);EditorUtility.SetDirty(filters[i]);}
    changed++;
   }
   return changed;
  }
  public static string ConvertLevel(int number){
   if(Application.isPlaying)throw new InvalidOperationException("Stop Play first");
   string path="Assets/MetaHackthone/Scenes/Level"+number+".unity";
   var active=SceneManager.GetActiveScene();var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;
   bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
   try{
    if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
    System.IO.Directory.CreateDirectory("Logs/SceneryMotion/Before");
    string backup="Logs/SceneryMotion/Before/Level"+number+".unity";
    if(!System.IO.File.Exists(backup))EditorSceneManager.SaveScene(scene,backup,true);
    string prefab="Assets/MetaHackthone/Generated/Campaign/Level"+number+".prefab";
    if(!System.IO.File.Exists("Logs/SceneryMotion/Before/Level"+number+".prefab"))System.IO.File.Copy(prefab,"Logs/SceneryMotion/Before/Level"+number+".prefab");
    var factory=SandboxFactoryAuthoring.Find(scene);var snapshot=CarSceneryAuthoring.GameplaySnapshot(factory);
    string report=ApplyLevel(factory);
    if(snapshot!=CarSceneryAuthoring.GameplaySnapshot(factory))throw new InvalidOperationException("Gameplay objects changed");
    AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Scene save failed");
    return report;
   }finally{if(opened && scene.IsValid())EditorSceneManager.CloseScene(scene,true);if(active.IsValid())SceneManager.SetActiveScene(active);SandboxTrackEditing.Suspended=suspended;}
  }
 }
}
