using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class LightweightBoundaryAuthoring
    {
        public const string ShaderName="Creek Diorama/Lightweight Range Frame";
        const string Root="Assets/CreekDiorama";
        static Mesh walls, floor;
        static Material material;

        public static void BuildAssets()
        {
            material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Lightweight range frame.mat");
            if(!material) { material=new Material(Shader.Find(ShaderName)); AssetDatabase.CreateAsset(material,Root+"/Materials/Lightweight range frame.mat"); }
            walls=BuildMesh(false); floor=BuildMesh(true);
        }
        static Mesh BuildMesh(bool bottom)
        {
            var vertices=new List<Vector3>(); var normals=new List<Vector3>(); var colors=new List<Color>(); var triangles=new List<int>();
            void Beam(Vector3 a,Vector3 b,bool corner)
            {
                var direction=(b-a).normalized;
                var u=Mathf.Abs(direction.y)>.5f?Vector3.right:Vector3.up;
                var v=Vector3.Cross(direction,u);
                int start=vertices.Count;
                foreach(var p in new[]{a,b}) foreach(var offset in new[]{u+v,-u+v,-u-v,u-v}) {
                    vertices.Add(p); normals.Add(offset*(corner?1.5f:1)); colors.Add(corner?Color.white:Color.black);
                }
                for(int i=0;i<4;i++) { int j=(i+1)%4; triangles.AddRange(new[]{start+i,start+j,start+j+4,start+i,start+j+4,start+i+4}); }
                triangles.AddRange(new[]{start,start+2,start+1,start,start+3,start+2,start+4,start+5,start+6,start+4,start+6,start+7});
            }
            for(int axis=0;axis<3;axis++)for(int a=-1;a<=1;a+=2)for(int b=-1;b<=1;b+=2) {
                var p=Vector3.zero; p[axis]=-.5f; p[(axis+1)%3]=a*.5f; p[(axis+2)%3]=b*.5f;
                var q=p; q[axis]=.5f;
                if((p.y==-.5f && q.y==-.5f)!=bottom) continue;
                var d=q-p;
                Beam(p,p+d*.13f,true); Beam(p+d*.13f,q-d*.13f,false); Beam(q-d*.13f,q,true);
            }
            var mesh=new Mesh{name=bottom?"Lightweight range floor perimeter":"Lightweight range upper edges"};
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors); mesh.SetTriangles(triangles,0);
            mesh.bounds=new Bounds(Vector3.zero,Vector3.one*1.08f);
            string path=Root+"/Meshes/Effects/"+mesh.name+".asset";
            var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(old) { EditorUtility.CopySerialized(mesh,old); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(old); return old; }
            AssetDatabase.CreateAsset(mesh,path); return mesh;
        }
        public static void Convert(Transform artwork)
        {
            if(!artwork)return;
            if(!material)BuildAssets();
            // Keep the root and tabletop identities referenced by gameplay.
            var surface=artwork.Find("Assembly tabletop - shared footprint");
            if(!surface) { surface=new GameObject("Assembly tabletop - shared footprint").transform; surface.SetParent(artwork,false); }
            var neon=artwork.Find("Neon rounded edge core");
            if(neon)Object.DestroyImmediate(neon.gameObject);
            Set(artwork,walls); Set(surface,floor);
            foreach(var volume in artwork.GetComponentsInParent<SandboxBuildVolume>(true)) {
                volume.Tabletop=surface.GetComponent<Renderer>(); volume.ShowWalls=true; volume.Rebuild(); EditorUtility.SetDirty(volume);
            }
        }
        static void Set(Transform root,Mesh mesh)
        {
            var filter=root.GetComponent<MeshFilter>();if(!filter)filter=root.gameObject.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
            var renderer=root.GetComponent<MeshRenderer>();if(!renderer)renderer=root.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;renderer.enabled=true;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;renderer.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
        }
        static void ConvertFactory(SandboxFactoryLevel factory)
        {
            Convert(factory.WorkbenchVisuals);
            var area=factory.Layout.Course.BuildArea;
            if(!area)return;
            var volume=area.GetComponent<SandboxBuildVolume>();
            if(!volume)volume=area.gameObject.AddComponent<SandboxBuildVolume>();
            volume.ShowWalls=true;volume.Rebuild();Convert(volume.Artwork);EditorUtility.SetDirty(volume);
        }
        [MenuItem("MetaHackthone/Creek Art/Use lightweight building range")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode first.");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var setup=EditorSceneManager.GetSceneManagerSetup();bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
            try {
                BuildAssets();
                foreach(var path in new[]{"Assets/MetaHackthone/Resources/SandboxBuildingBoundary.prefab",Root+"/Prefabs/AssemblyBounds.prefab"}) {
                    var contents=PrefabUtility.LoadPrefabContents(path);
                    try { Convert(contents.transform); PrefabUtility.SaveAsPrefabAsset(contents,path); } finally { PrefabUtility.UnloadPrefabContents(contents); }
                }
                for(int n=1;n<=5;n++) {
                    var scene=EditorSceneManager.OpenScene($"Assets/MetaHackthone/Scenes/Level{n}.unity");var f=SandboxFactoryAuthoring.Find(scene);
                    var before=CarSceneryAuthoring.GameplaySnapshot(f);ConvertFactory(f);
                    if(CarSceneryAuthoring.GameplaySnapshot(f)!=before)throw new Exception("Boundary migration changed gameplay");
                    EditorSceneManager.SaveScene(scene);
                    string path=$"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab";var contents=PrefabUtility.LoadPrefabContents(path);
                    try { f=contents.GetComponent<SandboxFactoryLevel>();before=CarSceneryAuthoring.GameplaySnapshot(f);ConvertFactory(f);if(CarSceneryAuthoring.GameplaySnapshot(f)!=before)throw new Exception("Boundary migration changed gameplay");PrefabUtility.SaveAsPrefabAsset(contents,path); }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                }
                var sandbox=EditorSceneManager.OpenScene("Assets/MetaHackthone/Scenes/DownhillSandbox.unity");
                foreach(var volume in sandbox.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<SandboxBuildVolume>(true))) { volume.ShowWalls=true;volume.Rebuild();Convert(volume.Artwork);EditorUtility.SetDirty(volume); }
                EditorSceneManager.SaveScene(sandbox);AssetDatabase.SaveAssets();
                Debug.Log("LIGHTWEIGHT_BOUNDARY_READY: opaque geometric edges, no transparent faces, no texture sampling or postprocessing.");
            } finally { SandboxTrackEditing.Suspended=suspended;if(setup.Any(s=>s.isLoaded))EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
    }
}
