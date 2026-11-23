using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalAssembly {
 // Visual-only LOD. The existing road mesh already contains every puzzle cutout;
 // reuse it for rendering without replacing or editing any collision mesh.
 [DisallowMultipleComponent]
 public sealed class BridgeDeckLod:MonoBehaviour {
  public const string MaterialResource="FilteredBridgeDeck";
  public LODGroup Group {get;private set;}
  public Renderer[] Detailed {get;private set;}
  public Renderer Filtered {get;private set;}
  SandboxTrackGenerator track;GameObject bound,visual;Material material;bool[] enabledBefore;
  void OnEnable(){track=GetComponent<SandboxTrackGenerator>();RenderPipelineManager.beginCameraRendering+=PrepareCamera;Refresh();}
  void LateUpdate(){if(track && track.Generated!=bound)Refresh();}
  void OnDisable(){RenderPipelineManager.beginCameraRendering-=PrepareCamera;Release();}
  void PrepareCamera(ScriptableRenderContext context,Camera camera) {
   if(!Group || !track || track.Samples.Length<2)return;
   // A winding track's centre can be far from a plank directly beside the user.
   // Anchor the width-based LOD at the closest point on the baked centreline.
   var eye=track.transform.InverseTransformPoint(camera.transform.position);
   var nearest=track.Samples[0];float best=float.PositiveInfinity;
   for(int i=1;i<track.Samples.Length;i++) {
    var a=track.Samples[i-1];var edge=track.Samples[i]-a;
    var point=a+edge*Mathf.Clamp01(Vector3.Dot(eye-a,edge)/Mathf.Max(edge.sqrMagnitude,1e-8f));
    float distance=(point-eye).sqrMagnitude;if(distance<best){best=distance;nearest=point;}
   }
   Group.localReferencePoint=Group.transform.InverseTransformPoint(track.transform.TransformPoint(nearest));
  }
  void Refresh() {
   Release();
   bound=track?track.Generated:null;
   if(!Application.isPlaying || !track || track.DeckStyle!=SandboxDeckStyle.CreekDeck || !track.ShowGeneratedVisuals || !track.Generated)return;
   // This shader matches the authored straight timber module. Other modules may
   // have different board spacing and should keep their original geometry.
   if(!track.DeckModule || track.DeckModule.name!="Deck_Straight_080")return;
   var road=bound.transform.Find("Road")?.GetComponent<MeshFilter>();
   var template=Resources.Load<Material>(MaterialResource);
   if(!road || !road.sharedMesh || !template)return;
   Detailed=bound.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("CreekDeck_")).Cast<Renderer>().ToArray();
   if(Detailed.Length==0)return;
   enabledBefore=Detailed.Select(r=>r.enabled).ToArray();
   visual=new GameObject("Bridge deck - filtered seams LOD"){hideFlags=HideFlags.DontSave};visual.transform.SetParent(road.transform,false);
   visual.AddComponent<MeshFilter>().sharedMesh=road.sharedMesh;
   Filtered=visual.AddComponent<MeshRenderer>();
   material=new Material(template){name="Filtered bridge deck (instance)",hideFlags=HideFlags.DontSave};material.SetFloat("_Pitch",track.DefaultWidth*.125f);material.SetFloat("_Width",track.DefaultWidth);
   Filtered.sharedMaterial=material;
   Group=visual.AddComponent<LODGroup>();Group.fadeMode=LODFadeMode.None;
   Group.SetLODs(new[]{new LOD(.35f,Detailed),new LOD(0,new[]{Filtered})});
   Group.localReferencePoint=road.sharedMesh.bounds.center;
   // LOD is based on road width, not the length of the entire winding track.
   // No dither/transparent crossfade: it would reintroduce speckled edge patterns.
   Group.size=track.DefaultWidth;Group.animateCrossFading=false;
  }
  void Release() {
   if(Group){Group.enabled=false;Destroy(Group);}
   if(Detailed!=null && enabledBefore!=null)for(int i=0;i<Detailed.Length;i++)if(Detailed[i])Detailed[i].enabled=enabledBefore[i];
   if(visual){visual.SetActive(false);Destroy(visual);}if(material)Destroy(material);
   Group=null;Filtered=null;Detailed=null;enabledBefore=null;visual=null;material=null;bound=null;
  }
 }
}
