using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor {
 public static class SceneryAAAuthoring {
  const string Output="Assets/CreekDiorama/Meshes/FilteredScenery";
  const string Resource="Assets/MetaHackthone/Resources/SceneryDetailCatalog.asset";
  sealed class Piece {public int[] Indices;public Bounds Bounds;public int[] Vertices;}
  static string[] SourcePaths()=>Enumerable.Range(1,5).Select(i=>$"Assets/MetaHackthone/Generated/Campaign/Level{i}.prefab")
   .Concat(new[]{"Assets/CreekDiorama/Prefabs/BeautifulCorner.prefab","Assets/CreekDiorama/Prefabs/CarScenery/Editable/Creek_Environment_Editable.prefab"})
   .Concat(AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/CreekDiorama/Prefabs/Botanical"}).Select(AssetDatabase.GUIDToAssetPath)).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
  public static string Fingerprint() {
   // Dependency hashes catch edits to existing meshes as well as replacement GUIDs.
   var text=new StringBuilder("scenery-lod-flowers-v1\n");
   text.Append(AssetDatabase.GetAssetDependencyHash("Assets/MetaHackthone/Editor/SceneryAAAuthoring.cs")).Append('\n');
   foreach(var path in SourcePaths())text.Append(path).Append(':').Append(AssetDatabase.GetAssetDependencyHash(path)).Append('\n');
   return Hash128.Compute(text.ToString()).ToString();
  }
  public static void EnsureCurrent() {
   var catalog=AssetDatabase.LoadAssetAtPath<SceneryDetailCatalog>(Resource);
   if(!catalog || catalog.AuthoringFingerprint!=Fingerprint() || catalog.Foliage.Length==0 || catalog.Foliage.Any(e=>e==null || !e.Source || !e.Filtered))Build();
  }
  public static bool IsDetailMaterial(Material material)=>material &&
   (material.name.StartsWith("Botanical leaf",StringComparison.Ordinal) || material.name.StartsWith("Botanical pollen",StringComparison.Ordinal) ||
    material.name.StartsWith("Botanical ivory",StringComparison.Ordinal) || material.name.StartsWith("Botanical lavender",StringComparison.Ordinal));
  [MenuItem("MetaHackthone/Creek Art/Rebuild foliage and base anti-aliasing LODs")]
  public static void Build() {
   Directory.CreateDirectory(Output);Directory.CreateDirectory("Logs/SceneryAA");AssetDatabase.Refresh();
   var catalog=AssetDatabase.LoadAssetAtPath<SceneryDetailCatalog>(Resource);
   if(!catalog){catalog=ScriptableObject.CreateInstance<SceneryDetailCatalog>();AssetDatabase.CreateAsset(catalog,Resource);}
   var foliage=new Dictionary<Mesh,SceneryDetailCatalog.Entry>();var bases=new Dictionary<Mesh,SceneryDetailCatalog.BaseEntry>();
   var report=new StringBuilder("Visual LODs; source meshes/colliders unchanged. Leaf LODs are opaque geometry, not alpha dithering.\n");
   var paths=SourcePaths();
   bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
   try {foreach(var path in paths) {
    var root=PrefabUtility.LoadPrefabContents(path);
    try {
     var renderers=root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).ToArray();
     foreach(var r in renderers) {
      if(!IsDetailMaterial(r.sharedMaterial))continue;
      var filter=r.GetComponent<MeshFilter>();if(!filter || !filter.sharedMesh || foliage.ContainsKey(filter.sharedMesh))continue;
      var mesh=filter.sharedMesh;string identity=AssetDatabase.GetAssetPath(mesh);
      if(mesh.subMeshCount!=1 || r.sharedMaterials.Length!=1)continue;
      bool tree=identity.Contains("Tree_Round")||identity.Contains("Pine_Branch")||identity.Contains("Shrub_Broadleaf")||r.transform.parent.name=="Layered canopy";
      Mesh filtered;int oldPieces,newPieces;
      if(r.sharedMaterial.name.StartsWith("Botanical pollen")) { filtered=FilterFlowerCentres(mesh);oldPieces=newPieces=0; }
      else if(r.sharedMaterial.name.StartsWith("Botanical leaf")) filtered=FilterLeaves(mesh,tree,out oldPieces,out newPieces);
      else { filtered=Select(mesh,Pieces(mesh));oldPieces=newPieces=0; }
      // Preserve every petal's six-sided outline, but remove its raised centre fan.
      // Run after leaf thinning, so broad fern leaves also lose unnecessary internal faces.
      if(!r.sharedMaterial.name.StartsWith("Botanical pollen")) {
       var simple=SimplifyLeafFans(filtered);Object.DestroyImmediate(filtered);filtered=simple;
      }
      if(filtered.GetIndexCount(0)>=mesh.GetIndexCount(0)){Object.DestroyImmediate(filtered);continue;}
      var cullingBounds=filtered.bounds;cullingBounds.Encapsulate(mesh.bounds);filtered.bounds=cullingBounds;
      AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh,out string guid,out long localId);
      string id=guid+"-"+localId;
      filtered=Save(filtered,Output+"/Foliage-"+id+".asset");
      foliage[mesh]=new SceneryDetailCatalog.Entry{Source=mesh,Filtered=filtered,ReferenceSize=tree?.08f:.04f};
      report.AppendLine($"FOLIAGE {identity} tree={tree} pieces={oldPieces}->{newPieces} triangles={mesh.triangles.Length/3}->{filtered.triangles.Length/3}");
     }
     foreach(var parent in renderers.Where(r=>r.transform.parent && r.transform.parent.name=="01 Display base").Select(r=>r.transform.parent).Distinct()) {
      var steel=parent.Find("Blue steel edge")?.GetComponent<MeshFilter>();if(!steel || bases.ContainsKey(steel.sharedMesh))continue;
      bases[steel.sharedMesh]=BuildBase(parent,report);
     }
     // Native hand-placement prefabs carry their own switch; existing baked
     // campaign batches are handled by the factory's visual-only component.
     if(path.Contains("/Botanical/")||path.EndsWith("Creek_Environment_Editable.prefab")) {
      bool supported=renderers.Any(r=>{var f=r.GetComponent<MeshFilter>();return f && foliage.ContainsKey(f.sharedMesh);});
      var installed=root.GetComponent<SceneryDetailLod>();
      if(supported && !installed){root.AddComponent<SceneryDetailLod>();PrefabUtility.SaveAsPrefabAsset(root,path);}
      if(!supported && installed){Object.DestroyImmediate(installed);PrefabUtility.SaveAsPrefabAsset(root,path);}
     }
    }finally{PrefabUtility.UnloadPrefabContents(root);}
   }}finally{SandboxTrackEditing.Suspended=suspended;}
   catalog.Foliage=foliage.Values.ToArray();catalog.Bases=bases.Values.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
   // Keep previous generated assets: an artist may still reference them in a scene.
   // The catalog is the authoritative runtime set; unused assets need no destructive cleanup.
   catalog.AuthoringFingerprint=Fingerprint();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
   File.WriteAllText("Logs/SceneryAA/Build.txt",report.ToString());
   foreach(var shader in new[]{"Creek Diorama/Filtered Display Base","Creek Diorama/Soft Foliage"})if(ShaderUtil.ShaderHasError(Shader.Find(shader)))throw new Exception("Shader errors: "+shader);
   Debug.Log($"SCENERY_AA_BUILT: {foliage.Count} foliage alternatives, {bases.Count} base alternatives.");
  }
  static Mesh Save(Mesh mesh,string path) {
   var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(mesh,old);old.UploadMeshData(false);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}
   AssetDatabase.CreateAsset(mesh,path);return mesh;
  }
  internal static Bounds[] TrayRimCaps(Mesh mesh)=>Pieces(mesh).Where(p=>p.Bounds.min.y>.15f && p.Bounds.max.y>.16f).Select(p=>p.Bounds).ToArray();
  static List<Piece> Pieces(Mesh mesh) {
   var vertices=mesh.vertices;var indices=mesh.triangles;var parents=Enumerable.Range(0,vertices.Length).ToArray();
   int Root(int x){while(parents[x]!=x){parents[x]=parents[parents[x]];x=parents[x];}return x;}
   void Join(int a,int b){parents[Root(a)]=Root(b);}
   var weld=new Dictionary<Vector3Int,int>();
   for(int i=0;i<vertices.Length;i++) {
    var p=vertices[i];var key=new Vector3Int(Mathf.RoundToInt(p.x*100000),Mathf.RoundToInt(p.y*100000),Mathf.RoundToInt(p.z*100000));
    if(weld.TryGetValue(key,out int old))Join(i,old);else weld[key]=i;
   }
   for(int i=0;i<indices.Length;i+=3){Join(indices[i],indices[i+1]);Join(indices[i],indices[i+2]);}
   var parts=new Dictionary<int,List<int>>();
   for(int i=0;i<indices.Length;i+=3){int key=Root(indices[i]);if(!parts.TryGetValue(key,out var list))parts[key]=list=new List<int>();list.Add(indices[i]);list.Add(indices[i+1]);list.Add(indices[i+2]);}
   return parts.Values.Select(list=>{var unique=list.Distinct().ToArray();var bounds=new Bounds(vertices[unique[0]],Vector3.zero);foreach(int i in unique)bounds.Encapsulate(vertices[i]);return new Piece{Indices=list.ToArray(),Vertices=unique,Bounds=bounds};}).ToList();
  }
  static Mesh Select(Mesh source,IEnumerable<Piece> pieces,Vector3[] moved=null) {
   var sourceVertices=moved??source.vertices;var sourceNormals=source.normals;var sourceUv=source.uv;var sourceTangents=source.tangents;var sourceColors=source.colors;
   var indices=new List<int>();var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var tangents=new List<Vector4>();var colors=new List<Color>();var map=new Dictionary<int,int>();
   var used=new List<int>();
   foreach(var piece in pieces)foreach(int index in piece.Indices) {
    if(!map.TryGetValue(index,out int next)){next=vertices.Count;map[index]=next;used.Add(index);vertices.Add(sourceVertices[index]);normals.Add(sourceNormals.Length==sourceVertices.Length?sourceNormals[index]:Vector3.up);uv.Add(sourceUv.Length==sourceVertices.Length?sourceUv[index]:Vector2.zero);if(sourceTangents.Length==sourceVertices.Length)tangents.Add(sourceTangents[index]);if(sourceColors.Length==sourceVertices.Length)colors.Add(sourceColors[index]);}
    indices.Add(next);
   }
   var mesh=new Mesh{name=source.name+" filtered",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);if(tangents.Count==vertices.Count)mesh.SetTangents(tangents);if(colors.Count==vertices.Count)mesh.SetColors(colors);
   CopyExtraUVs(source,mesh,used);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
  }
  static void CopyExtraUVs(Mesh source,Mesh target,List<int> sourceIndices) {
   // Wind anchors/phase live in UV3/UV4, not in the material. Dropping these
   // channels freezes distant plants and makes them jump when their LOD changes.
   for(int channel=1;channel<8;channel++) {var values=new List<Vector4>();source.GetUVs(channel,values);if(values.Count==source.vertexCount)target.SetUVs(channel,sourceIndices.Select(i=>values[i]).ToList());}
  }
  // Each authored flower centre is a bevelled box. Keep its volume and location,
  // replacing 84 triangles with twelve; never alter the detailed source mesh.
  public static Mesh FilterFlowerCentres(Mesh source) {
   var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();var used=new List<int>();var original=source.vertices;
   foreach(var piece in Pieces(source)) {
    var min=piece.Bounds.min;var max=piece.Bounds.max;
    var corners=new[]{new Vector3(min.x,min.y,min.z),new Vector3(max.x,min.y,min.z),new Vector3(max.x,max.y,min.z),new Vector3(min.x,max.y,min.z),new Vector3(min.x,min.y,max.z),new Vector3(max.x,min.y,max.z),new Vector3(max.x,max.y,max.z),new Vector3(min.x,max.y,max.z)};
    int[] faces={0,3,2,1,4,5,6,7,0,4,7,3,1,2,6,5,0,1,5,4,3,7,6,2};
    for(int face=0;face<6;face++) {
     int start=vertices.Count;var normal=Vector3.Cross(corners[faces[face*4+1]]-corners[faces[face*4]],corners[faces[face*4+2]]-corners[faces[face*4]]).normalized;
     for(int v=0;v<4;v++){var point=corners[faces[face*4+v]];vertices.Add(point);normals.Add(normal);uv.Add(new Vector2(v==1||v==2?1:0,v>=2?1:0));int nearest=piece.Vertices[0];foreach(int index in piece.Vertices)if((original[index]-point).sqrMagnitude<(original[nearest]-point).sqrMagnitude)nearest=index;used.Add(nearest);}
     triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
    }
   }
   var mesh=new Mesh{name=source.name+" simple centres",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);CopyExtraUVs(source,mesh,used);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
  }
  public static Mesh SimplifyLeafFans(Mesh source) {
   var vertices=source.vertices;var indices=source.triangles;var result=new List<int>();
   // Validate each six-triangle fan before replacing it. Unknown/imported topology
   // passes through intact, including stems, branches and broad canopy volumes.
   bool Same(int a,int b)=>(vertices[a]-vertices[b]).sqrMagnitude<1e-12f;
   for(int offset=0;offset<indices.Length;) {
    List<int> ring=null;
    if(offset+18<=indices.Length)for(int candidate=0;candidate<3 && ring==null;candidate++) {
     int centre=indices[offset+candidate];var edges=new List<(int a,int b)>();
     for(int f=0;f<6;f++) {
      int pos=-1;for(int v=0;v<3;v++)if(Same(indices[offset+f*3+v],centre))pos=v;
      if(pos<0)break;
      edges.Add((indices[offset+f*3+(pos+1)%3],indices[offset+f*3+(pos+2)%3]));
     }
     if(edges.Count!=6)continue;
     var ordered=new List<int>{edges[0].a};int end=edges[0].b;edges.RemoveAt(0);
     while(edges.Count>0) {int next=edges.FindIndex(e=>Same(e.a,end));if(next<0)break;ordered.Add(end);end=edges[next].b;edges.RemoveAt(next);}
     if(edges.Count==0 && ordered.Count==6 && Same(end,ordered[0]) && ordered.Select(i=>vertices[i]).Distinct().Count()==6)ring=ordered;
    }
    if(ring!=null){for(int v=1;v<5;v++)result.AddRange(new[]{ring[0],ring[v],ring[v+1]});offset+=18;}
    else {result.Add(indices[offset]);result.Add(indices[offset+1]);result.Add(indices[offset+2]);offset+=3;}
   }
   return Select(source,new[]{new Piece{Indices=result.ToArray()}});
  }
  static Mesh FilterLeaves(Mesh source,bool tree,out int before,out int after) {
   var pieces=Pieces(source);var vertices=source.vertices;var keep=new List<Piece>();int thin=0;before=pieces.Count;
   foreach(var p in pieces) {
    // Broad canopy volumes stay. Keep a sparse set of needles in each material
    // so the sunlit colour does not disappear when an entire batch changes LOD.
    if(tree){if(p.Indices.Length>36 || thin++%6==0)keep.Add(p);continue;}
    if(p.Indices.Length>24){keep.Add(p);continue;}
    // Estimate the leaf's long axis from its furthest pair. Thin blades become
    // fewer broader blades. Broad ivy/fern leaves retain their original form.
    Vector3 axis=Vector3.up;float longest=0;
    foreach(int a in p.Vertices)foreach(int b in p.Vertices){var delta=vertices[b]-vertices[a];if(delta.sqrMagnitude>longest){longest=delta.sqrMagnitude;axis=delta.normalized;}}
    float width=0;var centre=p.Bounds.center;
    foreach(int index in p.Vertices){var delta=vertices[index]-centre;width=Mathf.Max(width,(delta-axis*Vector3.Dot(delta,axis)).magnitude*2);}
    if(width>.014f){keep.Add(p);continue;}
    if(thin++%3!=0)continue;
    foreach(int index in p.Vertices){var delta=vertices[index]-centre;var along=axis*Vector3.Dot(delta,axis);vertices[index]=centre+along*.94f+(delta-along)*2.5f;}
    keep.Add(p);
   }
   after=keep.Count;return Select(source,keep,vertices);
  }
  static SceneryDetailCatalog.BaseEntry BuildBase(Transform parent,StringBuilder report) {
   Mesh Get(string name)=>parent.Find(name).GetComponent<MeshFilter>().sharedMesh;
   Material Mat(string name)=>parent.Find(name).GetComponent<MeshRenderer>().sharedMaterial;
   var basalt=Get("Basalt enamel");var steel=Get("Blue steel edge");var inset=Get("Deep shadow");
   var body=Pieces(basalt).OrderByDescending(p=>p.Bounds.size.x).First();float unit=body.Bounds.size.x/14.3f;
   var verts=basalt.vertices;float top=body.Bounds.max.y;
   string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(steel));
   var repaired=(Vector3[])verts.Clone();
   foreach(int index in body.Vertices)if(Mathf.Abs(repaired[index].y-top)<.00001f)repaired[index].y=unit*.69f;
   var near=Save(Select(basalt,Pieces(basalt),repaired),Output+"/Base-near-"+id+".asset");
   foreach(int index in body.Vertices)if(Mathf.Abs(verts[index].y-top)<.00001f)verts[index].y=unit*.83f;
   var facade=Save(Select(basalt,new[]{body},verts),Output+"/Base-"+id+".asset");
   var caps=Pieces(steel).Where(p=>p.Bounds.min.y>unit*.78f).ToArray();
   if(caps.Length<4)throw new Exception("Could not identify original rim caps: "+steel.name);
   var rim=Save(Select(steel,caps),Output+"/Rim-"+id+".asset");
   var floor=Save(Select(inset,Pieces(inset).Where(p=>p.Bounds.size.x>unit*5)),Output+"/Inset-"+id+".asset");
   string materialPath=Output+"/Base-"+id+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
   if(!material){material=new Material(Shader.Find("Creek Diorama/Filtered Display Base"));AssetDatabase.CreateAsset(material,materialPath);}
   material.SetFloat("_Unit",unit);material.SetColor("_BaseColor",Mat("Basalt enamel").GetColor("_BaseColor"));material.SetColor("_Copper",Mat("Satin copper").GetColor("_BaseColor"));material.SetColor("_Steel",Mat("Blue steel edge").GetColor("_BaseColor"));material.SetColor("_Dark",Mat("Deep shadow").GetColor("_BaseColor"));material.SetColor("_Amber",Mat("Amber lamps").GetColor("_BaseColor"));material.enableInstancing=true;EditorUtility.SetDirty(material);
   var rimPath=Output+"/Rim-"+id+".mat";var rimMat=AssetDatabase.LoadAssetAtPath<Material>(rimPath);
   if(!rimMat){rimMat=new Material(Mat("Blue steel edge"));AssetDatabase.CreateAsset(rimMat,rimPath);}
   rimMat.SetFloat("_Smoothness",.16f);rimMat.SetFloat("_Metallic",.2f);EditorUtility.SetDirty(rimMat);
   report.AppendLine($"BASE {id} unit={unit:R} original={parent.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3)} far={(facade.triangles.Length+rim.triangles.Length+floor.triangles.Length)/3}");
   return new SceneryDetailCatalog.BaseEntry{SteelSource=steel,BasaltSource=basalt,RepairedBasalt=near,Facade=facade,Rim=rim,Inset=floor,FacadeMaterial=material,RimMaterial=rimMat,InsetMaterial=Mat("Deep shadow"),ReferenceSize=unit*2.5f};
  }
  public static void Inspect() {
   Directory.CreateDirectory("Logs/SceneryAA");var report=new StringBuilder();
   bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
   try {
    var root=PrefabUtility.LoadPrefabContents("Assets/MetaHackthone/Generated/Campaign/Level1.prefab");
    try {
     foreach(var r in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled)) {
      string path=r.name;for(var p=r.transform.parent;p && p!=root.transform;p=p.parent)path=p.name+"/"+path;
      var filter=r.GetComponent<MeshFilter>();var mesh=filter?filter.sharedMesh:null;
      report.AppendLine($"{path}|bounds={r.bounds}|scale={r.transform.lossyScale}|mesh={AssetDatabase.GetAssetPath(mesh)}|mats={string.Join(";",r.sharedMaterials.Select(m=>m?m.name:"null"))}");
      if(mesh && path.Contains("01 Display base")) {
       report.AppendLine("LOCAL "+mesh.bounds+" verts="+mesh.vertexCount+" triangles="+mesh.triangles.Length/3);
       File.WriteAllText("Logs/SceneryAA/Base-"+r.name+".json",JsonUtility.ToJson(new MeshDump{vertices=mesh.vertices,triangles=mesh.triangles,normals=mesh.normals}));
      }
     }
    }finally{PrefabUtility.UnloadPrefabContents(root);}
   }finally{SandboxTrackEditing.Suspended=suspended;}
   File.WriteAllText("Logs/SceneryAA/Inspect.txt",report.ToString());
  }
  [Serializable] public class MeshDump {public Vector3[] vertices,normals;public int[] triangles;}
 }
}
