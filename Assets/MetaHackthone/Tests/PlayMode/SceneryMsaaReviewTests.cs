#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests {
 // Explicit opt-in diagnostic: screenshots use a real multisampled target, not the
 // default single-sample temporary target used by the ordinary scenery tests.
 public sealed class SceneryMsaaReviewTests {
  const string Output="Logs/MSAAReview";
  [UnityTest, Timeout(600000)] public IEnumerator CompareAllFiveLevelsOffAndFourSamples() {
   Directory.CreateDirectory(Output);
   var previousPipeline=QualitySettings.renderPipeline;
   int previousVsync=QualitySettings.vSyncCount, previousFps=Application.targetFrameRate;
   float previousTime=Time.timeScale;
   var pipeline=Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset"));
   pipeline.name="Transient mobile MSAA comparison";
   Camera camera=null;RenderTexture target=null;
   var report=new System.Text.StringBuilder("level,round,msaa,width,height,frames,editor_frame_ms_median,editor_frame_ms_p95,gpu_samples,gpu_ms_median\n");
   var settings=new System.Text.StringBuilder($"main=210\nunity={Application.unityVersion}\ngpu={SystemInfo.graphicsDeviceName}\napi={SystemInfo.graphicsDeviceType}\nplatform={Application.platform}\nsource_pipeline=Mobile_RPAsset\nsource_msaa={pipeline.msaaSampleCount}\nrender_scale={pipeline.renderScale}\ndepth={pipeline.supportsCameraDepthTexture}\nopaque={pipeline.supportsCameraOpaqueTexture}\npost_processing=false\nresolution=1500x1000\nview=authored player start looking at track bounds\nframe_timing_enabled={FrameTimingManager.IsFeatureEnabled()}\nTiming is Editor wall frame interval, NOT Quest FPS or GPU cost. Static simulation; includes Editor/test overhead.\n");
   try {
    QualitySettings.renderPipeline=pipeline;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
    for(int level=1;level<=5;level++) {
     Time.timeScale=1;
     DesignModeSession.SetEnabled(true);DesignModeSession.SelectLevel((DesignLevelId)SandboxCourseCatalog.Courses[level-1]);
     SceneManager.LoadScene("DownhillSandbox");yield return null;yield return null;
     // WorkshopBootstrap sets 90 FPS in Start. Remove that cap after scene startup,
     // otherwise both variants misleadingly report the same 11.11 ms frame interval.
     QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
     var f=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill.FactoryLevel;
     Assert.IsNotNull(f);Assert.IsNotNull(f.Layout.Course.SandboxFrame.Find("Environment/Car campaign scenery"));
     foreach(var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled=false;
     foreach(var motion in Object.FindObjectsByType<CarSceneryBreeze>(FindObjectsSortMode.None)) motion.enabled=false;
     Time.timeScale=0;
     camera=new GameObject("MSAA controlled game view").AddComponent<Camera>();
     camera.transform.position=f.Layout.PlayerStart.position;
     var bounds=new Bounds(f.Start.position,Vector3.zero);
     foreach(var r in f.TrackRoot.GetComponentsInChildren<Renderer>())if(r.enabled)bounds.Encapsulate(r.bounds);
     camera.transform.LookAt(bounds.center);camera.fieldOfView=70;camera.aspect=1.5f;camera.nearClipPlane=.01f;camera.farClipPlane=200;
     camera.allowMSAA=true;camera.allowHDR=false;camera.allowDynamicResolution=false;
     camera.gameObject.AddComponent<CarScenerySky>().Bind(camera,Resources.Load<Material>("CarCampaignSky"));
     var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.antialiasing=AntialiasingMode.None;
     data.requiresDepthTexture=false;data.requiresColorTexture=false;
     settings.AppendLine($"level={level} position={camera.transform.position:F6} rotation={camera.transform.rotation:F6} frozen_time={Time.time:F6}");
     // ABBA ordering reduces warm-up/order bias. Readbacks are outside timing windows.
     int round=0;
     foreach(int samples in new[]{1,4,4,1}) {
      round++;pipeline.msaaSampleCount=samples;
      target=new RenderTexture(1500,1000,24,RenderTextureFormat.ARGB32){antiAliasing=samples,name=$"MSAA {samples} target"};
      Assert.GreaterOrEqual(SystemInfo.GetRenderTextureSupportedMSAASampleCount(target.descriptor),samples);
      Assert.IsTrue(target.Create());camera.targetTexture=target;
      for(int warm=0;warm<45;warm++)yield return null;
      Assert.AreEqual(samples,target.antiAliasing);Assert.AreEqual(samples,pipeline.msaaSampleCount);
      var times=new List<double>();var gpu=new List<double>();var timing=new FrameTiming[1];
      double last=Time.realtimeSinceStartupAsDouble;
      for(int frame=0;frame<120;frame++) {
       if(FrameTimingManager.IsFeatureEnabled())FrameTimingManager.CaptureFrameTimings();
       yield return null;
       double now=Time.realtimeSinceStartupAsDouble;times.Add((now-last)*1000);last=now;
       if(FrameTimingManager.IsFeatureEnabled() && FrameTimingManager.GetLatestTimings(1,timing)>0 && timing[0].gpuFrameTime>0)gpu.Add(timing[0].gpuFrameTime);
      }
      times.Sort();gpu.Sort();
      report.AppendLine(string.Join(",",new[]{level.ToString(),round.ToString(),samples.ToString(),"1500","1000",times.Count.ToString(),F(times[times.Count/2]),F(times[(int)((times.Count-1)*.95)]),gpu.Count.ToString(),gpu.Count>0?F(gpu[gpu.Count/2]):"unavailable"}));
      settings.AppendLine($"level={level} round={round} pipeline_samples={pipeline.msaaSampleCount} target_samples={target.antiAliasing} camera_msaa={camera.allowMSAA} quality_samples={QualitySettings.antiAliasing} target_fps={Application.targetFrameRate} vsync={QualitySettings.vSyncCount} frozen_time={Time.time:F6}");
      camera.targetTexture=null;target.Release();Object.Destroy(target);target=null;
      File.WriteAllText(Output+"/Editor-timing.csv",report.ToString());File.WriteAllText(Output+"/Settings.txt",settings.ToString());
     }
     // Some gameplay presentation uses unscaled time. Capture all four images
     // synchronously in ONE simulation frame, separately from performance samples,
     // so even those objects cannot change pose between OFF and 4x.
     camera.enabled=false;int capture=0;
     foreach(int samples in new[]{1,4,4,1}) {
      capture++;pipeline.msaaSampleCount=samples;
      target=new RenderTexture(1500,1000,24,RenderTextureFormat.ARGB32){antiAliasing=samples};target.Create();
      camera.targetTexture=target;camera.Render();
      Save(target,$"{Output}/Level{level}-{(samples==1?"OFF":"4x")}{(capture<=2?"":"-repeat")}.png");
      settings.AppendLine($"capture level={level} index={capture} frame={Time.frameCount} msaa={samples} target_samples={target.antiAliasing} time={Time.time:F6}");
      camera.targetTexture=null;target.Release();Object.Destroy(target);target=null;
     }
     File.WriteAllText(Output+"/Settings.txt",settings.ToString());
     Object.Destroy(camera.gameObject);camera=null;
    }
   }finally {
    if(camera)Object.Destroy(camera.gameObject);
    if(target){target.Release();Object.Destroy(target);}
    QualitySettings.renderPipeline=previousPipeline;QualitySettings.vSyncCount=previousVsync;Application.targetFrameRate=previousFps;Time.timeScale=previousTime;
    Object.Destroy(pipeline);DesignModeSession.SetEnabled(false);SandboxCampaignSession.Reset();SceneManager.LoadScene("MainMenu");
   }
  }
  static string F(double value)=>value.ToString("F4",CultureInfo.InvariantCulture);
  static void Save(RenderTexture source,string path) {
   // Resolve MSAA before CPU readback; never read a multisampled surface directly.
   var resolved=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32);
   var old=RenderTexture.active;var image=new Texture2D(source.width,source.height,TextureFormat.RGB24,false);
   try {Graphics.Blit(source,resolved);RenderTexture.active=resolved;image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
   finally {RenderTexture.active=old;RenderTexture.ReleaseTemporary(resolved);Object.Destroy(image);}
  }
 }
}
#endif
