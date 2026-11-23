#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests {
 public sealed class SceneryDetailReviewTests {
  const string Output="Logs/LODRepair-20261117/Captures";
  [UnityTest] public IEnumerator AllFiveLevelsKeepPhysicsAndRestoreDetailedScenery() {
   Directory.CreateDirectory(Output);var previous=QualitySettings.renderPipeline;float time=Time.timeScale;
   var pipeline=Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset"));
   pipeline.msaaSampleCount=4;pipeline.renderScale=1;pipeline.supportsCameraDepthTexture=false;pipeline.supportsCameraOpaqueTexture=false;
   var report=new StringBuilder("Desktop PlayMode. 1500x1000, 4x MSAA, URP scale 1, no PP/depth/opaque texture. Before=component off, After=automatic LOD. Same-frame A/B.\n");Camera camera=null;
   try {
    QualitySettings.renderPipeline=pipeline;
    for(int level=1;level<=5;level++) {
     Time.timeScale=1;DesignModeSession.SetEnabled(true);DesignModeSession.SelectLevel((DesignLevelId)SandboxCourseCatalog.Courses[level-1]);
     SceneManager.LoadScene("DownhillSandbox");yield return null;yield return null;
     var f=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill.FactoryLevel;
     var colliders=f.GetComponentsInChildren<Collider>(true);var matrices=colliders.Select(c=>c.transform.localToWorldMatrix).ToArray();
     var collisionMeshes=colliders.OfType<MeshCollider>().Select(c=>c.sharedMesh).ToArray();
     var switches=f.GetComponentsInChildren<SceneryDetailLod>();Assert.IsNotEmpty(switches);
     var bindings=switches.SelectMany(s=>s.Bindings).ToArray();Assert.Greater(bindings.Length,5);
     if(level<=4) {
      var meadow=bindings.Where(b=>b.Original.Length==1 && b.Original[0].transform.parent.name=="Moss ivy ferns and meadow").ToArray();
      Assert.AreEqual(6,meadow.Length,"All six current meadow material batches need runtime LODs");
      Assert.Less(meadow.Sum(b=>Triangles(b.Filtered)),meadow.Sum(b=>Triangles(b.Original))*.6f);
     }
     foreach(var b in bindings)foreach(var r in b.Filtered)Assert.IsEmpty(r.GetComponents<Collider>());
     report.AppendLine($"level={level} groups={bindings.Length} detailed_renderers={bindings.Sum(b=>b.Original.Length)} far_renderers={bindings.Sum(b=>b.Filtered.Length)} detailed_triangles={bindings.Sum(b=>Triangles(b.Original))} far_triangles={bindings.Sum(b=>Triangles(b.Filtered))}");
     foreach(var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))c.enabled=false;
     foreach(var b in Object.FindObjectsByType<CarSceneryBreeze>(FindObjectsSortMode.None))b.enabled=false;
     Time.timeScale=0;camera=new GameObject("Scenery AA same-frame review").AddComponent<Camera>();camera.enabled=false;
     camera.transform.position=f.Layout.PlayerStart.position;
     var bounds=new Bounds(f.Start.position,Vector3.zero);foreach(var r in f.TrackRoot.GetComponentsInChildren<Renderer>())if(r.enabled)bounds.Encapsulate(r.bounds);
     camera.transform.LookAt(bounds.center);camera.aspect=1.5f;camera.fieldOfView=70;camera.nearClipPlane=.01f;camera.farClipPlane=200;camera.allowMSAA=true;camera.allowHDR=false;
     camera.gameObject.AddComponent<CarScenerySky>().Bind(camera,Resources.Load<Material>("CarCampaignSky"));
     var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.antialiasing=AntialiasingMode.None;data.requiresDepthTexture=false;data.requiresColorTexture=false;
     Compare(camera,switches,$"Level{level}",report);
     if(level==1) {
      var display=f.GetComponentsInChildren<Transform>().First(t=>t.name=="01 Display base");
      camera.fieldOfView=55;camera.transform.position=display.TransformPoint(new Vector3(1.65f,.65f,2.8f));camera.transform.LookAt(display.TransformPoint(new Vector3(0,.10f,0)));
      Compare(camera,switches,"Base",report);
      camera.fieldOfView=55;camera.transform.position=display.TransformPoint(new Vector3(.5f,1.55f,2.7f));camera.transform.LookAt(display.TransformPoint(new Vector3(-.9f,.62f,.05f)));
      Compare(camera,switches,"Plants",report);
      // Verify actual camera-driven near selection on a single original mesh.
      var owner=switches.First(s=>s.Bindings.Any(b=>b.Original.Length==1 && b.Original[0].GetComponent<MeshFilter>().sharedMesh.triangles.Length>0));
      var nearBinding=owner.Bindings.First(b=>b.Original.Length==1);
      var point=nearBinding.Group.transform.TransformPoint(nearBinding.LocalBounds.center);
      camera.transform.position=point+Vector3.up*.015f+Vector3.forward*.02f;camera.transform.LookAt(point);
      foreach(var s in switches)s.ForceLOD(0);nearBinding.Group.ForceLOD(-1);
      var near=Capture(camera,Output+"/Near.png");nearBinding.Group.ForceLOD(0);Similar(near,Capture(camera,null),"Near camera restores original foliage");
      foreach(var s in switches)s.ForceLOD(-1);
     }
     CollectionAssert.AreEquivalent(colliders,f.GetComponentsInChildren<Collider>(true));
     CollectionAssert.AreEqual(matrices,colliders.Select(c=>c.transform.localToWorldMatrix).ToArray());
     CollectionAssert.AreEqual(collisionMeshes,colliders.OfType<MeshCollider>().Select(c=>c.sharedMesh).ToArray());
     foreach(var s in switches){var original=s.Bindings.SelectMany(b=>b.Original).ToArray();s.enabled=false;foreach(var r in original)Assert.IsTrue(r.enabled);}
     Object.Destroy(camera.gameObject);camera=null;
    }
    File.WriteAllText(Output+"/Validation.txt",report.ToString());
   }finally{if(camera)Object.Destroy(camera.gameObject);QualitySettings.renderPipeline=previous;Time.timeScale=time;Object.Destroy(pipeline);DesignModeSession.SetEnabled(false);SandboxCampaignSession.Reset();SceneManager.LoadScene("MainMenu");}
  }
  static int Triangles(Renderer[] renderers)=>renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);
  static void Compare(Camera camera,SceneryDetailLod[] switches,string name,StringBuilder report) {
   int frame=Time.frameCount;
   foreach(var s in switches)s.enabled=false;Capture(camera,null);var before=Capture(camera,$"{Output}/{name}-Before.png");
   foreach(var s in switches)s.enabled=true;Capture(camera,null);Capture(camera,$"{Output}/{name}-After.png");
   foreach(var s in switches)s.ForceLOD(1);var far=Capture(camera,$"{Output}/{name}-Far.png");
   foreach(var s in switches)s.ForceLOD(-1);
   foreach(var s in switches)s.enabled=false;Similar(before,Capture(camera,null),"Disabling returns to original renderer/mesh state");
   foreach(var s in switches)s.enabled=true;
   Assert.AreEqual(frame,Time.frameCount);
   report.AppendLine($"capture={name} frame={frame} position={camera.transform.position:F5} rotation={camera.transform.eulerAngles:F5}");
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
