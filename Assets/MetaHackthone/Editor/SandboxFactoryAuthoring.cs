using System;
using System.IO;
using System.Linq;
using CloudGarden.Integration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Unity.Mathematics;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    [InitializeOnLoad]
    public static class SandboxFactoryAuthoring
    {
        public const string TemplatePath="Assets/MetaHackthone/Scenes/LevelFactory.unity";
        public const string Folder="Assets/MetaHackthone/Generated/LevelFactory";
        public const string PrefabPath=Folder+"/TrackGenerator.prefab";
        const string Pending="Factory.PendingCar";
        [Serializable] sealed class PendingCar { public string scene, owner, asset; }
        static SandboxFactoryAuthoring()
        {
            SandboxFactoryLevel.SaveCarRequested+=SaveCar;
            EditorApplication.update+=AutoTest;
            EditorApplication.playModeStateChanged+=state=> {
                if(state==PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall+=ApplySavedCar;
            };
        }
        static void AutoTest()
        {
            if(!Application.isPlaying || !SessionState.GetBool("Factory.AutoTest",false)) return;
            var level=Object.FindFirstObjectByType<DownhillController>();
            if(!level || !level.FactoryLevel || !level.Core || !level.IsBuilding) return;
            SessionState.EraseBool("Factory.AutoTest");
            if(level.FactoryLevel.TestCar && level.TryLoadFactoryCar(level.FactoryLevel.TestCar)) level.TryStart();
        }
        static Transform Child(string name,Transform parent,Vector3 position)
        {
            var t=new GameObject(name).transform; t.SetParent(parent,false); t.localPosition=position; return t;
        }
        static BoxCollider Volume(string name,Transform parent,Vector3 position,Vector3 size)
        {
            var box=Child(name,parent,position).gameObject.AddComponent<BoxCollider>(); box.size=size; box.isTrigger=true; return box;
        }
        public static SandboxTrackGenerator InitializeTrack(GameObject go)
        {
            var track=go.AddComponent<SandboxTrackGenerator>();
            track.DeckStyle=SandboxDeckStyle.CreekDeck;
            track.DeckModule=AssetDatabase.LoadAssetAtPath<GameObject>(CreekDeckBaker.ModulePath);
            track.RoadMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CreekDiorama/Materials/Honey timber 02.mat");
            track.TimberMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CreekDiorama/Materials/Honey timber 00.mat");
            track.RoadPhysics=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Folder+"/Road.physicMaterial");
            track.RailPhysics=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Folder+"/Rails.physicMaterial");
            var spline=new Spline();
            spline.Add(new BezierKnot(new float3(-8,6,0)),TangentMode.AutoSmooth);
            spline.Add(new BezierKnot(new float3(0,3,0)),TangentMode.AutoSmooth);
            spline.Add(new BezierKnot(new float3(8,3,0)),TangentMode.AutoSmooth);
            track.Curve.Spline=spline;
            for(int i=0;i<spline.Count;i++) track.SetKnotWidth(i,track.DefaultWidth);
            return track;
        }
        public static void EnsurePrefab()
        {
            SandboxTrackBaker.EnsureFolder(Folder);
            foreach(bool road in new[]{true,false}) {
                var path=Folder+(road?"/Road.physicMaterial":"/Rails.physicMaterial");
                if(!AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path)) {
                    var physics=new PhysicsMaterial(road?"Factory timber road":"Factory smooth rails") {staticFriction=road?.5f:0,dynamicFriction=road?.4f:0,bounciness=0,frictionCombine=PhysicsMaterialCombine.Minimum};
                    AssetDatabase.CreateAsset(physics,path);
                }
            }
            if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)) return;
            var preview=EditorSceneManager.NewPreviewScene(); var prior=SandboxTrackEditing.Suspended; SandboxTrackEditing.Suspended=true;
            GameObject go=null;
            try {
                go=new GameObject("Track Generator"); SceneManager.MoveGameObjectToScene(go,preview);
                var track=InitializeTrack(go); track.Supports=false;
                SandboxTrackBaker.Persist(track,Folder+"/PrefabMeshes",PrefabPath);
                track.Supports=true; track.BakedSignature="";
                go.transform.localScale=Vector3.one*(GravityGarage25DCourse.UnitScale*DownhillSettings.VehicleScale*1.2f);
                PrefabUtility.SaveAsPrefabAsset(go,PrefabPath);
            } finally { if(go) Object.DestroyImmediate(go); EditorSceneManager.ClosePreviewScene(preview); SandboxTrackEditing.Suspended=prior; }
        }
        public static void EnsureTemplate()
        {
            EnsurePrefab(); if(AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplatePath)) return;
            // Copy the already-authored platform and its persistent resources. Never regenerate it through runtime bootstrap.
            if(!AssetDatabase.CopyAsset("Assets/MetaHackthone/Scenes/DownhillSandbox.unity",TemplatePath)) throw new InvalidOperationException("Could not copy the existing platform scene.");
            var previous=SceneManager.GetActiveScene();
            bool suspended=SandboxTrackEditing.Suspended; SandboxTrackEditing.Suspended=true;
            var scene=EditorSceneManager.OpenScene(TemplatePath,OpenSceneMode.Additive);
            try {
                SceneManager.SetActiveScene(scene);
                var entry=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DownhillEntry>(true)).Single();
                var settings=Object.Instantiate(entry.Settings);
                settings.name="Factory settings"; settings.Campaign=false; settings.SandboxDrillCourse=false; settings.DesignerBuilder=true; settings.UseGravityGarage=true;
                settings.Level25D=null; settings.Level3D=null; settings.WorkbenchApproach=0;
                AssetDatabase.CreateAsset(settings,AssetDatabase.GenerateUniqueAssetPath(Folder+"/FactorySettings.asset")); entry.Settings=settings;
                var layout=entry.Layout; var course=layout.Downhill;
                var authored=layout.GetComponent<AuthoredSandboxLayout>(); if(authored) Object.DestroyImmediate(authored);
                foreach(Transform child in course.SandboxFrame.Cast<Transform>().ToArray()) if(child!=course.StartPoint) Object.DestroyImmediate(child.gameObject);
                course.FinishArea=null; course.PlayArea=null; course.SandboxPolish=false;
                course.SandboxFrame.localScale=Vector3.one*settings.PresentationScale;
                var factory=layout.gameObject.AddComponent<SandboxFactoryLevel>(); factory.Title="Blank Sandbox Level";
                factory.CourseScale=GravityGarage25DCourse.UnitScale*DownhillSettings.VehicleScale*settings.PresentationScale;
                factory.TrackRoot=Child("Tracks and puzzles",course.SandboxFrame,new Vector3(0,.65f,2));
                factory.TrackRoot.localScale=Vector3.one*(factory.CourseScale/course.SandboxFrame.lossyScale.x);
                factory.Start=Child("START - surface pose",factory.TrackRoot,Vector3.zero);
                factory.Finish=Volume("GOAL - move and resize",factory.TrackRoot,new Vector3(0,1,10),new Vector3(3,2,2));
                factory.TestBounds=Volume("TEST BOUNDS - move and resize",factory.TrackRoot,new Vector3(0,5,0),new Vector3(80,20,80));
                factory.TestBounds.enabled=false; factory.Tracks=Array.Empty<SandboxTrackGenerator>();
                SandboxFactorySamples.ConfigureKit(factory);
                SandboxFactorySamples.AssignPresets(factory);
                EditorSceneManager.SaveScene(scene);
            } finally {
                SandboxTrackEditing.Suspended=suspended;
                EditorSceneManager.CloseScene(scene,true); if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        [MenuItem("MetaHackthone/Level Factory/Create Level from Factory")]
        public static void CreateLevel()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureTemplate();
            string path=EditorUtility.SaveFilePanelInProject("Create factory level","NewFactoryLevel","unity","Choose the new level scene.");
            if(string.IsNullOrEmpty(path)) return;
            if(AssetDatabase.LoadMainAssetAtPath(path)) throw new InvalidOperationException("Choose a new scene name.");
            AssetDatabase.CopyAsset(TemplatePath,path);
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive); SceneManager.SetActiveScene(scene);
            MakeIndependent(scene,path); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject=Find(scene).gameObject; SceneView.lastActiveSceneView?.FrameSelected();
        }
        public static SandboxFactoryLevel Find(Scene scene) => scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SandboxFactoryLevel>(true)).FirstOrDefault();
        public static void MakeIndependent(Scene scene,string path)
        {
            var folder=Path.ChangeExtension(path,null)+"_Factory"; SandboxTrackBaker.EnsureFolder(folder);
            var entry=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DownhillEntry>(true)).Single();
            var settings=Object.Instantiate(entry.Settings); AssetDatabase.CreateAsset(settings,AssetDatabase.GenerateUniqueAssetPath(folder+"/Settings.asset")); entry.Settings=settings;
            var factory=Find(scene); factory.Title=Path.GetFileNameWithoutExtension(path);
            SandboxLevelLayoutAuthoring.Migrate(factory.GetComponent<BuildingSceneLayout>());
            SandboxBoundaryArtworkAuthoring.Apply(factory.GetComponent<BuildingSceneLayout>());
            factory.Layout.name="Level - "+factory.Title;
            if(factory.TestCar) { var car=Object.Instantiate(factory.TestCar); AssetDatabase.CreateAsset(car,AssetDatabase.GenerateUniqueAssetPath(folder+"/TestCar.asset")); factory.TestCar=car; }
            foreach(var track in SandboxTrackEditing.Tracks(scene)) { track.BakeOwner=""; SandboxTrackBaker.Rebuild(track,true); }
            if(!SandboxTrackEditing.Flush(scene,true)) throw new InvalidOperationException("Fix track validation before saving the new level.");
        }
        [MenuItem("MetaHackthone/Level Factory/Save Level As")]
        public static void SaveAs()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene=SceneManager.GetActiveScene(); if(!Find(scene)) throw new InvalidOperationException("Select a factory scene first.");
            string path=EditorUtility.SaveFilePanelInProject("Save factory level as",scene.name+"Copy","unity","Choose a new scene name.");
            if(string.IsNullOrEmpty(path)) return;
            if(AssetDatabase.LoadMainAssetAtPath(path)) throw new InvalidOperationException("Choose a new scene name.");
            if(!EditorSceneManager.SaveScene(scene,path,true)) return;
            var copy=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive); SceneManager.SetActiveScene(copy);
            MakeIndependent(copy,path); EditorSceneManager.SaveScene(copy);
        }
        [MenuItem("MetaHackthone/Level Factory/Add Track")]
        static void AddSelected() { var factory=Find(SceneManager.GetActiveScene()); if(!factory) throw new InvalidOperationException("Open a factory level first."); AddTrack(factory,true); }
        public static SandboxTrackGenerator AddTrack(SandboxFactoryLevel factory,bool undo=true)
        {
            EnsurePrefab();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject go;
            if(prefab.GetComponentsInChildren<SandboxTrackGenerator>(true).Length==1) go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,factory.TrackRoot);
            else { go=new GameObject("Track"); go.transform.SetParent(factory.TrackRoot,false); InitializeTrack(go); }
            go.transform.localPosition=Vector3.zero; go.transform.localRotation=Quaternion.identity; go.transform.localScale=Vector3.one;
            if(undo) Undo.RegisterCreatedObjectUndo(go,"Add track");
            var track=go.GetComponent<SandboxTrackGenerator>(); track.BakeOwner="";
            track.RoadPhysics=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Folder+"/Road.physicMaterial");
            track.RailPhysics=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Folder+"/Rails.physicMaterial");
            // Prefab instances initially share the immutable prefab bake; replacing the generated root must not delete it in the asset.
            SandboxTrackBaker.Rebuild(track,true);
            factory.Tracks=factory.TrackRoot.GetComponentsInChildren<SandboxTrackGenerator>(true);
            if(factory.Tracks.Length==1 && factory.Start) {
                if(undo) Undo.RecordObject(factory,"Bind level start");
                factory.BindStart=true; factory.StartBinding=new SandboxTrackBinding {Track=track,Distance=.5f};
                SandboxTrackEditing.UpdateStart(factory);
                if(factory.Finish) {
                    if(undo) Undo.RecordObject(factory.Finish.transform,"Place level goal");
                    var path=SandboxTrackBaker.SamplePath(track); var end=path[path.Count-1];
                    factory.Finish.transform.SetPositionAndRotation(track.transform.TransformPoint(end.Position+end.Up),track.transform.rotation*Quaternion.LookRotation(end.Forward,end.Up));
                }
            }
            EditorSceneManager.MarkSceneDirty(factory.gameObject.scene);
            if(undo) SandboxTrackEditing.Edit(track); return track;
        }
        [MenuItem("MetaHackthone/Level Factory/Test Level")]
        public static void TestLevel()
        {
            var scene=SceneManager.GetActiveScene(); var factory=Find(scene);
            if(!factory || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if(!factory.TestCar) { Debug.LogError("Choose a saved test car, or use Build Vehicle first."); return; }
            if(!SandboxTrackEditing.Flush(scene,true)) { Debug.LogError("Fix factory validation errors first."); return; }
            if(!factory.Validate(out var validation)) { Debug.LogError(validation); return; }
            // Test this scene alone. Other loaded authoring scenes must not spawn their own workshops.
            EditorSceneManager.SaveScene(scene);
            SessionState.SetBool("Factory.AutoTest",true);
            DesignModeEditor.Play(factory.GetComponent<BuildingSceneLayout>());
        }
        static void SaveCar(SandboxFactoryLevel factory,AssemblySaveV1 data)
        {
            string path=EditorUtility.SaveFilePanelInProject("Save test car",factory.TestCar?factory.TestCar.name:"FactoryTestCar","asset","Save the connected car assembly.");
            if(string.IsNullOrEmpty(path)) return;
            var car=AssetDatabase.LoadAssetAtPath<SandboxTestCarAsset>(path);
            if(!car && AssetDatabase.LoadMainAssetAtPath(path)) throw new InvalidOperationException("That asset is not a test car.");
            if(!car) { car=ScriptableObject.CreateInstance<SandboxTestCarAsset>(); AssetDatabase.CreateAsset(car,path); }
            car.Assembly=JsonUtility.FromJson<AssemblySaveV1>(JsonUtility.ToJson(data)); EditorUtility.SetDirty(car); AssetDatabase.SaveAssetIfDirty(car);
            factory.TestCar=car;
            SessionState.SetString(Pending,JsonUtility.ToJson(new PendingCar {scene=factory.gameObject.scene.path,owner=GlobalObjectId.GetGlobalObjectIdSlow(factory).ToString(),asset=path}));
            var level=Object.FindFirstObjectByType<DownhillController>(); if(level) level.Workshop.Session.Notify("Test car saved. Its level reference is retained when you exit Play Mode.");
        }
        static void ApplySavedCar()
        {
            var json=SessionState.GetString(Pending,""); if(string.IsNullOrEmpty(json)) return;
            var pending=JsonUtility.FromJson<PendingCar>(json); var scene=SceneManager.GetSceneByPath(pending.scene);
            if(!scene.IsValid() || !scene.isLoaded) return;
            var factory=Find(scene); if(!factory) return;
            Undo.RecordObject(factory,"Assign saved test car"); factory.TestCar=AssetDatabase.LoadAssetAtPath<SandboxTestCarAsset>(pending.asset);
            EditorUtility.SetDirty(factory); EditorSceneManager.MarkSceneDirty(scene); SessionState.EraseString(Pending);
        }
    }
    [CustomEditor(typeof(SandboxFactoryLevel))]
    public sealed class SandboxFactoryLevelEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); var factory=(SandboxFactoryLevel)target;
            var presets=factory.Presets.Where(p=>p).ToArray();
            if(presets.Length>0) {
                int current=Array.IndexOf(presets,factory.TestCar);
                EditorGUI.BeginChangeCheck(); int choice=EditorGUILayout.Popup("Test vehicle preset",current,presets.Select(p=>p.name).ToArray());
                if(EditorGUI.EndChangeCheck() && choice>=0) { Undo.RecordObject(factory,"Choose test vehicle"); factory.TestCar=presets[choice]; EditorUtility.SetDirty(factory); }
            }
            using(new EditorGUI.DisabledScope(Application.isPlaying)) {
                if(GUILayout.Button("Puzzle Palette")) SandboxPuzzlePalette.Open();
                if(GUILayout.Button("Build Vehicle")) DesignModeEditor.Play(factory.GetComponent<BuildingSceneLayout>());
                if(GUILayout.Button("Add Track")) SandboxFactoryAuthoring.AddTrack(factory);
                if(GUILayout.Button("Test Level")) { SceneManager.SetActiveScene(factory.gameObject.scene); SandboxFactoryAuthoring.TestLevel(); }
                if(GUILayout.Button("Save Level As")) { SceneManager.SetActiveScene(factory.gameObject.scene); SandboxFactoryAuthoring.SaveAs(); }
            }
            EditorGUILayout.HelpBox("Add a track, edit its spline, then place puzzles from the palette. Choose a vehicle and Test Level. Build Vehicle opens the existing workbench; Save Test Car preserves your assembly after Play Mode.",MessageType.Info);
        }
        void OnSceneGUI()
        {
            var f=(SandboxFactoryLevel)target; if(Application.isPlaying || !f.Start) return;
            Handles.color=Color.yellow; Handles.Label(f.Start.position,"START");
            EditorGUI.BeginChangeCheck(); var position=Handles.PositionHandle(f.Start.position,f.Start.rotation);
            if(EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(f,"Move start"); Undo.RecordObject(f.Start,"Move start");
                if(f.BindStart && f.StartBinding.Track) {
                    var binding=f.StartBinding; binding.Distance=SandboxPuzzleEditing.Nearest(binding.Track,position);
                    var s=SandboxPuzzleEditing.At(SandboxTrackBaker.SamplePath(binding.Track),binding.Distance);
                    binding.Offset=Quaternion.Inverse(Quaternion.LookRotation(s.Forward,s.Up))*(binding.Track.transform.InverseTransformPoint(position)-s.Position);
                    f.StartBinding=binding; SandboxTrackEditing.UpdateStart(f);
                } else f.Start.position=position;
                EditorUtility.SetDirty(f);
            }
        }
    }
}
