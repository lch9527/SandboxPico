using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class BoundaryReviewBuild
    {
        const string Temporary="Assets/MetaHackthone/Resources/BoundaryReviewLegacy.prefab";
        public static GameObject Legacy()
        {
            var root=new GameObject("Legacy full transparent boundary");
            Add(root,"15100f05bdbd5394e9994c1e5cef4b0e","f80f153d0af99d14a95ad1dad732427b");
            var table=new GameObject("Assembly tabletop - shared footprint");table.transform.SetParent(root.transform,false);Add(table,"5bfdfc1af34f242408f7009b84d6e36f","2abc635b9699dfb46ba832fe6487c3d4");
            var neon=new GameObject("Neon rounded edge core");neon.transform.SetParent(root.transform,false);Add(neon,"0bf31904a36d08840b5043fa70ce2c08","8de967b534797844681166bfc798ac5d");
            return root;
        }
        static void Add(GameObject go,string mesh,string material)
        {
            go.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(mesh));
            var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(material));r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            if(!go.GetComponent<MeshFilter>().sharedMesh || !r.sharedMaterial)throw new Exception("Missing legacy reference");
        }
        public static void Capture()
        {
            Directory.CreateDirectory("Logs/SceneryClearance");var setup=EditorSceneManager.GetSceneManagerSetup();
            try {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var camera=new GameObject("Range comparison").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.fieldOfView=70;camera.nearClipPlane=.01f;camera.farClipPlane=10;camera.transform.position=new Vector3(0,.16f,.9f);camera.transform.LookAt(Vector3.zero);camera.allowHDR=false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                var report=new System.Text.StringBuilder("variant,renderers,triangles,nonblack_pixels,total_pixels\n");
                foreach(bool old in new[]{true,false}) {
                    var go=old?Legacy():Object.Instantiate(Resources.Load<GameObject>(SandboxBuildVolume.ArtworkResource));go.transform.localScale=new Vector3(.8f,.45f,.7f);
                    var rt=new RenderTexture(1200,800,24);rt.Create();var previous=RenderTexture.active;var image=new Texture2D(1200,800,TextureFormat.RGB24,false);
                    try {
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1200,800),0,0);image.Apply();
                        string name=old?"Legacy":"Lightweight";File.WriteAllBytes("Logs/SceneryClearance/Boundary-"+name+".png",image.EncodeToPNG());
                        int pixels=image.GetPixels32().Count(p=>p.r>1 || p.g>1 || p.b>1);
                        int triangles=go.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3);
                        report.AppendLine($"{name},{go.GetComponentsInChildren<Renderer>().Length},{triangles},{pixels},960000");
                    }finally{RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(go);}
                }
                File.WriteAllText("Logs/SceneryClearance/Boundary-geometry.csv",report.ToString());
            }finally{if(setup.Any(s=>s.isLoaded))EditorSceneManager.RestoreSceneManagerSetup(setup);}
        }
        public static void Build()
        {
            AndroidBuildValidation.ValidateTarget(BuildTarget.Android,EditorUserBuildSettings.activeBuildTarget);
            Capture();
            if(AssetDatabase.LoadAssetAtPath<GameObject>(Temporary))throw new Exception("Diagnostic resource already exists; inspect before replacing.");
            var target=NamedBuildTarget.Android;string id=PlayerSettings.GetApplicationIdentifier(target),product=PlayerSettings.productName;
            var legacy=Legacy();
            try {
                PrefabUtility.SaveAsPrefabAsset(legacy,Temporary);Object.DestroyImmediate(legacy);
                PlayerSettings.SetApplicationIdentifier(target,"com.metahackathon2026.boundaryreview");PlayerSettings.productName="Boundary Review";
                Directory.CreateDirectory("Builds");AssetDatabase.SaveAssets();
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName="Builds/BoundaryReview.apk",target=BuildTarget.Android,options=BuildOptions.Development|BuildOptions.CompressWithLz4|BuildOptions.DetailedBuildReport });
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Boundary review build failed: "+report.summary.result);
                Debug.Log("BOUNDARY_REVIEW_APK_READY "+report.summary.totalSize);
            } finally {
                if(legacy)Object.DestroyImmediate(legacy);
                PlayerSettings.SetApplicationIdentifier(target,id);PlayerSettings.productName=product;
                AssetDatabase.DeleteAsset(Temporary);AssetDatabase.SaveAssets();
            }
        }
    }
}
