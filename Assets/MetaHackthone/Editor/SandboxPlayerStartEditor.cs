using UnityEditor;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    // Design Mode editing for the player start: drag it across the floor, turn the ring to set the look direction.
    [CustomEditor(typeof(SandboxPlayerStart))]
    public sealed class SandboxPlayerStartEditor : UnityEditor.Editor
    {
        static readonly Color Gold=new(1,.85f,.2f);
        SandboxLevelLayout Layout => ((SandboxPlayerStart)target).GetComponentInParent<SandboxLevelLayout>(true);

        public override void OnInspectorGUI()
        {
            var start=(SandboxPlayerStart)target; var layout=Layout;
            EditorGUILayout.HelpBox("The headset camera position and view direction when this level starts (the camera preview is editor-only). Drag the gold disc in the Scene view to move it horizontally, the gold arrow to raise or lower it; turn the ring to aim the view. It follows the Building Area. Save the scene, then Save Level to Game.",MessageType.Info);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) {
                if(layout && layout.BuildingRoot) {
                    EditorGUI.BeginChangeCheck();
                    float height=EditorGUILayout.FloatField(new GUIContent("Camera height (m)","Headset camera height relative to the Building Area's origin. In the game the player's eyes are exactly here, regardless of the real floor."),layout.BuildingRoot?start.transform.position.y-layout.BuildingRoot.position.y:0);
                    if(EditorGUI.EndChangeCheck()) {
                        var t=start.transform; Undo.RecordObject(t,"Set player start height");
                        t.position=new Vector3(t.position.x,layout.BuildingRoot.position.y+height,t.position.z); EditorUtility.SetDirty(t);
                    }
                    DrawPlacementCheck(SandboxPlayerStartAuthoring.Check(layout));
                }
                if(layout && layout.BuildingRoot) {
                    if(GUILayout.Button("Face the Building Area")) FaceBuildingArea(start,layout);
                    if(GUILayout.Button("Reset in front of the Building Area")) ResetToDefault(start,layout);
                    if(GUILayout.Button("Select Building Area")) DesignModeEditor.Frame(layout.BuildingRoot);
                    if(!start.transform.IsChildOf(layout.BuildingRoot))
                        EditorGUILayout.HelpBox("Move Player Start under the Building Area so it follows the area.",MessageType.Warning);
                } else EditorGUILayout.HelpBox("Player Start must sit inside a sandbox level's Building Area.",MessageType.Error);
                if(layout && layout.PlayerStart!=start.transform && layout.BuildingRoot && GUILayout.Button("Use this as the level's player start")) {
                    Undo.RecordObject(layout,"Assign player start"); layout.PlayerStart=start.transform; EditorUtility.SetDirty(layout);
                }
            }
        }
        void OnSceneGUI()
        {
            var start=(SandboxPlayerStart)target; var t=start.transform;
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            float size=HandleUtility.GetHandleSize(t.position);
            Handles.color=Gold;
            // Move on the floor plane only.
            EditorGUI.BeginChangeCheck();
            var moved=Handles.Slider2D(t.position,Vector3.up,Vector3.right,Vector3.forward,size*.35f,Handles.CircleHandleCap,Vector2.zero);
            if(EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(t,"Move player start");
                t.position=new Vector3(moved.x,t.position.y,moved.z); start.KeepLevel(); EditorUtility.SetDirty(t);
            }
            // Raise or lower along world Y.
            EditorGUI.BeginChangeCheck();
            var lifted=Handles.Slider(t.position,Vector3.up,size*1.1f,Handles.ArrowHandleCap,0);
            if(EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(t,"Raise player start");
                t.position=new Vector3(t.position.x,lifted.y,t.position.z); EditorUtility.SetDirty(t);
            }
            // Turn around Y to set the look direction.
            EditorGUI.BeginChangeCheck();
            var turned=Handles.Disc(t.rotation,t.position,Vector3.up,size*.9f,false,15);
            if(EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(t,"Turn player start");
                t.rotation=turned; start.KeepLevel(); EditorUtility.SetDirty(t);
            }
            Handles.ArrowHandleCap(0,t.position,Quaternion.LookRotation(Vector3.ProjectOnPlane(t.forward,Vector3.up).normalized+Vector3.forward*1e-5f),size*1.2f,EventType.Repaint);
            Handles.Label(t.position+Vector3.up*size*.6f,"PLAYER START");
            // Inspector edits of the Transform can tilt it; keep it upright like the runtime reads it.
            if(Event.current.type==EventType.Repaint && Quaternion.Angle(t.localRotation,Quaternion.Euler(0,t.localEulerAngles.y,0))>.01f) {
                Undo.RecordObject(t,"Level player start"); start.KeepLevel(); EditorUtility.SetDirty(t);
            }
        }
        static void DrawPlacementCheck(SandboxPlayerStartAuthoring.Placement c)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Arm reach ({SandboxPlayerStartAuthoring.ArmReach*100:F0} cm sphere) — "+(c.Good?"placement OK":"adjust placement"),EditorStyles.boldLabel);
            if(!c.HasBoundary) { EditorGUILayout.HelpBox("This level has no Sandbox building boundary.",MessageType.Warning); return; }
            static string Mark(bool ok)=>ok?"✔":"✘";
            EditorGUILayout.LabelField(Mark(c.CameraOutsideBoundary)+"  Camera is outside the building boundary");
            if(c.HasPlatform) {
                EditorGUILayout.LabelField(Mark(c.PlatformInsideBoundary)+"  Building platform is inside the boundary");
                EditorGUILayout.LabelField(Mark(c.PlatformInReach)+$"  Whole platform within reach (farthest corner {c.FarthestPlatform*100:F0} cm)");
            }
            EditorGUILayout.LabelField(Mark(c.CameraOutsideBoundary && c.NearestBoundary<SandboxPlayerStartAuthoring.ArmReach)+$"  Arm reaches into the boundary (nearest face {c.NearestBoundary*100:F0} cm)");
            EditorGUILayout.LabelField((c.BoundaryInReach?"✔":"•")+$"  Boundary within reach: farthest corner {c.FarthestBoundary*100:F0} cm"+(c.BoundaryInReach?"":" (info)"));
            EditorGUILayout.HelpBox("The sphere around the camera shows how far the player's arm reaches. It turns green when the camera is outside the building boundary, the building platform is inside it and fully within reach, and the arm can reach into the boundary. Editor only — never shown in game.",MessageType.None);
        }
        static void FaceBuildingArea(SandboxPlayerStart start,SandboxLevelLayout layout)
        {
            var t=start.transform; var toward=Vector3.ProjectOnPlane(layout.BuildingRoot.position-t.position,Vector3.up);
            if(toward.sqrMagnitude<1e-6f) return;
            Undo.RecordObject(t,"Face Building Area"); t.rotation=Quaternion.LookRotation(toward); start.KeepLevel(); EditorUtility.SetDirty(t);
        }
        static void ResetToDefault(SandboxPlayerStart start,SandboxLevelLayout layout)
        {
            var t=start.transform; Undo.RecordObject(t,"Reset player start");
            if(t.parent!=layout.BuildingRoot) Undo.SetTransformParent(t,layout.BuildingRoot,"Reset player start");
            t.localPosition=SandboxPlayerStartAuthoring.DefaultLocalPosition(layout); t.localRotation=Quaternion.identity;
            EditorUtility.SetDirty(t);
        }
    }
}
