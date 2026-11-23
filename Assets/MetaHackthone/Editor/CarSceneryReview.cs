using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Editor {
 public static class CarSceneryReview {
  public const string Output="Logs/CarScenery";
  public static void Audit()=>RenderAll("Before");
  public static void Review()=>RenderAll("After");
  static void RenderAll(string label) {
   Directory.CreateDirectory(Output);
   var report=new StringBuilder();
   bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
   try {
    for(int n=1;n<=5;n++) {
     EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
     var asset=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab");
     var instance=Object.Instantiate(asset);var f=instance.GetComponent<SandboxFactoryLevel>();
     report.AppendLine($"LEVEL {n}: {f.Title} frame {f.Layout.Course.SandboxFrame.position} scale {f.Layout.Course.SandboxFrame.lossyScale}");
     foreach(var t in instance.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Environment"||t.name=="Track"||t.name.Contains("Beautiful")||t.name.Contains("Player Start")||t.name.Contains("cloud banks")))
      report.AppendLine($"{t.name} pos={t.position} local={t.localPosition} scale={t.lossyScale} children={string.Join(",",t.Cast<Transform>().Select(x=>x.name))}");
     foreach(var track in f.Tracks)report.AppendLine($"TRACK {track.name} pos={track.transform.position} scale={track.transform.lossyScale} points={string.Join(";",track.Samples.Select(s=>s.ToString()))}");
     foreach(var scenery in f.Scenery.Where(t=>t))report.AppendLine($"SCENERY {scenery.name} pos={scenery.position} scale={scenery.lossyScale}");
     Capture(f,Output+$"/{label}-Level{n}.png");
    }
    File.WriteAllText(Output+"/"+label+"-Audit.txt",report.ToString());
   }finally{SandboxTrackEditing.Suspended=suspended;}
  }
  public static void Capture(SandboxFactoryLevel f,string path) {
   var sun=new GameObject("Review sunlight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.color=new Color(1,.94f,.84f);sun.transform.rotation=Quaternion.Euler(45,-35,0);
   RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.58f,.65f,.72f);
   var renderers=f.TrackRoot.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
   var bounds=new Bounds(f.Start.position,Vector3.zero);foreach(var r in renderers)bounds.Encapsulate(r.bounds);
   var environment=f.Layout.Course.SandboxFrame.Find("Environment");
   if(environment)foreach(var r in environment.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy && !r.transform.GetComponentsInParent<Transform>().Any(t=>t.name.Contains("cloud")||t.name.Contains("Cloud")||t.name=="Horizon transition islands")))bounds.Encapsulate(r.bounds);
   // The desktop camera helper is editor-only artwork, not part of the landscape.
   if(f.Layout.BuildingRoot)f.Layout.BuildingRoot.gameObject.SetActive(false);
   var camera=new GameObject("Review camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=Mathf.Max(bounds.size.x,bounds.size.z)*.56f;camera.aspect=1.5f;
   camera.nearClipPlane=.01f;camera.farClipPlane=1000;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.23f,.27f);
   camera.clearFlags=CameraClearFlags.Skybox;camera.gameObject.AddComponent<Skybox>().material=Resources.Load<Material>("CarCampaignSky");
   camera.transform.position=bounds.center+new Vector3(1.1f,1.4f,-1.4f)*Mathf.Max(bounds.size.magnitude,2);camera.transform.LookAt(bounds.center);
   camera.GetUniversalAdditionalCameraData().requiresDepthTexture=false;camera.GetUniversalAdditionalCameraData().requiresColorTexture=false;
   var rt=new RenderTexture(1500,1000,24,RenderTextureFormat.ARGB32);rt.Create();var old=RenderTexture.active;
   try {RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());Object.DestroyImmediate(tex);}
   finally {RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(sun.gameObject);}
  }
 }
}
