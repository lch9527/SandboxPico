#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests {
 public sealed class SceneryMotionPlayTests {
  [UnityTest] public IEnumerator WindMovesRenderedVerticesWithoutChangingSavedMeshOrTransform(){
   var level=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>("Assets/MetaHackthone/Generated/Campaign/Level5.prefab");
   var source=level.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Fern_Rosette");
   var go=Object.Instantiate(source.gameObject);go.transform.SetPositionAndRotation(new Vector3(100,100,100),Quaternion.identity);go.transform.localScale=Vector3.one;
   foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
   var camera=new GameObject("Wind validation camera").AddComponent<Camera>();camera.cullingMask=1<<31;camera.orthographic=true;
   camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.nearClipPlane=.001f;camera.farClipPlane=3;
   var bounds=go.GetComponentInChildren<Renderer>().bounds;foreach(var r in go.GetComponentsInChildren<Renderer>())bounds.Encapsulate(r.bounds);
   camera.orthographicSize=bounds.extents.magnitude*.75f;camera.transform.position=bounds.center+Vector3.back*.5f;camera.transform.LookAt(bounds.center);
   var light=new GameObject("Wind validation light").AddComponent<Light>();light.type=LightType.Directional;light.cullingMask=1<<31;light.intensity=2;light.transform.rotation=Quaternion.Euler(35,-20,0);
   var rt=new RenderTexture(512,512,24);rt.Create();camera.targetTexture=rt;
   var before=go.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh.vertices).ToArray();var position=go.transform.position;
   var pixels=new List<Color32[]>();Directory.CreateDirectory("Logs/SceneryMotion");
   try{
    for(int frame=0;frame<2;frame++){
     SceneryWindClock.Set(true,true,frame*2.2f);yield return null;camera.Render();
     var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(512,512,TextureFormat.RGB24,false);
     try{image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();pixels.Add(image.GetPixels32());File.WriteAllBytes("Logs/SceneryMotion/Wind-"+frame+".png",image.EncodeToPNG());}
     finally{RenderTexture.active=old;Object.Destroy(image);}
    }
    int changed=pixels[0].Zip(pixels[1],(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)>12?1:0).Sum();
    Assert.Greater(changed,40,"GPU wind must produce visible pixel changes");
    Assert.AreEqual(position,go.transform.position);
    var after=go.GetComponentsInChildren<MeshFilter>();for(int i=0;i<after.Length;i++)CollectionAssert.AreEqual(before[i],after[i].sharedMesh.vertices);
   }finally{SceneryWindClock.Set(true,false,0);camera.targetTexture=null;rt.Release();Object.Destroy(rt);Object.Destroy(camera.gameObject);Object.Destroy(light.gameObject);Object.Destroy(go);}
  }
  [UnityTest] public IEnumerator IdleWheelAndWinchMaterialsStayStationary(){
   foreach(int n in new[]{3,4,5}){
    var level=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>("Assets/MetaHackthone/Generated/Campaign/Level"+n+".prefab");
    var source=level.GetComponentsInChildren<Transform>(true).First(t=>t.name==(n==3?"Wheelwright_Bench":"Quarry_Winch"));
    var go=Object.Instantiate(source.gameObject);
    try{Assert.IsEmpty(go.GetComponentsInChildren<CarSceneryBreeze>());var motions=go.GetComponentsInChildren<Transform>();var initial=motions.Select(b=>b.transform.localRotation).ToArray();
     yield return new WaitForSeconds(.5f);
     for(int i=0;i<motions.Length;i++)Assert.Less(Quaternion.Angle(initial[i],motions[i].localRotation),.001f);
    }finally{Object.Destroy(go);}
   }
  }
 }
}
#endif
