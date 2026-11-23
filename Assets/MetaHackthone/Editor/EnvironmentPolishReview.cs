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

namespace PhysicalAssembly.Editor
{
    public static class EnvironmentPolishReview
    {
        public const string Output = "Logs/ArtPolish-20261117";
        public static void PlayerView(SandboxFactoryLevel factory,string name,float pitch=0)
        {
            Directory.CreateDirectory(Output);
            var camera=new GameObject("Player composition review").AddComponent<Camera>();
            var sun=new GameObject("Player review light").AddComponent<Light>();
            var oldMode=RenderSettings.ambientMode;var oldAmbient=RenderSettings.ambientLight;
            sun.type=LightType.Directional;sun.intensity=1.1f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.60f,.64f);
            var pose=factory.Layout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance);
            camera.transform.SetPositionAndRotation(pose.position,pose.rotation*Quaternion.Euler(pitch,0,0));
            camera.fieldOfView=75;camera.aspect=1.5f;camera.nearClipPlane=.02f;camera.farClipPlane=200;
            camera.clearFlags=CameraClearFlags.Skybox;camera.gameObject.AddComponent<Skybox>().material=Resources.Load<Material>("CarCampaignSky");
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture=false;camera.GetUniversalAdditionalCameraData().requiresColorTexture=false;
            var helpers=factory.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("(editor only)")).ToArray();
            var helperStates=helpers.Select(t=>t.gameObject.activeSelf).ToArray();
            foreach(var t in helpers)t.gameObject.SetActive(false);
            var rt=new RenderTexture(1500,1000,24,RenderTextureFormat.ARGB32){antiAliasing=4};rt.Create();var old=RenderTexture.active;
            try {RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(Output+"/"+name+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);}
            finally {for(int i=0;i<helpers.Length;i++)helpers[i].gameObject.SetActive(helperStates[i]);RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(sun.gameObject);RenderSettings.ambientMode=oldMode;RenderSettings.ambientLight=oldAmbient;}
        }
        public static void Capture(Transform root, string name, Vector3 direction, float padding = 1.15f)
        {
            Directory.CreateDirectory(Output);
            var rr = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (rr.Length == 0) return;
            var bounds = rr[0].bounds;
            foreach (var r in rr) bounds.Encapsulate(r.bounds);
            CaptureBounds(bounds, name, direction, padding);
        }
        public static void CaptureBounds(Bounds bounds, string name, Vector3 direction, float padding = 1.15f)
        {
            Directory.CreateDirectory(Output);
            var camera = new GameObject("Polish review camera").AddComponent<Camera>();
            var sun = new GameObject("Polish review sun").AddComponent<Light>();
            var oldMode = RenderSettings.ambientMode; var oldAmbient = RenderSettings.ambientLight;
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(48, -30, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f, .60f, .64f);
            camera.orthographic = true; camera.aspect = 1.5f;
            camera.orthographicSize = Mathf.Max(bounds.size.y, Mathf.Max(bounds.size.x, bounds.size.z) * .85f) * padding * .55f;
            camera.nearClipPlane = .001f; camera.farClipPlane = 200;
            camera.transform.position = bounds.center + direction.normalized * Mathf.Max(bounds.size.magnitude * 2, 2);
            camera.transform.LookAt(bounds.center);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.15f, .21f, .24f);
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = false;
            camera.GetUniversalAdditionalCameraData().requiresColorTexture = false;
            var rt = new RenderTexture(1500, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create(); var old = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            }
            finally
            {
                RenderTexture.active = old; rt.Release(); Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(sun.gameObject);
                RenderSettings.ambientMode = oldMode; RenderSettings.ambientLight = oldAmbient;
            }
        }
        public static void Audit(string label)
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            if (setup.Any(s => UnityEngine.SceneManagement.SceneManager.GetSceneByPath(s.path).isDirty)) throw new InvalidOperationException("Save scenes before review.");
            bool suspended = SandboxTrackEditing.Suspended; SandboxTrackEditing.Suspended = true;
            var report = new StringBuilder();
            try
            {
                foreach (var name in new[] { "Prop_Barrel", "Tree_Round_Canopy" })
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CreekDiorama/Prefabs/Botanical/" + name + ".prefab"));
                    Capture(go.transform, label + "-" + name, new Vector3(1, .8f, -1));
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) report.AppendLine(name + " " + r.name + " " + r.sharedMaterial.shader.name + " enabled=" + r.enabled);
                }
                for (int level = 1; level <= 5; level++)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab"));
                    var f = go.GetComponent<SandboxFactoryLevel>();
                    CarSceneryReview.Capture(f, Output + $"/{label}-Level{level}.png");
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name.Contains("curtain") || t.name.Contains("spring") || t.name.Contains("Spring") || t.name.Contains("Tree") || t.name.Contains("Barrel"))
                            report.AppendLine(level + " " + AnimationUtility.CalculateTransformPath(t, go.transform) + " pos=" + t.position + " scale=" + t.lossyScale + " active=" + t.gameObject.activeInHierarchy);
                    }
                    var bridge = go.GetComponentInChildren<SandboxDropBridge>(true);
                    if (bridge) Capture(bridge.transform, label + "-Bridge-Level" + level, bridge.transform.TransformDirection(new Vector3(1.2f, .9f, -.4f)), 1.25f);
                }
                File.WriteAllText(Output + "/" + label + "-Audit.txt", report.ToString());
            }
            finally { SandboxTrackEditing.Suspended = suspended; EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
    }
}
