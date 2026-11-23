#if UNITY_EDITOR
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests {
 public sealed class SceneryClarityReviewTests {
  const string Output="Logs/ClarityReview";
  [UnityTest] public IEnumerator CompareNativeAndClarityWithFourSamples() {
   Directory.CreateDirectory(Output);
   var previous=QualitySettings.renderPipeline;float time=Time.timeScale;
   var pipeline=Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset"));
   // Target textures have explicit dimensions: do not apply render scale a second time.
   pipeline.msaaSampleCount=4;pipeline.renderScale=1;
   var report=new System.Text.StringBuilder("Both variants: 4x MSAA, no post processing/depth/opaque texture.\nDesktop proxy of resolution scaling, not Quest compositor capture or performance measurement.\n");
   Camera camera=null;
   try {
    QualitySettings.renderPipeline=pipeline;
    for(int level=1;level<=5;level++) {
     Time.timeScale=1;DesignModeSession.SetEnabled(true);DesignModeSession.SelectLevel((DesignLevelId)SandboxCourseCatalog.Courses[level-1]);
     SceneManager.LoadScene("DownhillSandbox");yield return null;yield return null;
     var f=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill.FactoryLevel;
     foreach(var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))c.enabled=false;
     foreach(var b in Object.FindObjectsByType<CarSceneryBreeze>(FindObjectsSortMode.None))b.enabled=false;
     Time.timeScale=0;
     camera=new GameObject("Resolution comparison").AddComponent<Camera>();camera.enabled=false;
     camera.transform.position=f.Layout.PlayerStart.position;
     var bounds=new Bounds(f.Start.position,Vector3.zero);
     foreach(var r in f.TrackRoot.GetComponentsInChildren<Renderer>())if(r.enabled)bounds.Encapsulate(r.bounds);
     camera.transform.LookAt(bounds.center);camera.aspect=1.5f;camera.fieldOfView=70;camera.nearClipPlane=.01f;camera.farClipPlane=200;
     camera.allowMSAA=true;camera.allowHDR=false;camera.allowDynamicResolution=false;
     camera.gameObject.AddComponent<CarScenerySky>().Bind(camera,Resources.Load<Material>("CarCampaignSky"));
     var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.antialiasing=AntialiasingMode.None;
     data.requiresDepthTexture=false;data.requiresColorTexture=false;
     int frame=Time.frameCount;
     foreach(var variant in new[]{("Before",1500,1000),("After",1725,1150),("Reference",3000,2000),("Before-repeat",1500,1000)}) {
      Capture(camera,variant.Item2,variant.Item3,$"{Output}/Level{level}-{variant.Item1}.png");
      Assert.AreEqual(frame,Time.frameCount);
      report.AppendLine($"level={level} variant={variant.Item1} frame={frame} input={variant.Item2}x{variant.Item3} output=1500x1000 msaa=4 time={Time.time:F6} position={camera.transform.position:F6} rotation={camera.transform.rotation:F6}");
     }
     Object.Destroy(camera.gameObject);camera=null;
    }
    File.WriteAllText(Output+"/Capture-settings.txt",report.ToString());
   }finally {
    if(camera)Object.Destroy(camera.gameObject);QualitySettings.renderPipeline=previous;Time.timeScale=time;Object.Destroy(pipeline);
    DesignModeSession.SetEnabled(false);SandboxCampaignSession.Reset();SceneManager.LoadScene("MainMenu");
   }
  }
  static void Capture(Camera camera,int width,int height,string path) {
   var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){antiAliasing=4,filterMode=FilterMode.Bilinear};
   var resolved=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);resolved.filterMode=FilterMode.Bilinear;
   var output=RenderTexture.GetTemporary(1500,1000,0,RenderTextureFormat.ARGB32);
   var old=RenderTexture.active;var image=new Texture2D(1500,1000,TextureFormat.RGB24,false);
   try {
    Assert.GreaterOrEqual(SystemInfo.GetRenderTextureSupportedMSAASampleCount(target.descriptor),4);Assert.IsTrue(target.Create());
    camera.targetTexture=target;camera.Render();Assert.AreEqual(4,target.antiAliasing);
    // Resolve before the common bilinear presentation step. No FXAA/TAA/sharpen pass.
    Graphics.Blit(target,resolved);Graphics.Blit(resolved,output);RenderTexture.active=output;
    image.ReadPixels(new Rect(0,0,1500,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
   }finally {
    camera.targetTexture=null;RenderTexture.active=old;target.Release();Object.Destroy(target);Object.Destroy(image);
    RenderTexture.ReleaseTemporary(resolved);RenderTexture.ReleaseTemporary(output);
   }
  }
 }
}
#endif
