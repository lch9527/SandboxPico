using System.Linq;
using CloudGarden.Integration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAssembly.Editor
{
    // Saved palettes predate the Blender kit. Keep their editor-only meshes on the
    // same assets as the runtime factory, including after opening an older level.
    [InitializeOnLoad]
    public static class SandboxPartPreviewSync
    {
        static bool queued;
        static double refreshAfter;
        static SandboxPartPreviewSync()
        {
            EditorApplication.hierarchyChanged+=Queue;
            EditorApplication.projectChanged+=Queue;
            Undo.undoRedoPerformed+=Queue;
            EditorSceneManager.sceneOpened+=(_,__)=>Queue();
            EditorApplication.update+=RefreshLoaded;
            Queue();
        }

        public static void Queue()
        {
            queued=true;
            refreshAfter=EditorApplication.timeSinceStartup+.25;
        }

        static void RefreshLoaded()
        {
            if(!queued || EditorApplication.timeSinceStartup<refreshAfter) return;
            // Imports and domain reloads can still be settling when the first update arrives.
            // Keep the request pending until the editor is ready, rather than dropping it.
            if(EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            queued=false;
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            for(int i=0;i<SceneManager.sceneCount;i++) {
                var scene=SceneManager.GetSceneAt(i);
                if(scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene)) Refresh(scene);
            }
        }

        public static int Refresh(Scene scene)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return 0;
            var library=CarPartArtLibrary.Load();
            if(!library || !library.Enabled) return 0;
            int changed=0;
            foreach(var entry in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DownhillEntry>(true))) {
                if(!entry.Settings || !entry.Settings.DesignerBuilder || !entry.Layout) continue;
                var layout=entry.Layout;
                var spawns=(layout.Spawns??System.Array.Empty<BuildingPartSpawn>())
                    .Concat((layout.LevelLayouts??System.Array.Empty<SandboxLevelLayout>()).Where(l=>l)
                        .SelectMany(l=>l.Spawns??System.Array.Empty<BuildingPartSpawn>())).Where(s=>s).Distinct().ToArray();
                GameObject factory=null;
                try {
                    foreach(var spawn in spawns) {
                        var visuals=library.Visuals.Where(v=>v.Kind==spawn.Kind).ToArray();
                        // Only auto-replace previews backed entirely by persistent art assets.
                        // Procedural/legacy kits retain their existing explicit authoring path.
                        if(visuals.Length==0 || visuals.Any(v=>v.Lods==null || v.Lods.Length==0 || !v.Lods[0] ||
                            !EditorUtility.IsPersistent(v.Lods[0]) || v.Materials==null || v.Materials.Any(m=>!m || !EditorUtility.IsPersistent(m)))) continue;
                        if(Matches(spawn.Preview,visuals)) continue;
                        if(!factory) { factory=new GameObject("Part preview factory"); SceneManager.MoveGameObjectToScene(factory,scene); factory.AddComponent<AssemblyWorld>(); }
                        BuildingSceneAuthoring.CreatePartPreview(spawn,entry.Settings,factory.GetComponent<AssemblyWorld>(),spawn.Kind==PartKind.DownhillCore);
                        EditorUtility.SetDirty(spawn);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(spawn);
                        changed++;
                    }
                } finally { if(factory) Object.DestroyImmediate(factory); }
            }
            if(changed>0) { EditorSceneManager.MarkSceneDirty(scene); SceneView.RepaintAll(); }
            return changed;
        }

        static bool Matches(GameObject preview,CarPartArtLibrary.Visual[] visuals)
        {
            if(!preview || !preview.activeSelf || preview.tag!="EditorOnly" || !Identity(preview.transform)) return false;
            if(preview.GetComponentsInChildren<MeshFilter>(true).Length!=visuals.Length ||
                preview.GetComponentsInChildren<MeshRenderer>(true).Length!=visuals.Length) return false;
            foreach(var visual in visuals) {
                string path=visual.Body+"/"+(string.IsNullOrEmpty(visual.Parent)?"":visual.Parent+"/")+CarPartArtLibrary.VisualName;
                var child=preview.transform.Find(path);
                if(!child || !child.gameObject.activeSelf) return false;
                for(var t=child;t!=preview.transform;t=t.parent) if(!Identity(t)) return false;
                var filter=child.GetComponent<MeshFilter>(); var renderer=child.GetComponent<MeshRenderer>();
                if(!filter || filter.sharedMesh!=visual.Lods[0] || !renderer || !renderer.enabled || !renderer.sharedMaterials.SequenceEqual(visual.Materials)) return false;
            }
            return true;
        }

        static bool Identity(Transform t) => t.localPosition==Vector3.zero && t.localRotation==Quaternion.identity && t.localScale==Vector3.one;
    }
}
