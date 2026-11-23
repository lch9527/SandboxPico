#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using CloudGarden.Integration;
using NUnit.Framework;
using PhysicalAssembly;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CloudGarden.Tests
{
    public sealed class ManualEnvironmentPerfTests
    {
        [UnityTest]
        public IEnumerator FiveLevelsWaitForManualStartAndQuitSavesMeasurements()
        {
            var capture=QuestEnvironmentPerf.BeginManual();
            try {
                yield return WaitMenu();
                for(int n=1;n<=5;n++) {
                    var menu=Object.FindFirstObjectByType<MainMenuBootstrap>();
                    Assert.IsTrue(menu.Panel.gameObject.activeInHierarchy,"VR menu must remain visible");
                    menu.Panel.GetComponentsInChildren<WorkshopButton>().Single(b=>b.GetComponentInChildren<TextMesh>().text=="LEVEL "+n).Press();
                    float deadline=Time.realtimeSinceStartup+30;WorkshopBootstrap workshop=null;
                    do{yield return null;workshop=Object.FindFirstObjectByType<WorkshopBootstrap>();}while((!workshop||!workshop.Downhill||!workshop.Downhill.Core)&&Time.realtimeSinceStartup<deadline);
                    Assert.IsNotNull(workshop);var level=workshop.Downhill;yield return new WaitForSecondsRealtime(1.2f);
                    Assert.AreEqual(n,capture.SelectedLevel);Assert.AreEqual(n,SandboxCourseCatalog.Number(level.FactoryLevel.Layout.CourseId));
                    Assert.AreEqual(DownhillState.Build,level.State,"Must not auto-start level "+n);
                    Assert.IsNotNull(level.Core);
                    var water=level.FactoryLevel.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Living river surface"||f.name=="Freight creek");
                    var wall=level.FactoryLevel.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Inner tray wall");
                    var waterY=water.GetComponent<Renderer>().bounds.center.y;
                    foreach(int lod in new[]{0,1}) {
                        foreach(var detail in level.FactoryLevel.GetComponentsInChildren<SceneryDetailLod>())detail.ForceLOD(lod);
                        yield return null;
                        Assert.IsTrue(wall.enabled&&wall.gameObject.activeInHierarchy);Assert.IsNull(wall.GetComponentInParent<LODGroup>());
                        Assert.Less(wall.bounds.min.y,waterY);Assert.Greater(wall.bounds.max.y,waterY);
                        Capture(level.FactoryLevel,n,lod);
                    }
                    foreach(var detail in level.FactoryLevel.GetComponentsInChildren<SceneryDetailLod>())detail.ForceLOD(-1);
                    level.Settings.CountdownStep=.01f;level.Settings.GoDuration=.01f;
                    level.LaunchDecomposeSeconds=level.LaunchTravelSeconds=level.LaunchReconstructSeconds=.01f;
                    level.Presentation.ReadyButton.Press();deadline=Time.realtimeSinceStartup+8;
                    while(level.State!=DownhillState.Running&&Time.realtimeSinceStartup<deadline)yield return null;
                    Assert.AreEqual(DownhillState.Running,level.State,"Manual START failed on "+n);
                    yield return new WaitForSecondsRealtime(1.2f);
                    level.Presentation.ExitButton.Press();yield return WaitMenu();
                }
                var final=Object.FindFirstObjectByType<MainMenuBootstrap>();
                final.Panel.GetComponentsInChildren<WorkshopButton>().Single(b=>b.GetComponentInChildren<TextMesh>().text=="FINISH TEST").Press();
                Assert.IsTrue(capture.Finished);
                var lines=File.ReadAllLines(Path.Combine(capture.CaptureFolder,"samples.csv"));
                Assert.Greater(lines.Length,10);foreach(int n in Enumerable.Range(1,5))Assert.IsTrue(lines.Skip(1).Any(s=>s.Split(',')[1]==n.ToString()&&s.Split(',')[3]=="Running"),"Missing driving sample "+n);
                Directory.CreateDirectory("Logs/ArtFixes-20261117");File.Copy(Path.Combine(capture.CaptureFolder,"samples.csv"),"Logs/ArtFixes-20261117/Editor-ManualFlow.csv",true);
            }finally{if(capture)Object.Destroy(capture.gameObject);DesignModeSession.SetEnabled(false);}
        }
        static IEnumerator WaitMenu(){float end=Time.realtimeSinceStartup+30;MainMenuBootstrap menu;do{yield return null;menu=Object.FindFirstObjectByType<MainMenuBootstrap>();}while((!menu||!menu.Panel||!menu.Panel.GetComponentsInChildren<TextMesh>().Any(t=>t.text=="FINISH TEST"))&&Time.realtimeSinceStartup<end);Assert.IsNotNull(menu);yield return new WaitForSecondsRealtime(.55f);}
        static void Capture(SandboxFactoryLevel f,int n,int lod)
        {
            var camera=new GameObject("Runtime tray review").AddComponent<Camera>();var sun=new GameObject("Runtime tray light").AddComponent<Light>();
            var hidden=f.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Horizon transition islands"||t.name=="Layered realistic cloud banks"||t==f.Layout.BuildingRoot||t.name.Contains("(editor only)")).ToArray();var states=hidden.Select(t=>t.gameObject.activeSelf).ToArray();
            var rt=new RenderTexture(1500,1000,24){antiAliasing=4};rt.Create();var old=RenderTexture.active;
            try{
                foreach(var t in hidden)t.gameObject.SetActive(false);
                var tray=f.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="01 Display base");var rr=tray.GetComponentsInChildren<Renderer>();var bounds=rr[0].bounds;foreach(var r in rr)bounds.Encapsulate(r.bounds);
                var d=f.Layout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance).position-bounds.center;d.y=0;d=d.normalized;d.y=.22f;
                camera.orthographic=true;camera.aspect=1.5f;camera.orthographicSize=Mathf.Max(bounds.size.y,Mathf.Max(bounds.size.x,bounds.size.z)*.85f)*1.12f*.55f;
                camera.transform.position=bounds.center+d.normalized*Mathf.Max(bounds.size.magnitude*2,2);camera.transform.LookAt(bounds.center);camera.nearClipPlane=.01f;camera.farClipPlane=200;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.21f,.24f);
                sun.type=LightType.Directional;sun.intensity=1.1f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;var tex=new Texture2D(1500,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1500,1000),0,0);tex.Apply();Directory.CreateDirectory("Logs/ArtPolish-20261117");File.WriteAllBytes($"Logs/ArtPolish-20261117/RuntimeTray-Level{n}-LOD{lod}.png",tex.EncodeToPNG());Object.Destroy(tex);
            }finally{for(int i=0;i<hidden.Length;i++)hidden[i].gameObject.SetActive(states[i]);RenderTexture.active=old;rt.Release();Object.Destroy(rt);Object.Destroy(camera.gameObject);Object.Destroy(sun.gameObject);}
        }
    }
}
#endif
