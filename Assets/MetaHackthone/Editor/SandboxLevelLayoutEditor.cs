using UnityEditor;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    [CustomEditor(typeof(SandboxLevelLayout))]
    public sealed class SandboxLevelLayoutEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var layout=(SandboxLevelLayout)target;
            EditorGUILayout.HelpBox("Edit outside Play mode. Move either group, turn it around Y, or set its uniform scale. Save the scene; use Save Level to Game to publish the playable copy.",MessageType.Info);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) {
                Group("Building Area",layout.BuildingRoot);
                if(layout.BuildingRoot && GUILayout.Button("Select Player Start")) { SandboxPlayerStartAuthoring.Ensure(layout); DesignModeEditor.Frame(layout.PlayerStart); }
                if(layout.Course && layout.Course.BuildArea && GUILayout.Button("Select Building Boundary"))
                    DesignModeEditor.Frame(layout.Course.BuildArea.transform);
                Group("Sandbox Level",layout.SandboxRoot);
                var factory=layout.GetComponentInParent<SandboxFactoryLevel>(true);
                if(factory && factory.TrackRoot && GUILayout.Button("Select Track")) DesignModeEditor.Frame(factory.TrackRoot);
                var environment=layout.Course?layout.Course.SandboxFrame.Find("Environment"):null;
                if(environment && GUILayout.Button("Select Environment")) DesignModeEditor.Frame(environment);
                EditorGUILayout.HelpBox("Move Track to align the road, puzzles, Start and Goal with the scenery. Environment stays in place. Save the scene, then Save Level to Game.",MessageType.Info);
            }
            if(!layout.Validate(out var error)) EditorGUILayout.HelpBox(error,MessageType.Error);
            DrawDefaultInspector();
        }
        static void Group(string name,Transform root)
        {
            if(!root) return;
            if(GUILayout.Button("Select "+name)) DesignModeEditor.Frame(root);
            EditorGUI.BeginChangeCheck();
            float scale=EditorGUILayout.FloatField(name+" — Uniform Scale",root.localScale.x);
            if(EditorGUI.EndChangeCheck() && float.IsFinite(scale) && scale>0) {
                Undo.RecordObject(root,"Scale "+name); root.localScale=Vector3.one*scale;
                EditorUtility.SetDirty(root);
            }
        }
    }
}
