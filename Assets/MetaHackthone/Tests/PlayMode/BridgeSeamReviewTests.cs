#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests {
 public sealed class BridgeSeamReviewTests {
  [UnityTest] public IEnumerator FilteredSeamsPreservePhysicsAndAllFivePuzzleCutouts() {
   const string output="Logs/BridgeSeams";Directory.CreateDirectory(output);
   var previous=QualitySettings.renderPipeline;float time=Time.timeScale;
   var pipeline=Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset"));
   pipeline.msaaSampleCount=4;pipeline.renderScale=1;
   var report=new System.Text.StringBuilder("Both images: 4x MSAA, 1500x1000, render scale 1, no post-processing/depth/opaque texture. Same-frame capture.\n");
   Camera camera=null;
   try {
    QualitySettings.renderPipeline=pipeline;
    for(int level=1;level<=5;level++) {
     Time.timeScale=1;DesignModeSession.SetEnabled(true);DesignModeSession.SelectLevel((DesignLevelId)SandboxCourseCatalog.Courses[level-1]);
     SceneManager.LoadScene("DownhillSandbox");yield return null;yield return null;
     var f=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill.FactoryLevel;
     var colliders=f.GetComponentsInChildren<Collider>(true);
     var collisionMeshes=colliders.OfType<MeshCollider>().Select(c=>c.sharedMesh).ToArray();
     var lods=f.Tracks.Select(t=>t.GetComponent<BridgeDeckLod>()).ToArray();
     foreach(var lod in lods) {
      Assert.IsNotNull(lod);Assert.IsNotNull(lod.Group);Assert.IsNotNull(lod.Filtered);
      Assert.IsEmpty(lod.Filtered.GetComponents<Collider>());
      var track=lod.GetComponent<SandboxTrackGenerator>();var road=track.Generated.transform.Find("Road");
      Assert.AreSame(road.GetComponent<MeshCollider>().sharedMesh,lod.Filtered.GetComponent<MeshFilter>().sharedMesh,"Use the exact baked cutouts, never bridge a puzzle gap");
      report.AppendLine($"level={level} detailed_renderers={lod.Detailed.Length} filtered_renderers=1 detailed_triangles={lod.Detailed.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3)} filtered_triangles={road.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3}");
     }
     foreach(var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))c.enabled=false;
     foreach(var b in Object.FindObjectsByType<CarSceneryBreeze>(FindObjectsSortMode.None))b.enabled=false;
     Time.timeScale=0;camera=new GameObject("Bridge seam comparison").AddComponent<Camera>();camera.enabled=false;
     camera.transform.position=f.Layout.PlayerStart.position;
     var bounds=new Bounds(f.Start.position,Vector3.zero);foreach(var r in f.TrackRoot.GetComponentsInChildren<Renderer>())if(r.enabled)bounds.Encapsulate(r.bounds);
     camera.transform.LookAt(bounds.center);camera.aspect=1.5f;camera.fieldOfView=70;camera.nearClipPlane=.01f;camera.farClipPlane=200;
     camera.allowMSAA=true;camera.allowHDR=false;
     camera.gameObject.AddComponent<CarScenerySky>().Bind(camera,Resources.Load<Material>("CarCampaignSky"));
     var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.antialiasing=AntialiasingMode.None;data.requiresDepthTexture=false;data.requiresColorTexture=false;
     int frame=Time.frameCount;
     Color32[] filteredPixels=null;
     foreach(int mode in new[]{0,1,0}) {
      foreach(var lod in lods)lod.Group.ForceLOD(mode);
      var pixels=Capture(camera,$"{output}/Level{level}-{(mode==0?"Before":"After")}.png");if(mode==1)filteredPixels=pixels;
      Assert.AreEqual(frame,Time.frameCount);
     }
     Assert.That(f.GetComponentsInChildren<Collider>(true),Is.EquivalentTo(colliders));
     CollectionAssert.AreEqual(collisionMeshes,colliders.OfType<MeshCollider>().Select(c=>c.sharedMesh).ToArray());
     foreach(var lod in lods)lod.Group.ForceLOD(-1);
     Similar(filteredPixels,Capture(camera,null),"Default gameplay camera must select filtered seams");
     var savedPosition=camera.transform.position;var savedRotation=camera.transform.rotation;
     if(level==1) {
      var track=f.Tracks[0];var target=track.transform.TransformPoint(track.Samples[track.Samples.Length/2]);
      camera.transform.position=target+Vector3.up*.12f+camera.transform.forward*-.08f;camera.transform.LookAt(target);
      var near=Capture(camera,output+"/Level1-Near.png");foreach(var lod in lods)lod.Group.ForceLOD(0);
      Similar(near,Capture(camera,null),"Moving close to any road section must select original planks");
      foreach(var lod in lods)lod.Group.ForceLOD(-1);camera.transform.SetPositionAndRotation(savedPosition,savedRotation);Capture(camera,null);
     }
     foreach(var lod in lods) {
      lod.Group.ForceLOD(-1);
      // The transition uses road width, not track length. Normal default view uses
      // the filtered deck; moving near the board surface restores actual planks.
      float distance=Vector3.Distance(camera.transform.position,lod.Group.transform.TransformPoint(lod.Group.localReferencePoint));
      float relative=lod.Group.size*lod.transform.lossyScale.x/(2*distance*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f))*QualitySettings.lodBias;
      report.AppendLine($"level={level} frame={frame} relative_height={relative:F4} transition={lod.Group.GetLODs()[0].screenRelativeTransitionHeight} camera={camera.transform.position:F5}");
      var originalRenderers=lod.Detailed.ToArray();lod.enabled=false;foreach(var r in originalRenderers)Assert.IsTrue(r.enabled);
     }
     Object.Destroy(camera.gameObject);camera=null;
    }
    File.WriteAllText(output+"/Validation.txt",report.ToString());
   }finally {
    if(camera)Object.Destroy(camera.gameObject);QualitySettings.renderPipeline=previous;Time.timeScale=time;Object.Destroy(pipeline);
    DesignModeSession.SetEnabled(false);SandboxCampaignSession.Reset();SceneManager.LoadScene("MainMenu");
   }
  }
  static void Similar(Color32[] a,Color32[] b,string message) {
   int changed=0;for(int i=0;i<a.Length;i++)if(Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b)>8)changed++;
   Assert.Less(changed,a.Length*.001f,message);
  }
  static Color32[] Capture(Camera camera,string path) {
   var rt=new RenderTexture(1500,1000,24,RenderTextureFormat.ARGB32){antiAliasing=4};rt.Create();
   var resolved=RenderTexture.GetTemporary(1500,1000,0,RenderTextureFormat.ARGB32);var old=RenderTexture.active;var image=new Texture2D(1500,1000,TextureFormat.RGB24,false);
   try{camera.targetTexture=rt;camera.Render();Graphics.Blit(rt,resolved);RenderTexture.active=resolved;image.ReadPixels(new Rect(0,0,1500,1000),0,0);image.Apply();if(path!=null)File.WriteAllBytes(path,image.EncodeToPNG());return image.GetPixels32();}
   finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.Destroy(rt);RenderTexture.ReleaseTemporary(resolved);Object.Destroy(image);}
  }
 }
}
#endif
