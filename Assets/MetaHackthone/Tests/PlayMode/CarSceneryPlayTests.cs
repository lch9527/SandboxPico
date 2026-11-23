using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Rendering.Universal;

namespace PhysicalAssembly.Tests {
 public sealed class CarSceneryPlayTests {
  [UnityTearDown] public IEnumerator Cleanup() {
   DesignModeSession.SetEnabled(false);SandboxCampaignSession.Reset();SceneManager.LoadScene("MainMenu");yield return null;
  }
  [UnityTest] public IEnumerator AllFivePlayableLevelsKeepTheirSceneryAndAnimate() {
   Directory.CreateDirectory("Logs/CarScenery");
   for(int n=1;n<=5;n++) {
    var id=SandboxCourseCatalog.Courses[n-1];DesignModeSession.SetEnabled(true);DesignModeSession.SelectLevel((DesignLevelId)id);
    SceneManager.LoadScene("DownhillSandbox");yield return null;yield return null;
    var controller=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
    Assert.AreEqual(id,controller.CurrentCourse);var f=controller.FactoryLevel;
    var root=f.Layout.Course.SandboxFrame.Find("Environment/Car campaign scenery");Assert.IsNotNull(root);Assert.IsTrue(root.gameObject.activeInHierarchy);
    Assert.IsEmpty(root.GetComponentsInChildren<Collider>(true));
    var volume=f.Layout.Course.BuildArea.GetComponent<SandboxBuildVolume>();Assert.IsNotNull(volume);Assert.IsTrue(volume.ShowWalls);
    Assert.IsTrue(volume.Artwork.GetComponent<Renderer>().enabled,"Lightweight range should be visible while building");
    Assert.AreEqual("Creek Diorama/Lightweight Range Frame",volume.Artwork.GetComponent<Renderer>().sharedMaterial.shader.name);
    var breeze=root.GetComponentsInChildren<CarSceneryBreeze>();Assert.Greater(breeze.Length,0);
    var before=breeze.Select(b=>b.transform.localRotation).ToArray();
    var camera=new GameObject("Scenery review").AddComponent<Camera>();
    camera.transform.position=f.Layout.PlayerStart.position;
    var trackBounds=new Bounds(f.Start.position,Vector3.zero);foreach(var r in f.TrackRoot.GetComponentsInChildren<Renderer>())if(r.enabled)trackBounds.Encapsulate(r.bounds);
    camera.transform.LookAt(trackBounds.center);camera.fieldOfView=70;camera.nearClipPlane=.01f;camera.farClipPlane=200;
    camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.23f,.27f);
    camera.gameObject.AddComponent<CarScenerySky>().Bind(camera,Resources.Load<Material>("CarCampaignSky"));
    camera.GetUniversalAdditionalCameraData().requiresDepthTexture=false;
    camera.GetUniversalAdditionalCameraData().requiresColorTexture=false;
    camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
    camera.GetUniversalAdditionalCameraData().antialiasing=AntialiasingMode.None;
    Capture(camera,$"Logs/CarScenery/Player-Level{n}-A.png");
    yield return new WaitForSeconds(3);
    Assert.IsTrue(breeze.Select((b,i)=>Quaternion.Angle(before[i],b.transform.localRotation)>.5f).Any(x=>x),$"Level {n} scenery does not move");
    Capture(camera,$"Logs/CarScenery/Player-Level{n}-B.png");
    if(n==1) {camera.transform.LookAt(volume.transform.position);Capture(camera,"Logs/SceneryClearance/Lightweight-Range-InGame.png");}
    if(n==4) {
     var winch=root.GetComponentsInChildren<Transform>().First(t=>t.name=="Quarry_Winch");
     var renders=winch.GetComponentsInChildren<Renderer>();var focus=renders[0].bounds;foreach(var r in renders)focus.Encapsulate(r.bounds);
     camera.transform.position=focus.center+new Vector3(.7f,.6f,-1)*focus.size.magnitude;camera.transform.LookAt(focus.center);Capture(camera,"Logs/SceneryClearance/Level4-Art-Closeup.png");
    }
    Object.Destroy(camera.gameObject);
   }
  }
  [UnityTest] public IEnumerator BreezeMovesOnlyRotationAndRestoresPoseOnDisable() {
   var go=new GameObject("Motion test");go.transform.localRotation=Quaternion.Euler(15,30,4);go.transform.localPosition=new Vector3(1,2,3);go.transform.localScale=Vector3.one*.3f;
   var rest=go.transform.localRotation;
   try {
    var breeze=go.AddComponent<CarSceneryBreeze>();breeze.DegreesPerSecond=24;breeze.Axis=Vector3.forward;breeze.Apply(4);
    Assert.Greater(Quaternion.Angle(rest,go.transform.localRotation),70);
    Assert.AreEqual(new Vector3(1,2,3),go.transform.localPosition);Assert.AreEqual(Vector3.one*.3f,go.transform.localScale);
    breeze.enabled=false;Assert.Less(Quaternion.Angle(rest,go.transform.localRotation),.01f);
   }finally{Object.Destroy(go);} yield return null;
  }
  [UnityTest] public IEnumerator DesktopSkyRestoresCameraAndLeavesGlobalSkyUntouched() {
   var go=new GameObject("Desktop sky lifecycle");var camera=go.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;
   var global=RenderSettings.skybox;var binding=go.AddComponent<CarScenerySky>();
   binding.Bind(camera,Resources.Load<Material>("CarCampaignSky"));
   Assert.AreEqual(CameraClearFlags.Skybox,camera.clearFlags);Assert.AreSame(global,RenderSettings.skybox);
   binding.enabled=false;yield return null;
   Assert.AreEqual(CameraClearFlags.SolidColor,camera.clearFlags);Assert.IsNull(go.GetComponent<Skybox>());Assert.AreSame(global,RenderSettings.skybox);
   Object.Destroy(go);
  }
  static void Capture(Camera camera,string path) {
   var rt=RenderTexture.GetTemporary(1500,1000,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;
   try {
    camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
    var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());Object.Destroy(image);
   }finally{camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);}
  }
 }
}
