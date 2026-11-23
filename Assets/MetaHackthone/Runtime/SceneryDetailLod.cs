using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalAssembly {
 [DisallowMultipleComponent]
 public sealed class SceneryDetailLod:MonoBehaviour {
  public sealed class Binding {
   public LODGroup Group;public Renderer[] Original,Filtered;public Bounds LocalBounds;
   public bool[] Enabled;
  }
  public IReadOnlyList<Binding> Bindings=>bindings;
  readonly List<Binding> bindings=new();
  readonly List<(MeshFilter filter,Mesh original)> repaired=new();
  void OnEnable(){Install();RenderPipelineManager.beginCameraRendering+=Prepare;}
  void OnDisable(){RenderPipelineManager.beginCameraRendering-=Prepare;Release();}
  void Install() {
   if(!Application.isPlaying)return;
   var factory=GetComponentInParent<SandboxFactoryLevel>();if(factory && !factory.FilterSceneryDetails)return;
   var catalog=Resources.Load<SceneryDetailCatalog>("SceneryDetailCatalog");if(!catalog)return;
   // A removed/reimported source must leave its detailed renderer intact, rather
   // than aborting installation for the rest of the level.
   var entries=catalog.Foliage.Where(e=>e!=null && e.Source && e.Filtered).GroupBy(e=>e.Source).ToDictionary(g=>g.Key,g=>g.First());
   // Nested hand-placed prefab components own their renderers. The level-wide
   // component covers the existing render batches without duplicating those LODs.
   var renderers=GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled && r.GetComponentInParent<SceneryDetailLod>()==this).ToArray();
   foreach(var r in renderers) {
    var f=r.GetComponent<MeshFilter>();if(!f || !f.sharedMesh)continue;
    if(entries.TryGetValue(f.sharedMesh,out var entry)) {
     var root=new GameObject("Foliage detail LOD"){hideFlags=HideFlags.DontSave,layer=r.gameObject.layer};root.transform.SetParent(r.transform,false);
     var proxy=Proxy(root.transform,entry.Filtered,r.sharedMaterial,"Broad foliage");
     proxy.shadowCastingMode=r.shadowCastingMode;proxy.receiveShadows=r.receiveShadows;
     Add(root,new Renderer[]{r},new Renderer[]{proxy},f.sharedMesh.bounds,entry.ReferenceSize,.22f);
    }
   }
   foreach(var parent in renderers.Where(r=>r.transform.parent && r.transform.parent.name=="01 Display base").Select(r=>r.transform.parent).Distinct()) {
    var original=parent.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).ToArray();
    var steel=original.FirstOrDefault(r=>r.sharedMaterial && r.sharedMaterial.name=="Blue steel edge");if(!steel)continue;
    var entry=catalog.Bases.FirstOrDefault(e=>e.SteelSource==steel.GetComponent<MeshFilter>().sharedMesh);if(entry==null)continue;
    var basalt=original.Select(r=>r.GetComponent<MeshFilter>()).FirstOrDefault(f=>f && f.sharedMesh==entry.BasaltSource);
    if(basalt && entry.RepairedBasalt){repaired.Add((basalt,basalt.sharedMesh));basalt.sharedMesh=entry.RepairedBasalt;}
    var root=new GameObject("Filtered display base LOD"){hideFlags=HideFlags.DontSave,layer=steel.gameObject.layer};root.transform.SetParent(parent,false);
    var far=new[]{Proxy(root.transform,entry.Facade,entry.FacadeMaterial,"Continuous banded base"),Proxy(root.transform,entry.Rim,entry.RimMaterial,"Rim cap"),Proxy(root.transform,entry.Inset,entry.InsetMaterial,"River inset")};
    Add(root,original,far,entry.Facade.bounds,entry.ReferenceSize,.22f);
   }
  }
  static MeshRenderer Proxy(Transform parent,Mesh mesh,Material material,string name) {
   var go=new GameObject(name){hideFlags=HideFlags.DontSave};go.layer=parent.gameObject.layer;go.transform.SetParent(parent,false);
   go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;return r;
  }
  void Add(GameObject root,Renderer[] original,Renderer[] filtered,Bounds bounds,float size,float threshold) {
   root.layer=original[0].gameObject.layer;
   var group=root.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.None;group.animateCrossFading=false;
   group.SetLODs(new[]{new LOD(threshold,original),new LOD(0,filtered)});group.size=size;group.localReferencePoint=bounds.center;
   bindings.Add(new Binding{Group=group,Original=original,Filtered=filtered,LocalBounds=bounds,Enabled=original.Select(r=>r.enabled).ToArray()});
  }
  void Prepare(ScriptableRenderContext context,Camera camera) {
   foreach(var b in bindings)if(b.Group) {
    // A close look at either end of a large baked batch restores detail.
    var local=b.Group.transform.InverseTransformPoint(camera.transform.position);
    b.Group.localReferencePoint=b.LocalBounds.ClosestPoint(local);
   }
  }
  public void ForceLOD(int index){foreach(var b in bindings)if(b.Group)b.Group.ForceLOD(index);}
  void Release() {
   foreach(var b in bindings) {
    if(b.Group){b.Group.enabled=false;b.Group.gameObject.SetActive(false);Destroy(b.Group.gameObject);}
    for(int i=0;i<b.Original.Length;i++)if(b.Original[i])b.Original[i].enabled=b.Enabled[i];
   }
   bindings.Clear();
   foreach(var pair in repaired)if(pair.filter)pair.filter.sharedMesh=pair.original;
   repaired.Clear();
  }
 }
}
