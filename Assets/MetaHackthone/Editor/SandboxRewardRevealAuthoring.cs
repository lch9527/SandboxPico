using System.Linq;
// Gizmos and menu items here are editor-only; nothing in this file runs in builds.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    // Reward reveal marker: a child of each level's Building Area marking where the centre of a newly
    // unlocked part rises to. Without one the part rises above the assembly spot.
    public static class SandboxRewardRevealAuthoring
    {
        [MenuItem("MetaHackthone/Design Mode/Select Reward Reveal Point")]
        public static void Select()
        {
            var layout=Selection.activeTransform?Selection.activeTransform.GetComponentInParent<SandboxLevelLayout>(true):null;
            if(!layout) layout=Object.FindObjectsByType<SandboxLevelLayout>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).FirstOrDefault();
            if(!layout) { Debug.LogWarning("No sandbox level layout in the open scene."); return; }
            if(Ensure(layout)) {
                EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
                Debug.Log("Reward reveal point added. Move it, save the scene, then Save Level to Game to bake the prefab.");
            }
            Selection.activeTransform=layout.RewardRevealPoint; DesignModeEditor.Frame(layout.RewardRevealPoint);
        }
        // Returns true when something was created. An existing point is left where the designer put it.
        public static bool Ensure(SandboxLevelLayout layout)
        {
            if(!layout || !layout.BuildingRoot || layout.RewardRevealPoint) return false;
            var existing=layout.BuildingRoot.Find(SandboxLevelLayout.RewardRevealPointName);
            if(!existing) {
                // Starts at the runtime default so adding the marker alone changes nothing.
                var pose=layout.RewardRevealPose();
                existing=new GameObject(SandboxLevelLayout.RewardRevealPointName).transform;
                Undo.RegisterCreatedObjectUndo(existing.gameObject,"Add reward reveal point");
                existing.SetParent(layout.BuildingRoot,false);
                existing.SetPositionAndRotation(pose.position,pose.rotation);
            }
            Undo.RecordObject(layout,"Assign reward reveal point"); layout.RewardRevealPoint=existing;
            EditorUtility.SetDirty(layout);
            return true;
        }
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        static void DrawRevealPoint(SandboxLevelLayout layout,GizmoType type)
        {
            if(!layout || !layout.BuildingRoot) return;
            var pose=layout.RewardRevealPose(); var color=Gizmos.color;
            // Gold diamond where the part settles, with the path it rises along.
            Gizmos.color=new Color(1,.78f,.08f);
            Gizmos.DrawWireSphere(pose.position,.06f);
            Gizmos.DrawLine(pose.position,pose.position+Vector3.down*.2f);
            Gizmos.color=new Color(1,.78f,.08f,.25f); Gizmos.DrawSphere(pose.position,.06f);
            Gizmos.color=color;
        }
    }
}
