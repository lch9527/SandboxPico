using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace PhysicalAssembly.Editor
{
    [InitializeOnLoad]
    public static class SandboxTrackEditing
    {
        public static bool Suspended;
        static bool busy; static double next;
        static readonly Dictionary<int,string> attempted=new();
        static SandboxTrackEditing()
        {
            EditorApplication.update+=Update;
            Spline.Changed+=Changed;
            Undo.undoRedoPerformed+=()=>{attempted.Clear();next=0;};
            EditorSceneManager.sceneSaving+=(scene,path)=> {
                if(Application.isPlaying || Suspended || busy) return;
                if(!Flush(scene,true,path)) Debug.LogError("Factory track is invalid. Saved spline edits are retained; fix the reported error before testing or building.");
            };
            EditorApplication.playModeStateChanged+=state=> {
                if(state==PlayModeStateChange.ExitingEditMode && !Suspended) {
                    bool valid=true;
                    for(int i=0;i<SceneManager.sceneCount;i++) valid &= Flush(SceneManager.GetSceneAt(i),true);
                    if(!valid) { Debug.LogError("Correct factory track errors before testing."); EditorApplication.isPlaying=false; }
                }
                if(state==PlayModeStateChange.EnteredEditMode) attempted.Clear();
            };
        }
        public static SandboxTrackGenerator[] Tracks(Scene scene) => scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SandboxTrackGenerator>(true)).ToArray();
        static IEnumerable<SandboxTrackGenerator> Loaded() => UnityEngine.Object.FindObjectsByType<SandboxTrackGenerator>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Where(t=>t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(t) && !EditorSceneManager.IsPreviewScene(t.gameObject.scene));
        static void Changed(Spline spline,int index,SplineModification modification)
        {
            if(Application.isPlaying || Suspended || modification!=SplineModification.KnotInserted) return;
            foreach(var track in Loaded().Where(t=>t.Curve.Spline==spline)) {
                if(index<0 || index>=spline.Count) continue;
                float width=track.DefaultWidth;
                if(spline.Count>1) {
                    if(index==0) width=track.KnotWidth(1);
                    else if(index==spline.Count-1) width=track.KnotWidth(index-1);
                    else {
                        float a=spline.GetCurveLength(index-1), b=spline.GetCurveLength(index);
                        width=Mathf.Lerp(track.KnotWidth(index-1),track.KnotWidth(index+1),Mathf.SmoothStep(0,1,a/Mathf.Max(.0001f,a+b)));
                    }
                }
                // Embedded data is part of the SplineContainer's own Undo snapshot.
                track.SetKnotWidth(index,width); EditorUtility.SetDirty(track.Curve);
                PrefabUtility.RecordPrefabInstancePropertyModifications(track.Curve);
            }
        }
        static void Update()
        {
            if(Suspended || busy || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup<next) return;
            next=EditorApplication.timeSinceStartup+.15;
            foreach(var track in Loaded()) {
                if(track.AuthoredObjects || !track.LiveRebuild) continue;
                try { SandboxPuzzleEditing.UpdateTrack(track); } catch(Exception e) { track.ValidationError=e.Message; continue; }
                string signature=SandboxTrackBaker.Signature(track);
                if(track.Generated && signature==track.BakedSignature && string.IsNullOrEmpty(track.ValidationError)) continue;
                if(attempted.TryGetValue(track.GetInstanceID(),out var prior) && prior==signature) continue;
                attempted[track.GetInstanceID()]=signature; SandboxTrackBaker.Rebuild(track);
            }
            foreach(var factory in UnityEngine.Object.FindObjectsByType<SandboxFactoryLevel>(FindObjectsSortMode.None))
                if(factory.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(factory)) UpdateStart(factory);
        }
        public static bool UpdateStart(SandboxFactoryLevel factory)
        {
            if(factory.AuthoredObjects) return true;
            if(!factory.BindStart || !factory.Start) return true;
            var binding=factory.StartBinding; if(!binding.Track) return false;
            try {
                var path=SandboxTrackBaker.SamplePath(binding.Track);
                if(!float.IsFinite(binding.Distance) || binding.Distance<0 || binding.Distance>path[path.Count-1].Distance) return false;
                var sample=SandboxPuzzleEditing.At(path,binding.Distance); var q=Quaternion.LookRotation(sample.Forward,sample.Up);
                var pos=binding.Track.transform.TransformPoint(sample.Position+q*binding.Offset);
                var rot=binding.Track.transform.rotation*q*Quaternion.Euler(binding.Rotation);
                if((factory.Start.position-pos).sqrMagnitude>1e-10f || Quaternion.Angle(factory.Start.rotation,rot)>.001f) {
                    factory.Start.SetPositionAndRotation(pos,rot); EditorUtility.SetDirty(factory.Start);
                }
                return true;
            } catch { return false; }
        }
        public static bool Flush(Scene scene,bool persist,string savePath=null)
        {
            if(busy || Suspended) return true;
            busy=true; bool valid=true;
            try {
                foreach(var track in Tracks(scene)) {
                    bool pending=!track.Generated || track.BakedSignature!=SandboxTrackBaker.Signature(track);
                    if(!(pending ? SandboxTrackBaker.RebuildFromInspector(track) : SandboxTrackBaker.Rebuild(track))) { valid=false; continue; }
                    if(persist) {
                        var path=savePath??scene.path;
                        if(string.IsNullOrEmpty(path)) { track.ValidationError="Save this scene before testing."; valid=false; continue; }
                        SandboxTrackBaker.Persist(track,System.IO.Path.ChangeExtension(path,null)+"_Factory/Tracks",GlobalObjectId.GetGlobalObjectIdSlow(track)+"|"+path);
                    }
                }
                foreach(var factory in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SandboxFactoryLevel>(true))) {
                    if(!UpdateStart(factory)) { Debug.LogError("Start binding requires a valid track and distance.",factory); valid=false; }
                    factory.Tracks=factory.TrackRoot?factory.TrackRoot.GetComponentsInChildren<SandboxTrackGenerator>(true):Array.Empty<SandboxTrackGenerator>();
                    EditorUtility.SetDirty(factory);
                    if(!factory.Validate(out var error) && factory.Tracks.Length>0) { Debug.LogError(error,factory); valid=false; }
                }
            } finally { busy=false; }
            return valid;
        }
        public static void Edit(SandboxTrackGenerator track)
        {
            Selection.activeGameObject=track.gameObject; ActiveEditorTracker.sharedTracker.ForceRebuild();
            ToolManager.SetActiveContext<SplineToolContext>(); ToolManager.SetActiveTool<SplineMoveTool>();
            var view=SceneView.lastActiveSceneView??EditorWindow.GetWindow<SceneView>(); view.FrameSelected(); view.Focus();
        }
    }
    public sealed class SandboxTrackBuild : IProcessSceneWithReport
    {
        public int callbackOrder=>-900;
        public void OnProcessScene(Scene scene,BuildReport report)
        {
            foreach(var track in SandboxTrackEditing.Tracks(scene)) {
                if(track.AuthoredObjects) {
                    if(!track.Generated || track.Samples.Length<2 || track.GetComponentsInChildren<MeshFilter>(true).Any(f=>f.sharedMesh && !AssetDatabase.Contains(f.sharedMesh)))
                        throw new BuildFailedException("Save authored track objects before building: "+scene.path);
                    continue;
                }
                try { SandboxTrackBaker.SamplePath(track); }
                catch(Exception e) { throw new BuildFailedException(track.name+": "+e.Message); }
                // Instance IDs change in build scenes, so compare geometry with fresh samples and require persisted meshes.
                var samples=SandboxTrackBaker.SamplePath(track);
                if(!track.Generated || !string.IsNullOrEmpty(track.ValidationError) ||
                   samples.Any(s=>!track.Samples.Any(b=>Vector3.Distance(s.Position,b)<.0001f)) ||
                   track.Generated.GetComponentsInChildren<MeshFilter>().Any(f=>!f.sharedMesh || !AssetDatabase.Contains(f.sharedMesh)))
                    throw new BuildFailedException("Save and bake factory tracks before building: "+scene.path);
            }
        }
    }
    [CustomEditor(typeof(SandboxTrackGenerator))]
    public sealed class SandboxTrackGeneratorEditor : UnityEditor.Editor
    {
        int selected, selectedRailSection;
        int Selected(SandboxTrackGenerator t) {
            var element=SplineSelection.GetActiveElement(new[]{new SplineInfo(t.Curve,0)});
            if(element!=null) selected=element.KnotIndex;
            return selected=Mathf.Clamp(selected,0,Mathf.Max(0,t.Curve.Spline.Count-1));
        }
        public override void OnInspectorGUI()
        {
            var track=(SandboxTrackGenerator)target;
            if(track.AuthoredObjects) {
                EditorGUILayout.HelpBox("This level uses saved scene objects. Edit, move, disable or delete road, rail and scenery objects in the Hierarchy. Ctrl+S saves the campaign scene to the game. Spline generation is disabled; route samples remain as gameplay guidance.",MessageType.Info);
                return;
            }
            using(new EditorGUI.DisabledScope(Application.isPlaying)) {
                if(GUILayout.Button("Edit Track Spline")) SandboxTrackEditing.Edit(track);
                DrawDefaultInspector();
                DrawRails(track);
                if(track.Curve.Spline.Count>0) {
                    Selected(track);
                    selected=EditorGUILayout.IntSlider("Control point",selected,0,track.Curve.Spline.Count-1);
                    EditorGUI.BeginChangeCheck(); float width=EditorGUILayout.FloatField("Point width (full)",track.KnotWidth(selected));
                    if(EditorGUI.EndChangeCheck()) SetWidth(track,selected,width);
                }
                if(GUILayout.Button("Rebuild Road and Supports")) SandboxTrackBaker.RebuildFromInspector(track);
                if(!track.LiveRebuild) EditorGUILayout.HelpBox("Move/add spline points and configure puzzles, then Rebuild. The old road stays visible until then. Saving or testing also builds pending edits.",MessageType.Info);
            }
            EditorGUILayout.HelpBox("Auto Smooth On Rebuild recalculates tangent handles without moving your control points. Turn it off to retain custom tangents. Width handles change both road edges. Mark ground with SandboxTrackGround. Save the scene before testing.",MessageType.Info);
            if(!string.IsNullOrEmpty(track.ValidationError)) EditorGUILayout.HelpBox(track.ValidationError,MessageType.Error);
            if(!string.IsNullOrEmpty(track.SupportWarning)) EditorGUILayout.HelpBox(track.SupportWarning,MessageType.Warning);
        }
        void DrawRails(SandboxTrackGenerator track)
        {
            serializedObject.Update();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rails",EditorStyles.boldLabel);
            var enabled=serializedObject.FindProperty("Guardrails");
            EditorGUILayout.PropertyField(enabled,new GUIContent("Enable rails"));
            using(new EditorGUI.DisabledScope(!enabled.boolValue)) {
                DrawRail("Left",track.RailHeightForSide(-1),track.RailThicknessForSide(-1));
                DrawRail("Right",track.RailHeightForSide(1),track.RailThicknessForSide(1));
                DrawRailSections(track);
            }
            EditorGUILayout.HelpBox("Left and right follow the spline from START toward GOAL. Rebuild to apply rail changes.",MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }
        void DrawRail(string side,float height,float thickness)
        {
            EditorGUILayout.LabelField(side+" Rail",EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            var enabled=serializedObject.FindProperty(side+"RailEnabled");
            EditorGUILayout.PropertyField(enabled,new GUIContent("Enabled"));
            using(new EditorGUI.DisabledScope(!enabled.boolValue)) {
                EditorGUI.BeginChangeCheck();
                float h=EditorGUILayout.FloatField("Height",height);
                if(EditorGUI.EndChangeCheck()) serializedObject.FindProperty(side+"RailHeight").floatValue=h;
                EditorGUI.BeginChangeCheck();
                float w=EditorGUILayout.FloatField("Thickness",thickness);
                if(EditorGUI.EndChangeCheck()) serializedObject.FindProperty(side+"RailThickness").floatValue=w;
            }
            EditorGUI.indentLevel--;
        }
        void DrawRailSections(SandboxTrackGenerator track)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Individual rail sections",EditorStyles.boldLabel);
            var length=serializedObject.FindProperty("RailSectionLength");
            EditorGUI.BeginChangeCheck();
            float cm=EditorGUILayout.FloatField(new GUIContent("Section length (cm)","Measured along the authored track in scene space. Zero keeps each side continuous."),length.floatValue*100);
            if(EditorGUI.EndChangeCheck()) length.floatValue=cm/100;
            if(length.floatValue<=0 || !float.IsFinite(length.floatValue)) return;
            float total=0;
            for(int i=1;i<track.Samples.Length;i++) total+=Vector3.Distance(track.Samples[i-1],track.Samples[i]);
            total*=track.transform.lossyScale.x;
            int count=Mathf.Max(1,Mathf.CeilToInt(total/length.floatValue-.00001f));
            selectedRailSection=EditorGUILayout.IntSlider("Section",Mathf.Clamp(selectedRailSection+1,1,count),1,count)-1;
            EditorGUILayout.LabelField($"{selectedRailSection*length.floatValue*100:0.#}–{Mathf.Min(total,(selectedRailSection+1)*length.floatValue)*100:0.#} cm from START");
            DrawSectionSide(track,-1,selectedRailSection);
            DrawSectionSide(track,1,selectedRailSection);
            EditorGUILayout.HelpBox("Section numbers follow distance along the track. The last piece may be shorter. Changing section length changes which part of the road each number covers.",MessageType.Info);
        }
        void DrawSectionSide(SandboxTrackGenerator track,int side,int index)
        {
            string name=side<0?"Left":"Right";
            var sections=serializedObject.FindProperty(name+"RailSections");
            int found=-1;
            for(int i=0;i<sections.arraySize;i++) if(sections.GetArrayElementAtIndex(i).FindPropertyRelative("Index").intValue==index) {found=i;break;}
            var entry=found>=0?sections.GetArrayElementAtIndex(found):null;
            bool active=entry==null || entry.FindPropertyRelative("Enabled").boolValue;
            float oldHeight=entry==null?-1:entry.FindPropertyRelative("Height").floatValue;
            float oldThickness=entry==null?-1:entry.FindPropertyRelative("Thickness").floatValue;
            float height=oldHeight==-1?track.RailHeightForSide(side):oldHeight;
            float thickness=oldThickness==-1?track.RailThicknessForSide(side):oldThickness;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{name} section {index+1}",EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            bool enabled=EditorGUILayout.Toggle("Enabled",active);
            float h=height,w=thickness;
            using(new EditorGUI.DisabledScope(!enabled)) {
                h=EditorGUILayout.FloatField("Height",height);
                w=EditorGUILayout.FloatField("Thickness",thickness);
            }
            if(EditorGUI.EndChangeCheck()) {
                if(found<0) {found=sections.arraySize;sections.InsertArrayElementAtIndex(found);}
                entry=sections.GetArrayElementAtIndex(found);
                entry.FindPropertyRelative("Index").intValue=index;
                entry.FindPropertyRelative("Enabled").boolValue=enabled;
                entry.FindPropertyRelative("Height").floatValue=h!=height?h:oldHeight;
                entry.FindPropertyRelative("Thickness").floatValue=w!=thickness?w:oldThickness;
            }
            if(found>=0 && GUILayout.Button("Use side defaults")) sections.DeleteArrayElementAtIndex(found);
            if(GUILayout.Button("Highlight section")) {
                var piece=track.Generated?track.Generated.transform.Find($"{name} Rail/Section {index+1:000}"):null;
                if(piece) {EditorGUIUtility.PingObject(piece.gameObject);SceneView.lastActiveSceneView?.Frame(new Bounds(piece.GetComponent<Renderer>().bounds.center,piece.GetComponent<Renderer>().bounds.size*2),false);}
            }
            EditorGUILayout.EndVertical();
        }
        static void SetWidth(SandboxTrackGenerator t,int index,float width)
        {
            Undo.RecordObject(t.Curve,"Change track width"); t.SetKnotWidth(index,width); EditorUtility.SetDirty(t.Curve);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t.Curve); EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }
        void OnSceneGUI()
        {
            var t=(SandboxTrackGenerator)target;
            if(Application.isPlaying || t.Curve.Spline.Count<2) return;
            int index=Selected(t); var spline=t.Curve.Spline;
            int segment=Mathf.Min(index,spline.Count-2); float u=index==spline.Count-1?1:0;
            Vector3 tangent=CurveUtility.EvaluateTangent(spline.GetCurve(segment),u);
            if(tangent.sqrMagnitude<.000001f) tangent=(Vector3)spline[Mathf.Min(index+1,spline.Count-1)].Position-(Vector3)spline[Mathf.Max(0,index-1)].Position;
            var right=t.transform.TransformDirection(Vector3.Cross(Vector3.up,tangent).normalized);
            var center=t.transform.TransformPoint((Vector3)spline[index].Position);
            float scale=t.transform.lossyScale.x, half=t.KnotWidth(index)*scale*.5f;
            Handles.color=Color.cyan; Handles.DrawLine(center-right*half,center+right*half);
            foreach(int side in new[]{-1,1}) {
                var p=center+right*(side*half); EditorGUI.BeginChangeCheck();
                var moved=Handles.Slider(p,right*side,HandleUtility.GetHandleSize(p)*.07f,Handles.CubeHandleCap,0);
                if(EditorGUI.EndChangeCheck()) { SetWidth(t,index,Mathf.Max(.05f,Vector3.Dot(moved-center,right*side)*2/scale)); Repaint(); }
            }
            Handles.Label(center+Vector3.up*HandleUtility.GetHandleSize(center)*.12f,$"Point {index+1} width: {t.KnotWidth(index):0.00}");
        }
    }
}
