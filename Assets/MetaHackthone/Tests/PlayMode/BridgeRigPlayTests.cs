#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PhysicalAssembly.Tests
{
    public sealed class BridgeRigPlayTests
    {
        [UnityTest]
        public IEnumerator PolishedFinalCourseDrillCarCrossesBridgeRepeatedly()
        {
            DownhillController level = null;
            try
            {
                DesignModeSession.SetEnabled(true); DesignModeSession.SelectLevel(DesignLevelId.FactoryFinalRun);
                UnityEngine.SceneManagement.SceneManager.LoadScene("DownhillSandbox"); yield return null; yield return null;
                level = Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
                var car = AssetDatabase.LoadAssetAtPath<SandboxTestCarAsset>("Assets/MetaHackthone/Generated/LevelFactory/Vehicles/Level 5 drill car.asset");
                Assert.IsNotNull(car); Assert.IsTrue(level.TryLoadFactoryCar(car));
                level.Settings.CountdownStep = .01f; level.Settings.GoDuration = .01f; level.Settings.TransitionDuration = 0;
                level.LaunchDecomposeSeconds = level.LaunchTravelSeconds = level.LaunchReconstructSeconds = .01f;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    yield return null; Assert.IsTrue(level.TryStart());
                    typeof(DownhillController).GetProperty("GarageComparing").SetValue(level, true);
                    float deadline = Time.realtimeSinceStartup + 8;
                    while (level.State != DownhillState.Running && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.AreEqual(DownhillState.Running, level.State);
                    deadline = Time.realtimeSinceStartup + 65;
                    while (level.State == DownhillState.Running && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.AreEqual(DownhillState.Success, level.State, "Authored Level 5 drill car, attempt " + attempt);
                    Assert.IsTrue(level.FactoryLevel.GetComponentInChildren<SandboxDropBridge>(true).Released);
                    typeof(DownhillController).GetProperty("GarageComparing").SetValue(level, false);
                    level.RestoreBuildImmediately(); yield return null;
                }
            }
            finally
            {
                if (level) { typeof(DownhillController).GetProperty("GarageComparing").SetValue(level, false); level.RestoreBuildImmediately(); }
                DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset(); UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            }
        }
        [UnityTest]
        public IEnumerator DrillReleaseAnimatesDeckRopesAndLatchThenResets()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MetaHackthone/Generated/Campaign/Level4.prefab");
            var root = Object.Instantiate(prefab); root.transform.position += Vector3.right * 100;
            var bridge = root.GetComponentInChildren<SandboxDropBridge>(true);
            var rig = bridge.GetComponent<SandboxBridgeRig>();
            bridge.ResetPuzzle(true); Physics.SyncTransforms();
            var camera = new GameObject("Bridge verification camera").AddComponent<Camera>();
            var sun = new GameObject("Bridge verification sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(45,-35,0); sun.intensity = 1.4f;
            var rr = bridge.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var bounds = rr[0].bounds; foreach(var r in rr) bounds.Encapsulate(r.bounds);
            camera.orthographic = true; camera.orthographicSize = bounds.size.magnitude * .43f; camera.aspect = 1.5f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 20; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.15f,.21f,.24f);
            camera.transform.position = bounds.center + bridge.transform.TransformDirection(new Vector3(1.1f,.65f,-.25f)).normalized * 4;
            camera.transform.LookAt(bounds.center);
            var rt = new RenderTexture(1200,800,24) { antiAliasing = 4 }; rt.Create();
            Directory.CreateDirectory("Logs/ArtPolish-20261117");
            void Capture(string name)
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                var old = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(1200,800,TextureFormat.RGB24,false);
                tex.ReadPixels(new Rect(0,0,1200,800),0,0); tex.Apply(); File.WriteAllBytes("Logs/ArtPolish-20261117/"+name+".png",tex.EncodeToPNG());
                RenderTexture.active=old; Object.Destroy(tex);
            }
            try
            {
                bridge.ResetPuzzle(true); int frame = Time.frameCount;
                yield return null; yield return null;
                Assert.Greater(Time.frameCount, frame, "The player loop must advance");
                var raised = bridge.Deck.rotation; var lever = rig.ReleaseLever.localRotation;
                Capture("Bridge-01-Locked");
                bridge.DrillHit(bridge.TargetCollider.bounds.center);
                yield return new WaitForSeconds(.15f); Capture("Bridge-02-Releasing");
                yield return new WaitForSeconds(.8f); Capture("Bridge-03-Landed");
                Assert.Greater(Quaternion.Angle(raised, bridge.Deck.rotation), 20);
                Assert.Greater(Quaternion.Angle(lever, rig.ReleaseLever.localRotation), 40);
                for (int i=0;i<rig.Cables.Length;i++) Assert.Less(Vector3.Distance(rig.Cables[i].GetPosition(0),rig.DeckEyes[i].position),.001f);
                Assert.IsTrue(bridge.Released); Assert.IsFalse(bridge.TargetBody.gameObject.activeSelf);
                bridge.ResetPuzzle(true); yield return null;
                Assert.Less(Quaternion.Angle(raised,bridge.Deck.rotation),.01f);
                Assert.Less(Quaternion.Angle(lever,rig.ReleaseLever.localRotation),.01f);
                Assert.IsTrue(bridge.TargetBody.gameObject.activeSelf); Capture("Bridge-04-Reset");
            }
            finally { rt.Release(); Object.Destroy(rt); Object.Destroy(camera.gameObject); Object.Destroy(sun.gameObject); Object.Destroy(root); }
        }
    }
}
#endif
