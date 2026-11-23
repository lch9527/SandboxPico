using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class SandboxPuzzleEditing
    {
        public static SandboxPuzzle[] Puzzles(SandboxTrackGenerator track) => track.gameObject.scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<SandboxPuzzle>(true)).Where(p => p.Track == track && p.isActiveAndEnabled).ToArray();
        public static SandboxTrackBaker.Sample At(IReadOnlyList<SandboxTrackBaker.Sample> path, float distance)
        {
            int i = 1; while (i < path.Count - 1 && path[i].Distance < distance) i++;
            var a = path[i - 1]; var b = path[i]; float f = Mathf.InverseLerp(a.Distance, b.Distance, distance);
            return new SandboxTrackBaker.Sample { Position = Vector3.Lerp(a.Position, b.Position, f), Forward = Vector3.Slerp(a.Forward, b.Forward, f).normalized,
                Right = Vector3.Slerp(a.Right, b.Right, f).normalized, Up = Vector3.Slerp(a.Up, b.Up, f).normalized, Width = Mathf.Lerp(a.Width, b.Width, f), Distance = distance };
        }
        public static float Nearest(SandboxTrackGenerator track, Vector3 world)
        {
            var path = SandboxTrackBaker.SamplePath(track); var p = track.transform.InverseTransformPoint(world);
            float best = float.PositiveInfinity, distance = 0;
            for (int i = 1; i < path.Count; i++) {
                var a = path[i - 1]; var b = path[i]; var d = b.Position - a.Position;
                float f = Mathf.Clamp01(Vector3.Dot(p - a.Position, d) / d.sqrMagnitude);
                float error = (p - Vector3.Lerp(a.Position, b.Position, f)).sqrMagnitude;
                if (error < best) { best = error; distance = Mathf.Lerp(a.Distance, b.Distance, f); }
            }
            return distance;
        }
        public static string Validate(SandboxPuzzle p, IReadOnlyList<SandboxTrackBaker.Sample> path)
        {
            if (!p.Track) return "Assign a track.";
            if (!float.IsFinite(p.Distance + p.Offset.sqrMagnitude + p.Rotation.sqrMagnitude)) return "Placement must be finite.";
            if (p.Distance - p.Span * .5f < 0 || p.Distance + p.Span * .5f > path[path.Count - 1].Distance) return "Puzzle extends past the track. Move it or extend the track.";
            if(p is SandboxDropBridge drop && (drop.TargetTrackDistance<0 || drop.TargetTrackDistance>path[path.Count-1].Distance)) return "Place the target between the track start and end.";
            if(p is SandboxCrabBridge crab) {
                float width=path.Where(s=>s.Distance>=p.Distance-p.Span*.5f && s.Distance<=p.Distance+p.Span*.5f).Select(s=>s.Width)
                    .Append(At(path,p.Distance-p.Span*.5f).Width).Append(At(path,p.Distance+p.Span*.5f).Width).Min();
                if(crab.HoleWidth+2*Mathf.Abs(p.Offset.x)>=width-.1f) return "Crab opening is too wide for this road. Reduce Hole Width or widen the track.";
            }
            if (p.ReplacesRoad && (Mathf.Abs(p.Offset.y) > .0001f || Mathf.Abs(p.Offset.z) > .0001f || p.Rotation.sqrMagnitude > .0001f))
                return "Road cutouts follow the spline: use Distance and lateral Offset; use bridge Pivot Height for vertical adjustment.";
            return p.ValidatePuzzle();
        }
        public static void UpgradeTarget(SandboxDropBridge drop)
        {
            if(!drop || drop.TargetDistance>=0 || drop.TargetTrackDistance<0) return;
            Undo.RecordObject(drop,"Use independent target position");
            drop.TargetDistance=drop.TargetTrackDistance;
            float unit=BuildingGeometry.Unit/GravityGarage25DCourse.UnitScale;
            float axle=(drop.HeightReference==SandboxDropBridge.ReferenceWheels.Large?.44f:.30f)/.38f;
            drop.TargetLayer=Mathf.Max(1,Mathf.RoundToInt(drop.TargetHeight/unit-axle)+1);
            EditorUtility.SetDirty(drop);
        }
        public static void UpdateTrack(SandboxTrackGenerator track)
        {
            if(track.AuthoredObjects) return;
            var puzzles = Puzzles(track);
            var path = SandboxTrackBaker.SamplePath(track);
            // Validate every placement before changing any generated puzzle or road.
            foreach(var p in puzzles) {
                if(p is SandboxDropBridge legacy) UpgradeTarget(legacy);
                if(p is SandboxDropBridge d && d.UseBuildingLayers) {
                    d.TargetHeight=d.LayerHeight;
                }
                if(p is SandboxRoller roller) {
                    var width=At(path,p.Distance).Width;
                    roller.Width=Mathf.Min(roller.Width,width);
                    roller.Height=0; p.Rotation=Vector3.zero;
                    p.Offset=new Vector3(Mathf.Clamp(p.Offset.x,-(width-roller.Width)*.5f,(width-roller.Width)*.5f),0,0);
                }
                p.ValidationError=Validate(p,path);
            }
            foreach(var p in puzzles) if(!string.IsNullOrEmpty(p.ValidationError))
                throw new InvalidOperationException(p.name+": "+p.ValidationError);
            var cuts=puzzles.Where(p=>p.ReplacesRoad).OrderBy(p=>p.Distance).ToArray();
            for(int i=1;i<cuts.Length;i++) if(cuts[i-1].Distance+cuts[i-1].Span*.5f>cuts[i].Distance-cuts[i].Span*.5f+.00001f) {
                var error=$"Road replacement puzzles '{cuts[i-1].name}' and '{cuts[i].name}' overlap. Separate their intervals.";
                cuts[i-1].ValidationError=cuts[i].ValidationError=error;
                throw new InvalidOperationException(error);
            }
            foreach(var p in track.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SandboxPuzzle>(true)).Where(p=>p.Track==track)) {
                // Older scenes can contain multiple baked copies, but only Generated
                // is controlled by this puzzle. Remove obsolete physics/visuals even
                // when the current geometry is unchanged or the puzzle is disabled.
                foreach(var child in p.transform.Cast<Transform>().ToArray())
                    if(child.gameObject!=p.Generated && child.name=="Generated puzzle (edit parent)" && !child.GetComponent<SandboxPuzzle>()) {
                        Object.DestroyImmediate(child.gameObject);
                        EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
                    }
                if(p.Generated) p.Generated.SetActive(p.isActiveAndEnabled);
            }
            foreach (var p in puzzles) {
                p.ValidationError = Validate(p, path);
                if (!string.IsNullOrEmpty(p.ValidationError)) continue;
                var s = At(path, p.Distance); var frame = Quaternion.LookRotation(s.Forward, s.Up);
                var position = track.transform.TransformPoint(s.Position + frame * p.Offset);
                var rotation = track.transform.rotation * frame * Quaternion.Euler(p.Rotation);
                if ((p.transform.position - position).sqrMagnitude > 1e-10f || Quaternion.Angle(p.transform.rotation, rotation) > .001f) {
                    p.transform.SetPositionAndRotation(position, rotation); EditorUtility.SetDirty(p.transform);
                }
                float scale = track.transform.lossyScale.x / (p.transform.parent ? p.transform.parent.lossyScale.x : 1);
                p.transform.localScale = Vector3.one * scale;
                if(p is SandboxDropBridge drop) {
                    var target=At(path,drop.TargetTrackDistance);
                    drop.TargetLocalPosition=p.transform.InverseTransformPoint(track.transform.TransformPoint(target.Position+target.Up*drop.TargetHeight));
                }
                if (p.Generated && p.GeometrySignature == p.Signature) continue;
                if (p.Generated) Object.DestroyImmediate(p.Generated);
                var root = new GameObject("Generated puzzle (edit parent)"); root.transform.SetParent(p.transform, false); p.Generated = root;
                p.BuildGeometry(root.transform); p.GeometrySignature = p.Signature;
                EditorUtility.SetDirty(p); EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
            }
        }
        public static SandboxPuzzle Add(SandboxTrackGenerator track, Type type, float distance, GameObject prefab = null)
        {
            var go = prefab ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, track.transform.parent) : new GameObject(ObjectNames.NicifyVariableName(type.Name.Replace("Sandbox", "")));
            if (!prefab) go.transform.SetParent(track.transform.parent, false);
            var p = go.GetComponent<SandboxPuzzle>(); if (!p) p = (SandboxPuzzle)go.AddComponent(type);
            p.Track = track; p.Distance = distance;
            var red=AssetDatabase.LoadAssetAtPath<Material>("Assets/CreekDiorama/Materials/Goal vermilion.mat");
            if(p is SandboxRoller && !p.Material) p.Material=red;
            if(p is SandboxDropBridge target && !target.TargetMaterial) target.TargetMaterial=red;
            if(p is SandboxDropBridge drop && !drop.ChainMaterial) drop.ChainMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/MetaHackthone/Art/HustleCar/Unity/Metal.mat");
            if(p is SandboxDropBridge bridge) bridge.TargetDistance=Mathf.Max(0,bridge.TargetTrackDistance);
            Undo.RegisterCreatedObjectUndo(go, "Add puzzle");
            if(track.LiveRebuild) SandboxTrackBaker.Rebuild(track, true);
            Selection.activeGameObject = go; return p;
        }
    }

    public sealed class SandboxPuzzlePalette : EditorWindow
    {
        SandboxTrackGenerator track; Type pending; GameObject custom; float? crabStart;
        [MenuItem("MetaHackthone/Level Factory/Puzzle Palette")]
        public static void Open() => GetWindow<SandboxPuzzlePalette>("Puzzles");
        void OnEnable() { SceneView.duringSceneGui += Place; }
        void OnDisable() { SceneView.duringSceneGui -= Place; }
        void OnGUI()
        {
            if (!track && Selection.activeGameObject) track = Selection.activeGameObject.GetComponentInParent<SandboxTrackGenerator>();
            track = (SandboxTrackGenerator)EditorGUILayout.ObjectField("Track", track, typeof(SandboxTrackGenerator), true);
            using (new EditorGUI.DisabledScope(Application.isPlaying || !track)) {
                foreach (var type in TypeCache.GetTypesDerivedFrom<SandboxPuzzle>().Where(t => !t.IsAbstract).OrderBy(t => t.Name))
                    if (GUILayout.Button(ObjectNames.NicifyVariableName(type.Name.Replace("Sandbox", "")))) { pending = type; custom = null; crabStart=null; SceneView.RepaintAll(); }
                custom = (GameObject)EditorGUILayout.ObjectField("Custom puzzle prefab", custom, typeof(GameObject), false);
                using (new EditorGUI.DisabledScope(!custom || !custom.GetComponent<SandboxPuzzle>()))
                    if (GUILayout.Button("Place custom prefab")) pending = custom.GetComponent<SandboxPuzzle>().GetType();
            }
            EditorGUILayout.HelpBox(pending==typeof(SandboxCrabBridge) ? (crabStart.HasValue?"Click the END of the crab section.":"Click the START of the crab section, then its END.") : pending != null ? "Click the track in Scene View to place. Escape cancels." : "Choose a puzzle, then click the track. Rebuild after placement or edits.", MessageType.Info);
        }
        void Place(SceneView view)
        {
            if (pending == null || !track || Application.isPlaying) return;
            var e = Event.current; if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { pending = null; e.Use(); Repaint(); return; }
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var hit = Physics.RaycastAll(ray, 1000).Where(h => h.collider.GetComponentInParent<SandboxTrackGenerator>() == track).OrderBy(h => h.distance).FirstOrDefault();
            if (!hit.collider) return;
            Handles.color = Color.cyan; Handles.DrawWireDisc(hit.point, track.transform.up, HandleUtility.GetHandleSize(hit.point) * .1f);
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt) {
                float distance=SandboxPuzzleEditing.Nearest(track,hit.point);
                if(pending==typeof(SandboxCrabBridge) && !crabStart.HasValue) { crabStart=distance; e.Use(); Repaint(); return; }
                var puzzle=SandboxPuzzleEditing.Add(track, pending, crabStart.HasValue?(distance+crabStart.Value)*.5f:distance, custom);
                if(puzzle is SandboxCrabBridge crab && crabStart.HasValue) { crab.Length=Mathf.Max(.1f,Mathf.Abs(distance-crabStart.Value)); EditorUtility.SetDirty(crab); }
                crabStart=null;
                pending = null; e.Use(); Repaint();
            }
            view.Repaint();
        }
    }

    [CustomEditor(typeof(SandboxPuzzle), true)]
    public sealed class SandboxPuzzleInspector : UnityEditor.Editor
    {
        void OnEnable() { Tools.hidden=true; if(!Application.isPlaying && target is SandboxDropBridge drop) SandboxPuzzleEditing.UpgradeTarget(drop); }
        void OnDisable() { Tools.hidden=false; }
        public override void OnInspectorGUI()
        {
            var p = (SandboxPuzzle)target;
            using (new EditorGUI.DisabledScope(Application.isPlaying)) {
                serializedObject.Update();
                var excluded=new List<string>{"m_Script"};
                if(p is SandboxDropBridge) excluded.AddRange(new[]{"TargetHeight","TargetApproach","UseBuildingLayers"});
                if(p is SandboxRoller) excluded.AddRange(new[]{"Height","Offset","Rotation"});
                if(p is SandboxCrabBridge) excluded.AddRange(new[]{"Distance","Length","LeftLaneWidth","RightLaneWidth"});
                DrawPropertiesExcluding(serializedObject,excluded.ToArray()); serializedObject.ApplyModifiedProperties();
                if(p is SandboxCrabBridge crab) {
                    EditorGUI.BeginChangeCheck();
                    float start=EditorGUILayout.FloatField("Section start",crab.Distance-crab.Length*.5f);
                    float end=EditorGUILayout.FloatField("Section end",crab.Distance+crab.Length*.5f);
                    if(EditorGUI.EndChangeCheck()) { Undo.RecordObject(crab,"Change crab section"); crab.Distance=(start+end)*.5f; crab.Length=end-start; EditorUtility.SetDirty(crab); }
                }
                if(p.Track && GUILayout.Button("Rebuild Road With Puzzles")) SandboxTrackBaker.RebuildFromInspector(p.Track);
                EditorGUILayout.HelpBox("Enable/disable this puzzle with the component checkbox. Rebuild to restore its old road section and apply its new placement. Distances are measured along the road from its start.",MessageType.Info);
            }
            if (!string.IsNullOrEmpty(p.ValidationError)) EditorGUILayout.HelpBox(p.ValidationError, MessageType.Error);
            if (GUILayout.Button("Open Puzzle Palette")) SandboxPuzzlePalette.Open();
        }
        void OnSceneGUI()
        {
            var p = (SandboxPuzzle)target; if (Application.isPlaying || !p.Track) return;
            if(Tools.current==Tool.Rotate && !p.ReplacesRoad) {
                EditorGUI.BeginChangeCheck(); var rotation=Handles.RotationHandle(p.transform.rotation,p.transform.position);
                if(EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(p,"Rotate puzzle");
                    var sample=SandboxPuzzleEditing.At(SandboxTrackBaker.SamplePath(p.Track),p.Distance);
                    p.Rotation=(Quaternion.Inverse(p.Track.transform.rotation*Quaternion.LookRotation(sample.Forward,sample.Up))*rotation).eulerAngles;
                    EditorUtility.SetDirty(p); if(p.Track.LiveRebuild) SandboxPuzzleEditing.UpdateTrack(p.Track);
                }
            }
            var placement=SandboxPuzzleEditing.At(SandboxTrackBaker.SamplePath(p.Track),p.Distance);
            var preview=p.Track.transform.TransformPoint(placement.Position+Quaternion.LookRotation(placement.Forward,placement.Up)*p.Offset);
            Handles.Label(preview,"Pending puzzle position — Rebuild to apply");
            EditorGUI.BeginChangeCheck(); var moved = Tools.current==Tool.Rotate ? preview : Handles.PositionHandle(preview, p.Track.transform.rotation);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(p, "Move puzzle"); p.Distance = SandboxPuzzleEditing.Nearest(p.Track, moved);
                var s = SandboxPuzzleEditing.At(SandboxTrackBaker.SamplePath(p.Track), p.Distance);
                var offset = Quaternion.Inverse(Quaternion.LookRotation(s.Forward, s.Up)) * (p.Track.transform.InverseTransformPoint(moved) - s.Position);
                p.Offset = p.ReplacesRoad ? new Vector3(offset.x, 0, 0) : offset;
                EditorUtility.SetDirty(p); if(p.Track.LiveRebuild) SandboxPuzzleEditing.UpdateTrack(p.Track);
            }
            var so = serializedObject; so.Update();
            void Dimension(string field, Vector3 axis, float factor = 1, Vector3? start = null) {
                var prop = so.FindProperty(field); if (prop == null) return;
                float scale = p.Track.transform.lossyScale.x; var origin = start ?? p.transform.position;
                var end = origin + axis * prop.floatValue * scale * factor;
                EditorGUI.BeginChangeCheck(); var result = Handles.Slider(end, axis, HandleUtility.GetHandleSize(end) * .07f, Handles.CubeHandleCap, 0);
                Handles.Label(end, field + " " + prop.floatValue.ToString("0.00"));
                if (EditorGUI.EndChangeCheck()) prop.floatValue = Mathf.Max(.02f, Vector3.Dot(result - origin, axis) / (scale * factor));
            }
            Handles.color = Color.cyan;
            Dimension("Length", p.transform.forward, .5f); Dimension("Width", p.transform.right, .5f);
            Dimension("HoleWidth", p.transform.right, .5f); Dimension("Diameter", p.transform.up);
            if(p is SandboxDropBridge drop) {
                if(drop.Pivot) {
                    Handles.color=Color.yellow; Handles.Label(drop.Pivot.position,"Hinge pivot");
                    EditorGUI.BeginChangeCheck(); var pivot=Handles.PositionHandle(drop.Pivot.position,drop.transform.rotation);
                    if(EditorGUI.EndChangeCheck()) so.FindProperty("PivotOffset").vector3Value+=drop.transform.InverseTransformVector(pivot-drop.Pivot.position);
                    Handles.color=Color.cyan;
                }
                var path=SandboxTrackBaker.SamplePath(p.Track);
                var sample=SandboxPuzzleEditing.At(path,Mathf.Max(0,drop.TargetTrackDistance));
                var target=p.Track.transform.TransformPoint(sample.Position+sample.Up*(drop.UseBuildingLayers?drop.LayerHeight:drop.TargetHeight));
                Handles.color=Color.yellow; Handles.Label(target,"Target — layer "+drop.TargetLayer);
                Handles.DrawDottedLine(target,p.transform.position,4);
                EditorGUI.BeginChangeCheck(); var targetMoved=Handles.PositionHandle(target,p.Track.transform.rotation);
                if(EditorGUI.EndChangeCheck()) so.FindProperty("TargetDistance").floatValue=SandboxPuzzleEditing.Nearest(p.Track,targetMoved);
                Dimension("TargetSize",p.transform.right,.5f,p.transform.TransformPoint(drop.TargetLocalPosition));
            }
            if(p is SandboxCrabBridge crab) {
                var sectionPath=SandboxTrackBaker.SamplePath(p.Track);
                foreach(int side in new[]{-1,1}) {
                    float distance=crab.Distance+side*crab.Length*.5f;
                    var endpoint=p.Track.transform.TransformPoint(SandboxPuzzleEditing.At(sectionPath,Mathf.Max(0,distance)).Position);
                    Handles.Label(endpoint,side<0?"Crab START":"Crab END");
                    EditorGUI.BeginChangeCheck(); var endpointMoved=Handles.PositionHandle(endpoint,p.Track.transform.rotation);
                    if(EditorGUI.EndChangeCheck()) {
                        float other=crab.Distance-side*crab.Length*.5f;
                        float changed=SandboxPuzzleEditing.Nearest(p.Track,endpointMoved);
                        so.FindProperty("Distance").floatValue=(other+changed)*.5f;
                        so.FindProperty("Length").floatValue=Mathf.Abs(other-changed);
                    }
                }
            }
            so.ApplyModifiedProperties();
        }
    }
}
