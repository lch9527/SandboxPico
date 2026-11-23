using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    public static class CarSceneryIntegration
    {
        // Run after accepting the program branch's scene/prefab layout during a merge.
        // Individual authoring passes verify that gameplay and source placement stay unchanged.
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play first.");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            CarSceneryAuthoring.Apply();
            LightweightBoundaryAuthoring.Apply();
            foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/CreekDiorama"})) {
                var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if(!material || material.shader.name!="Creek Diorama/Living Water")continue;
                material.SetFloat("_UseDepth",0);material.SetFloat("_UseOpaqueTexture",0);EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            CarSceneryClearance.AuditExisting();
            Debug.Log("CAR_SCENERY_INTEGRATED: latest program layouts preserved; art and lightweight frames reapplied; river materials require neither camera depth nor scene color.");
        }
    }
}
