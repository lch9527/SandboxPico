using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor {
 public static class CreekShaderReview {
  public const string Output="Logs/CreekAA";
  public static void Before()=>Capture("Before");
  public static void After()=>Capture("After");
  // Freeze procedural time in transient diagnostic shaders. No asset or runtime time override.
  static string Source(string path) {
   string source=File.ReadAllText(path);
   return Regex.Replace(source,"#include \\\"([^\\\"]+)\\\"",m=>m.Groups[1].Value.StartsWith("Packages/")?m.Value:Source(m.Groups[1].Value.StartsWith("Assets/")?m.Groups[1].Value:Path.Combine(Path.GetDirectoryName(path),m.Groups[1].Value)));
  }
  static void Capture(string label) {
   Directory.CreateDirectory(Output);var setup=EditorSceneManager.GetSceneManagerSetup();
   // Both camera.allowMSAA and the target's sample count disable MSAA for this
   // capture. Do not assign temporary URP assets to serialized quality settings.
   var report=new System.Text.StringBuilder("fixture,rmse_to_4x_spatial_reference,post_processing,msaa\n");
   try {
    foreach(var fixture in new[]{"Water","Waterfall","Impact","Portal","Emblem"}) {
     EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
     string path=fixture=="Emblem"?"Assets/MetaHackthone/Resources/SteeringEmblem.shader":"Assets/CreekDiorama/Shaders/Creek"+fixture+".shader";
     var source=Source(path).Replace("_Time.y","2.5");source=Regex.Replace(source,"Shader \\\"[^\\\"]+\\\"","Shader \"Hidden/CreekReview/"+fixture+"\"",RegexOptions.None);
     var shader=ShaderUtil.CreateShaderAsset(source);var material=new Material(shader);
     if(material.HasProperty("_UseDepth"))material.SetFloat("_UseDepth",0);
     var plane=GameObject.CreatePrimitive(PrimitiveType.Quad);plane.GetComponent<Renderer>().sharedMaterial=material;
     bool horizontal=fixture=="Water"||fixture=="Emblem";float size=fixture=="Emblem"?.46f:fixture=="Water"?4:1;
     plane.transform.localScale=Vector3.one*size;if(horizontal)plane.transform.rotation=Quaternion.Euler(90,0,0);
     // Water and emblem read object XZ, so bake a true XZ quad rather than rotate XY geometry.
     Mesh mesh=null;
     if(horizontal) {
      mesh=Object.Instantiate(plane.GetComponent<MeshFilter>().sharedMesh);mesh.vertices=mesh.vertices.Select(p=>new Vector3(p.x,0,p.y)).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();plane.GetComponent<MeshFilter>().sharedMesh=mesh;plane.transform.rotation=Quaternion.identity;
     }
     var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.transform.rotation=Quaternion.Euler(43,-25,0);
     var camera=new GameObject("No post processing diagnostic").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=size*.68f;camera.aspect=1.5f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.12f);camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.allowMSAA=false;camera.allowHDR=false;
     camera.transform.position=horizontal?new Vector3(.1f,.65f,-1.2f)*size:new Vector3(.1f,.08f,-1.5f)*size;camera.transform.LookAt(Vector3.zero);
     var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.antialiasing=AntialiasingMode.None;
     var low=Render(camera,384,256,Output+"/"+label+"-"+fixture+".png");var high=Render(camera,1536,1024,null);
     double square=0;int count=0;
     for(int y=4;y<252;y++)for(int x=4;x<380;x++) {
      var average=Vector3.zero;for(int dy=0;dy<4;dy++)for(int dx=0;dx<4;dx++){Color c=high[(y*4+dy)*1536+x*4+dx];average+=new Vector3(c.r,c.g,c.b)/16;}
      Color p=low[y*384+x];square+=(new Vector3(p.r,p.g,p.b)-average).sqrMagnitude;count+=3;
     }
     report.AppendLine($"{fixture},{Math.Sqrt(square/count):F6},off,1");
     if(ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity.ToString()=="Error"))throw new Exception("Shader errors: "+fixture);
     Object.DestroyImmediate(material);Object.DestroyImmediate(shader);if(mesh)Object.DestroyImmediate(mesh);
    }
    File.WriteAllText(Output+"/"+label+"-metrics.csv",report.ToString());
   }finally{
    if(setup.Any(s=>s.isLoaded))EditorSceneManager.RestoreSceneManagerSetup(setup);
   }
  }
  static Color32[] Render(Camera camera,int width,int height,string path) {
   var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){antiAliasing=1};rt.Create();var old=RenderTexture.active;
   var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
   try {RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();if(path!=null)File.WriteAllBytes(path,texture.EncodeToPNG());return texture.GetPixels32();}
   finally{RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);}
  }
 }
}
