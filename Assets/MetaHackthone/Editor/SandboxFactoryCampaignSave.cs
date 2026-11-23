using System;
using System.Linq;
using CloudGarden.Integration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class SandboxFactoryCampaignSave
    {
        public const string ScenePath="Assets/MetaHackthone/Scenes/Level5.unity";
        public const string PrefabPath="Assets/MetaHackthone/Generated/Campaign/Level5.prefab";
        const string GameScene="Assets/MetaHackthone/Scenes/DownhillSandbox.unity";

        [MenuItem("MetaHackthone/Level Factory/Save Level 5 to Game")]
        public static void SaveLevel4()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before saving Level 4.");
            var scene=SceneManager.GetActiveScene();
            var factory=SandboxFactoryAuthoring.Find(scene);
            if(!factory) throw new InvalidOperationException("Open the factory level scene first.");
            if(!SandboxTrackEditing.Flush(scene,true) || !factory.Validate(out _))
                throw new InvalidOperationException("Fix factory validation errors before saving to the game.");
            if(scene.path!=ScenePath && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                throw new InvalidOperationException("Level4 already exists. Open Level5.unity, make your changes there, then Save Level 5 to Game.");
            if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the factory scene.");
            if(scene.path!=ScenePath) {
                if(!AssetDatabase.CopyAsset(scene.path,ScenePath)) throw new InvalidOperationException("Could not create Level5.unity.");
                var source=scene;
                scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                SandboxFactoryAuthoring.MakeIndependent(scene,ScenePath);
                factory=SandboxFactoryAuthoring.Find(scene);
                factory.Title="Factory Final Run";
                EditorSceneManager.SaveScene(scene);
                EditorSceneManager.CloseScene(source,true);
            }
            var prefab=Export(factory);
            // Register the exported course on the settings used by the actual Sandbox entry.
            var game=SceneManager.GetSceneByPath(GameScene);
            bool opened=!game.isLoaded;
            if(opened) game=EditorSceneManager.OpenScene(GameScene,OpenSceneMode.Additive);
            try {
                var entry=game.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DownhillEntry>(true)).Single();
                Undo.RecordObject(entry.Settings,"Save factory Level 4 to game");
                entry.Settings.Level4=prefab;
                EditorUtility.SetDirty(entry.Settings);
                AssetDatabase.SaveAssets();
            } finally {
                if(opened) EditorSceneManager.CloseScene(game,true);
                SceneManager.SetActiveScene(scene);
            }
            Selection.activeGameObject=factory.gameObject;
            Debug.Log("Level 4 saved to the game. Sandbox progression: Level 1 → Level 2 → Level 3 → Level 4.");
        }

        [MenuItem("MetaHackthone/Level Factory/Save Level 1 to Game")]
        public static void SaveLevel1() => BeautifulCornerDesignAuthoring.SaveCurrent();

        public static SandboxFactoryLevel Export(SandboxFactoryLevel source) => Export(source,PrefabPath,"Assets/MetaHackthone/Generated/Campaign/Level5Meshes","Level 5 - Factory Final Run");

        public static SandboxFactoryLevel Export(SandboxFactoryLevel source,string prefabPath,string meshesFolder,string title)
        {
            SceneryMotionPreview.Stop();
            if(!source.Validate(out var error)) throw new InvalidOperationException(error);
            var layout=source.GetComponent<BuildingSceneLayout>();
            if(!layout || layout.Spawns.Count(s=>s && s.Kind==PartKind.DownhillCore)!=1)
                throw new InvalidOperationException("The factory palette needs exactly one Core marker.");
            if(source.Layout && (new[]{source.TrackRoot,source.WorkbenchVisuals}.Concat(source.Scenery).Where(t=>t).Any(t=>!t.IsChildOf(source.transform)) ||
                layout.Spawns.Where(s=>s).Any(s=>!s.transform.IsChildOf(source.transform))))
                throw new InvalidOperationException("Keep referenced scenery, workbench art and spawn markers inside the editable level hierarchy before exporting.");
            SandboxTrackBaker.EnsureFolder(System.IO.Path.GetDirectoryName(prefabPath).Replace('\\','/'));
            var preview=EditorSceneManager.NewPreviewScene();
            bool suspended=SandboxTrackEditing.Suspended; SandboxTrackEditing.Suspended=true;
            GameObject clone=null;
            try {
                // Clone together so linked targets, bridges, spline bindings and palette references remap.
                clone=Object.Instantiate(source.gameObject);
                SceneManager.MoveGameObjectToScene(clone,preview);
                clone.name=title;
                var factory=clone.GetComponent<SandboxFactoryLevel>();
                factory.CampaignSpawns=clone.GetComponent<BuildingSceneLayout>().Spawns;
                if(factory.Layout) {
                    if(!factory.TrackRoot.IsChildOf(factory.Layout.SandboxRoot)) factory.TrackRoot.SetParent(factory.Layout.Course.SandboxFrame,true);
                    if(factory.WorkbenchVisuals && !factory.WorkbenchVisuals.IsChildOf(factory.Layout.BuildingRoot)) factory.WorkbenchVisuals.SetParent(factory.Layout.BuildingRoot,true);
                    foreach(var scenery in factory.Scenery.Where(t=>t && t!=factory.WorkbenchVisuals))
                        if(!scenery.IsChildOf(factory.Layout.SandboxRoot)) scenery.SetParent(factory.Layout.Course.SandboxFrame,true);
                    foreach(var spawn in factory.CampaignSpawns.Where(s=>s)) {
                        if(!spawn.transform.IsChildOf(factory.Layout.BuildingRoot)) spawn.transform.SetParent(factory.Layout.BuildingRoot,true);
                        spawn.gameObject.SetActive(true);
                        if(spawn.Preview) Object.DestroyImmediate(spawn.Preview);
                    }
                    if(!factory.AuthoredObjects)
                        foreach(Transform child in clone.transform.Cast<Transform>().ToArray())
                            if(child!=factory.Layout.transform) Object.DestroyImmediate(child.gameObject);
                } else {
                factory.TrackRoot.SetParent(clone.transform,true);
                if(factory.WorkbenchVisuals) factory.WorkbenchVisuals.SetParent(clone.transform,true);
                foreach(var scenery in factory.Scenery.Where(t=>t))
                    if(!scenery.IsChildOf(factory.TrackRoot) && scenery!=factory.WorkbenchVisuals) scenery.SetParent(clone.transform,true);
                var palette=new GameObject("Factory palette").transform; palette.SetParent(clone.transform,false);
                foreach(var spawn in factory.CampaignSpawns.Where(s=>s)) {
                    float size=spawn.SizeMultiplier;
                    spawn.transform.SetParent(palette,true); spawn.transform.localScale=Vector3.one*size;
                    spawn.gameObject.SetActive(true);
                    if(spawn.Preview) Object.DestroyImmediate(spawn.Preview);
                }
                foreach(Transform child in clone.transform.Cast<Transform>().ToArray())
                    if(child!=factory.TrackRoot && child!=palette && child!=factory.WorkbenchVisuals && !factory.Scenery.Contains(child)) Object.DestroyImmediate(child.gameObject);
                }
                foreach(var component in clone.GetComponents<Component>())
                    if(!(component is Transform) && component!=factory) Object.DestroyImmediate(component);
                clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); clone.transform.localScale=Vector3.one;
                // Keep the game snapshot's meshes separate from subsequent authoring edits.
                if(!factory.Validate(out error)) throw new InvalidOperationException(error);
                var previous=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>(prefabPath);
                for(int i=0;i<factory.Tracks.Length;i++) {
                    var track=factory.Tracks[i];
                    track.BakeOwner=prefabPath;
                    track.MeshAssetPath=previous && i<previous.Tracks.Length?previous.Tracks[i].MeshAssetPath:"";
                    // Reparenting changes the support-raycast signature, but the saved geometry
                    // is already valid. Preserve it instead of raycasting an empty preview scene.
                    track.BakedSignature=SandboxTrackBaker.Signature(track);
                    SandboxTrackBaker.Persist(track,meshesFolder,prefabPath);
                }
                if(!factory.Validate(out error)) throw new InvalidOperationException(error);
                var asset=PrefabUtility.SaveAsPrefabAsset(clone,prefabPath);
                if(!asset) throw new InvalidOperationException("Could not save the course prefab.");
                return asset.GetComponent<SandboxFactoryLevel>();
            } finally {
                if(clone) Object.DestroyImmediate(clone);
                EditorSceneManager.ClosePreviewScene(preview);
                SandboxTrackEditing.Suspended=suspended;
            }
        }
    }
}
