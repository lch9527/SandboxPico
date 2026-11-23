using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace CreekDiorama.Editor {
// Replaces the head-aligned cloud cards in the campaign levels with world-fixed cloud meshes:
// an opaque vertex-animated body plus a soft haze shell. The cards stay in the hierarchy,
// inactive, so the change can be reverted with Restore.
public static class CreekCloudPuffAuthoring {
const string R="Assets/CreekDiorama";
public const string BodyShader="Creek Diorama/Cloud Puff",ShellShader="Creek Diorama/Cloud Shell";
public const string NoisePath=R+"/Textures/Clouds/CloudVolumeNoise.asset";
public const string Bank="Layered realistic cloud banks";
public const string CardPrefix="Cloud layer ",PuffPrefix="Cloud puff ",LegacyVolumePrefix="Cloud volume ";
const int N=64,Variants=4;
static readonly string[] Levels={"Level1","Level2","Level3","Level4"};

[MenuItem("MetaHackthone/Creek Art/Convert clouds to mesh puffs")]
public static void Apply()=>Run(true);
[MenuItem("MetaHackthone/Creek Art/Restore billboard clouds")]
public static void Restore()=>Run(false);

static void Run(bool puffs) {
 Material[] bodies=null,shells=null;Mesh[] meshes=null;
 if(puffs){var noise=Noise();meshes=Enumerable.Range(0,Variants).Select(Puff).ToArray();Materials(noise,out bodies,out shells);}
 AssetDatabase.SaveAssets();
 // Material and mesh tuning applies immediately; scenes and prefabs can only be rewritten outside play mode.
 if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.LogWarning("Cloud puffs: assets updated. Leave play mode and run again to rewrite the level scenes and prefabs.");return;}
 foreach(var level in Levels) {
  string scenePath="Assets/MetaHackthone/Scenes/"+level+".unity";
  var scene=SceneManager.GetSceneByPath(scenePath);bool opened=!scene.isLoaded;
  if(opened)scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
  int count=scene.GetRootGameObjects().Sum(g=>Convert(g.transform,meshes,bodies,shells,puffs));
  if(count>0){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
  if(opened)EditorSceneManager.CloseScene(scene,true);
  string prefabPath="Assets/MetaHackthone/Generated/Campaign/"+level+".prefab";
  var contents=PrefabUtility.LoadPrefabContents(prefabPath);
  try {
   int inPrefab=Convert(contents.transform,meshes,bodies,shells,puffs);
   if(inPrefab>0)PrefabUtility.SaveAsPrefabAsset(contents,prefabPath);
   Debug.Log($"Cloud puffs: {level} scene {count}, prefab {inPrefab} ({(puffs?"mesh":"billboard")})");
  } finally {PrefabUtility.UnloadPrefabContents(contents);}
 }
 if(puffs)RemoveRayMarchAssets();
 AssetDatabase.SaveAssets();
}

// Returns the number of clouds switched under root.
static int Convert(Transform root,Mesh[] meshes,Material[] bodies,Material[] shells,bool puffs) {
 int count=0;
 foreach(var bank in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name==Bank).ToArray()) {
  foreach(var card in bank.Cast<Transform>().Where(t=>t.name.StartsWith(CardPrefix)).ToArray()) {
   int j=int.Parse(card.name.Substring(CardPrefix.Length));string id=j.ToString("00");
   var legacy=bank.Find(LegacyVolumePrefix+id);if(legacy)Object.DestroyImmediate(legacy.gameObject);
   var puff=bank.Find(PuffPrefix+id);
   if(!puffs){card.gameObject.SetActive(true);if(puff)Object.DestroyImmediate(puff.gameObject);count++;continue;}
   if(!puff){puff=new GameObject(PuffPrefix+id).transform;puff.SetParent(bank,false);puff.gameObject.AddComponent<MeshFilter>();puff.gameObject.AddComponent<MeshRenderer>();}
   puff.gameObject.layer=card.gameObject.layer;
   puff.localPosition=card.localPosition;
   puff.localRotation=Quaternion.Euler(0,(j*47)%90-45,0);
   puff.localScale=Vector3.one*card.localScale.x*.75f;
   puff.GetComponent<MeshFilter>().sharedMesh=meshes[j%meshes.Length];
   var renderer=puff.GetComponent<MeshRenderer>();
   renderer.sharedMaterials=new[]{bodies[j%bodies.Length],shells[j%shells.Length]};
   renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
   renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
   card.gameObject.SetActive(false);count++;
  }
 }
 return count;
}

static void Materials(Texture3D noise,out Material[] bodies,out Material[] shells) {
 var body=Shader.Find(BodyShader);var shell=Shader.Find(ShellShader);
 if(!body||!shell)throw new InvalidOperationException("Missing cloud puff shaders");
 bodies=new Material[10];shells=new Material[10];
 for(int j=0;j<10;j++) {
  bool far=j<3;
  // Same three depth tiers as the cards: far layers are bluer and hazier.
  var tint=far?new Color(.74f,.83f,1):j<7?new Color(.9f,.93f,1):new Color(.97f,.97f,1);
  var shadow=new Color(tint.r*.46f,tint.g*.55f,tint.b*.8f,1);
  foreach(var pair in new[]{(shader:body,name:"Cloud puff ",target:bodies),(shader:shell,name:"Cloud shell ",target:shells)}) {
   var m=new Material(pair.shader);m.name=pair.name+j.ToString("00");
   m.SetTexture("_NoiseTex",noise);m.SetColor("_Tint",tint);m.SetColor("_ShadowTint",shadow);
   m.SetColor("_SunTint",new Color(1,.93f,.8f,1));m.SetFloat("_SunRim",far?.3f:.5f);
   m.SetFloat("_Billow",.045f);m.SetFloat("_BillowScale",1.5f);m.SetFloat("_RollSpeed",.012f);
   m.SetFloat("_Swell",.45f);m.SetFloat("_Drift",.06f);m.SetFloat("_Seed",j*1.37f+.5f);
   m.SetFloat("_ShellInner",far?.045f:.035f);m.SetFloat("_ShellOuter",far?.1f:.08f);m.SetFloat("_ShellOpacity",far?.42f:.3f);
   m.SetFloat("_WispScale",2.5f);m.SetFloat("_WispSpeed",.016f);
   string path=R+"/Materials/"+m.name+".mat";var old=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(old){EditorUtility.CopySerialized(m,old);Object.DestroyImmediate(m);m=old;EditorUtility.SetDirty(m);}else AssetDatabase.CreateAsset(m,path);
   pair.target[j]=m;
  }
 }
}

static void RemoveRayMarchAssets() {
 AssetDatabase.DeleteAsset(R+"/Shaders/CreekCloudVolume.shader");
 for(int j=0;j<10;j++)AssetDatabase.DeleteAsset(R+"/Materials/Cloud volume "+j.ToString("00")+".mat");
}

// One watertight cumulus: an icosphere pushed out to the surface of a union of random spheres,
// about 1 wide and 0.5 tall/deep, with a flattened base. Vertex colour R = lobe phase,
// G = 0 on the flat base and 1 on the billowing top. Two submeshes share the triangles so the
// body and the haze shell materials each get their own.
static Mesh Puff(int variant) {
 var random=new System.Random(7919*(variant+1));
 float Range(float a,float b)=>a+(float)random.NextDouble()*(b-a);
 var lobes=new List<Vector4>{new Vector4(0,0,0,.2f)};
 int extra=6+variant%2;
 for(int k=0;k<extra;k++) {
  float radius=Range(.11f,.19f);
  float x=Mathf.Clamp(Range(-.34f,.34f),-.46f+radius,.46f-radius);
  lobes.Add(new Vector4(x,Range(-.04f,.1f)*(1-Mathf.Abs(x)*1.2f),Range(-.1f,.1f),radius));
 }
 Icosphere(4,out var directions,out var triangles);
 int count=directions.Count;var reach=new float[count];var winners=new int[count];
 for(int i=0;i<count;i++) {
  var d=directions[i];float best=0;
  for(int k=0;k<lobes.Count;k++) {
   var c=(Vector3)lobes[k];float along=Vector3.Dot(d,c);float disc=along*along-c.sqrMagnitude+lobes[k].w*lobes[k].w;
   if(disc<0)continue;float t=along+Mathf.Sqrt(disc);if(t>best){best=t;winners[i]=k;}
  }
  reach[i]=best;
 }
 // Relax the creases between lobes so they become soft valleys instead of stretched shards,
 // and keep a heavily relaxed hull to measure how deep each vertex sits in a crease.
 var neighbours=new HashSet<int>[count];for(int i=0;i<count;i++)neighbours[i]=new HashSet<int>();
 for(int i=0;i<triangles.Count;i+=3){int a=triangles[i],b=triangles[i+1],c=triangles[i+2];neighbours[a].Add(b);neighbours[a].Add(c);neighbours[b].Add(a);neighbours[b].Add(c);neighbours[c].Add(a);neighbours[c].Add(b);}
 float[] Relax(float[] source,int iterations){var current=(float[])source.Clone();for(int n=0;n<iterations;n++){var next=new float[count];for(int i=0;i<count;i++)next[i]=Mathf.Lerp(current[i],neighbours[i].Average(k=>current[k]),.5f);current=next;}return current;}
 var soft=Relax(reach,6);var hull=Relax(reach,24);
 var vertices=new Vector3[count];var colors=new Color[count];
 const float floor=-.11f;
 for(int i=0;i<count;i++) {
  var p=directions[i]*soft[i];
  if(p.y<floor)p.y=Mathf.Lerp(floor,p.y,.22f);
  vertices[i]=p;
  // R = lobe phase, G = 0 on the flat base and 1 on top, B = crease depth for soft self-shadow.
  colors[i]=new Color(winners[i]/(float)lobes.Count,Mathf.Clamp01((p.y-floor)/.09f),Mathf.Clamp01((hull[i]-soft[i])/.05f),1);
 }
 var mesh=new Mesh{name="CloudPuff "+(char)('A'+variant)};
 mesh.SetVertices(vertices);mesh.SetColors(colors);
 mesh.subMeshCount=2;mesh.SetTriangles(triangles,0);mesh.SetTriangles(triangles,1);
 mesh.RecalculateNormals();
 // Room for vertex displacement, drift and the haze shell.
 mesh.bounds=new Bounds(Vector3.zero,new Vector3(1.9f,1.2f,1.4f));
 string path=R+"/Meshes/Effects/CloudPuff "+(char)('A'+variant)+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
 if(old){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}
 AssetDatabase.CreateAsset(mesh,path);return mesh;
}
static void Icosphere(int subdivisions,out List<Vector3> vertices,out List<int> triangles) {
 float t=(1+Mathf.Sqrt(5))/2;
 var v=new List<Vector3>{new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1)};
 for(int i=0;i<v.Count;i++)v[i]=v[i].normalized;
 var tris=new List<int>{0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
 for(int s=0;s<subdivisions;s++) {
  var cache=new Dictionary<long,int>();var next=new List<int>(tris.Count*4);
  int Mid(int a,int b) {
   long key=a<b?((long)a<<32)|(uint)b:((long)b<<32)|(uint)a;
   if(cache.TryGetValue(key,out int index))return index;
   v.Add(((v[a]+v[b])*.5f).normalized);cache[key]=v.Count-1;return v.Count-1;
  }
  for(int i=0;i<tris.Count;i+=3) {
   int a=tris[i],b=tris[i+1],c=tris[i+2],ab=Mid(a,b),bc=Mid(b,c),ca=Mid(c,a);
   next.AddRange(new[]{a,ab,ca,b,bc,ab,c,ca,bc,ab,bc,ca});
  }
  tris=next;
 }
 vertices=v;triangles=tris;
}

// R: tiling Perlin-Worley billows. G: light from above (unused by the mesh clouds).
static Texture3D Noise() {
 var existing=AssetDatabase.LoadAssetAtPath<Texture3D>(NoisePath);if(existing)return existing;
 var density=new float[N*N*N];
 for(int z=0;z<N;z++)for(int y=0;y<N;y++)for(int x=0;x<N;x++) {
  var p=new Vector3(x,y,z)/N;
  float value=Value(p,4)*.55f+Value(p,8)*.3f+Value(p,16)*.15f;
  float cells=(1-Worley(p,4))*.65f+(1-Worley(p,8))*.35f;
  density[(z*N+y)*N+x]=Mathf.Clamp01(Mathf.Lerp(value,cells,.6f)*1.25f-.12f);
 }
 var data=new byte[N*N*N*2];
 for(int z=0;z<N;z++)for(int x=0;x<N;x++) {
  float above=0;
  for(int y=N-1;y>=0;y--) {
   int i=(z*N+y)*N+x;
   data[i*2]=(byte)Mathf.RoundToInt(density[i]*255);
   data[i*2+1]=(byte)Mathf.RoundToInt(Mathf.Exp(-above*.09f)*255);
   above=above*.86f+density[i];
  }
 }
 var texture=new Texture3D(N,N,N,TextureFormat.RG16,true){name="CloudVolumeNoise",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
 texture.SetPixelData(data,0);texture.Apply(true,false);
 AssetDatabase.CreateAsset(texture,NoisePath);return texture;
}
static float Hash(int x,int y,int z,int seed) {
 unchecked {
  uint h=(uint)(x*374761393+y*668265263+z*2147483647+seed*1274126177);
  h=(h^(h>>13))*1274126177u;h^=h>>16;
  return (h&0xffffff)/16777215f;
 }
}
static int Wrap(int v,int period)=>((v%period)+period)%period;
static float Value(Vector3 p,int period) {
 p*=period;int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);
 float fx=Smooth(p.x-x),fy=Smooth(p.y-y),fz=Smooth(p.z-z);
 float Corner(int dx,int dy,int dz)=>Hash(Wrap(x+dx,period),Wrap(y+dy,period),Wrap(z+dz,period),period);
 return Mathf.Lerp(
  Mathf.Lerp(Mathf.Lerp(Corner(0,0,0),Corner(1,0,0),fx),Mathf.Lerp(Corner(0,1,0),Corner(1,1,0),fx),fy),
  Mathf.Lerp(Mathf.Lerp(Corner(0,0,1),Corner(1,0,1),fx),Mathf.Lerp(Corner(0,1,1),Corner(1,1,1),fx),fy),fz);
}
static float Smooth(float t)=>t*t*(3-2*t);
static float Worley(Vector3 p,int period) {
 p*=period;int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);float best=9;
 for(int dz=-1;dz<=1;dz++)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++) {
  int cx=Wrap(x+dx,period),cy=Wrap(y+dy,period),cz=Wrap(z+dz,period);
  var feature=new Vector3(x+dx+Hash(cx,cy,cz,period+11),y+dy+Hash(cx,cy,cz,period+23),z+dz+Hash(cx,cy,cz,period+37));
  best=Mathf.Min(best,(feature-p).sqrMagnitude);
 }
 return Mathf.Clamp01(Mathf.Sqrt(best));
}
}
}
