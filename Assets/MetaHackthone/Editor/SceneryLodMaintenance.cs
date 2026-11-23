using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    // Rebuild from saved authoring inputs, never during a frame or an asset-import
    // callback. In particular, replacing a baked meadow mesh must invalidate LODs.
    [InitializeOnLoad]
    public static class SceneryLodMaintenance
    {
        static SceneryLodMaintenance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if(state != PlayModeStateChange.ExitingEditMode) return;
                try { SceneryAAAuthoring.EnsureCurrent(); }
                catch(Exception error)
                {
                    EditorApplication.isPlaying = false;
                    Debug.LogException(new InvalidOperationException("Scenery LOD rebuild failed before Play mode.", error));
                }
            };
        }
    }

    public sealed class SceneryLodBuild : IPreprocessBuildWithReport
    {
        // Run after the campaign/track authoring preprocessors have saved their meshes.
        public int callbackOrder => 10000;
        public void OnPreprocessBuild(BuildReport report)
        {
            try { SceneryAAAuthoring.EnsureCurrent(); }
            catch(Exception error) { throw new BuildFailedException("Scenery LOD rebuild failed: " + error); }
        }
    }
}
