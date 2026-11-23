using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    // Campaign scenes are the authoring source. Their runtime prefabs are snapshots,
    // never an opportunity to regenerate the scene's objects.
    [InitializeOnLoad]
    public static class AuthoredCampaignSaving
    {
        const string Scenes = "Assets/MetaHackthone/Scenes/Level";
        const string Campaign = "Assets/MetaHackthone/Generated/Campaign/Level";
        static bool saving;

        static AuthoredCampaignSaving()
        {
            EditorSceneManager.sceneSaving += (scene, path) => {
                if(Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || Number(path)==0) return;
                var factory=SandboxFactoryAuthoring.Find(scene);
                if(factory && factory.AuthoredObjects) PersistResources(factory.gameObject);
            };
            EditorSceneManager.sceneSaved += scene => {
                if(Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || saving) return;
                try { SaveToGame(scene); }
                catch(Exception error) { Debug.LogError("Scene saved, but its game copy could not be updated: "+error); }
            };
        }

        static int Number(string path)
        {
            for(int i=1;i<=5;i++) if(path==Scenes+i+".unity") return i;
            return 0;
        }

        public static void SaveToGame(Scene scene)
        {
            int number=Number(scene.path);
            if(number==0 || saving) return;
            var factory=SandboxFactoryAuthoring.Find(scene);
            if(!factory || !factory.AuthoredObjects) return;
            saving=true;
            try {
                // sceneSaving already flushed references and persisted resources.
                // Do not dirty or mutate the source again from sceneSaved.
                if(!factory.Validate(out var error)) throw new InvalidOperationException(error);
                PersistResources(factory.gameObject);
                var prefab=SandboxFactoryCampaignSave.Export(factory,Campaign+number+".prefab",Campaign+number+"Meshes",factory.Title);
                foreach(string guid in AssetDatabase.FindAssets("t:DownhillSettings",new[]{"Assets/MetaHackthone"})) {
                    var settings=AssetDatabase.LoadAssetAtPath<DownhillSettings>(AssetDatabase.GUIDToAssetPath(guid));
                    switch(number) {
                        case 1: settings.Level1=prefab; break;
                        case 2: settings.CrabBridgeCourse=prefab; break;
                        case 3: settings.WheelSizeCourse=prefab; break;
                        case 4: settings.DrillBridgeCourse=prefab; break;
                        case 5: settings.Level4=prefab; break;
                    }
                    EditorUtility.SetDirty(settings);
                }
                AssetDatabase.SaveAssets();
            } finally { saving=false; }
        }

        // Keep existing asset identities. Persist only newly authored transient resources,
        // by object identity (mesh names are not unique), including inactive objects.
        public static void PersistResources(GameObject root)
        {
            string scene=root.scene.path;
            var copies=new Dictionary<Object,Object>();
            Object Saved(Object source)
            {
                if(!source || AssetDatabase.Contains(source)) return source;
                if(copies.TryGetValue(source,out var saved)) return saved;
                if(string.IsNullOrEmpty(scene)) throw new InvalidOperationException("Save new meshes and materials in the authoring scene before exporting.");
                string folder=System.IO.Path.ChangeExtension(scene,null)+"_Factory/AuthoredResources";
                SandboxTrackBaker.EnsureFolder(folder);
                saved=Object.Instantiate(source); saved.name=source.name;
                AssetDatabase.CreateAsset(saved,AssetDatabase.GenerateUniqueAssetPath(folder+"/"+(source is Mesh?"Mesh.asset":"Material.mat")));
                copies.Add(source,saved);
                return saved;
            }
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true)) {
                var mesh=(Mesh)Saved(filter.sharedMesh);
                if(mesh!=filter.sharedMesh) { filter.sharedMesh=mesh; EditorUtility.SetDirty(filter); }
            }
            foreach(var collider in root.GetComponentsInChildren<MeshCollider>(true)) {
                var mesh=(Mesh)Saved(collider.sharedMesh);
                if(mesh!=collider.sharedMesh) { collider.sharedMesh=mesh; EditorUtility.SetDirty(collider); }
            }
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)) {
                var materials=renderer.sharedMaterials;
                var saved=materials.Select(m=>(Material)Saved(m)).ToArray();
                if(!materials.SequenceEqual(saved)) { renderer.sharedMaterials=saved; EditorUtility.SetDirty(renderer); }
            }
        }
    }
}
