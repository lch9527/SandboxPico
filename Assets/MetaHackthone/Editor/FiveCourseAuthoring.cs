using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Unity.Mathematics;
using Object=UnityEngine.Object;
namespace PhysicalAssembly.Editor {
 public static class FiveCourseAuthoring {
  const string Scenes="Assets/MetaHackthone/Scenes/", Exports="Assets/MetaHackthone/Generated/Campaign/";
  // Authoring files are named by displayed campaign number (SandboxCourseCatalog order):
  // Level2 = Crab Bridge, Level3 = Wheel Size Challenge, Level4 = Drill Drop Bridge.
  static SandboxCourseId Id(int n)=>SandboxCourseCatalog.Courses[n-1];
  static string File(int n)=>"Level"+n;
  [MenuItem("MetaHackthone/Design Mode/Level 4 - Drill Drop Bridge")]
  public static void Open4()=>Open(4);
  public static void Open(int n) { if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(Scenes+File(n)+".unity"); }
  [MenuItem("MetaHackthone/Level Factory/Save Level 2 to Game")] public static void Save2()=>Save(2);
  [MenuItem("MetaHackthone/Level Factory/Save Level 3 to Game")] public static void Save3()=>Save(3);
  [MenuItem("MetaHackthone/Level Factory/Save Level 4 to Game")] public static void Save4()=>Save(4);
  public static void Save(int n) {
   var scene=SceneManager.GetActiveScene(); if(scene.path!=Scenes+File(n)+".unity") throw new InvalidOperationException("Open the matching authoring scene first.");
   if(!SandboxTrackEditing.Flush(scene,true)) throw new InvalidOperationException("Fix track validation before saving.");
   var f=SandboxFactoryAuthoring.Find(scene); var prefab=SandboxFactoryCampaignSave.Export(f,Exports+File(n)+".prefab",Exports+File(n)+"Meshes",f.Title);
   EditorSceneManager.SaveScene(scene); Register(n,prefab); AssetDatabase.SaveAssets();
  }
  static void Register(int n,SandboxFactoryLevel f) {
   foreach(var guid in AssetDatabase.FindAssets("t:DownhillSettings",new[]{"Assets/MetaHackthone"})) {
    var s=AssetDatabase.LoadAssetAtPath<DownhillSettings>(AssetDatabase.GUIDToAssetPath(guid));
    var id=Id(n);
    if(id==SandboxCourseId.GravityGarage)s.WheelSizeCourse=f; else if(id==SandboxCourseId.TeleportPipes)s.CrabBridgeCourse=f; else s.DrillBridgeCourse=f;
    EditorUtility.SetDirty(s);
   }
  }
  public static void CreateCourses() {
   if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
   var original=SceneManager.GetActiveScene();
   foreach(var pair in new[]{("Scenes/Level4.unity","Scenes/Level5.unity"),("Generated/Campaign/Level4.prefab","Generated/Campaign/Level5.prefab"),("Generated/Campaign/Level4Meshes","Generated/Campaign/Level5Meshes")}) {
    string a="Assets/MetaHackthone/"+pair.Item1,b="Assets/MetaHackthone/"+pair.Item2;
    if(AssetDatabase.LoadMainAssetAtPath(a) && !AssetDatabase.LoadMainAssetAtPath(b)) {var error=AssetDatabase.MoveAsset(a,b);if(error.Length>0)throw new InvalidOperationException(error);}
   }
   for(int n=2;n<=4;n++) {
    string path=Scenes+"Level"+n+".unity";
    if(AssetDatabase.LoadAssetAtPath<SceneAsset>(path)) continue;
    if(!AssetDatabase.CopyAsset(Scenes+"Level1.unity",path)) throw new InvalidOperationException("Scene copy failed");
    var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive); SceneManager.SetActiveScene(scene);
    bool suspended=SandboxTrackEditing.Suspended; SandboxTrackEditing.Suspended=true;
    try {
     var f=SandboxFactoryAuthoring.Find(scene);
     SandboxFactoryAuthoring.MakeIndependent(scene,path);
     // Detach goal before removing the original bridge/stations from this independent copy.
     f.Finish.transform.SetParent(f.TrackRoot,true);
     foreach(var track in f.Tracks) Object.DestroyImmediate(track.gameObject);
     f.Tracks=Array.Empty<SandboxTrackGenerator>();
     var id=Id(n); bool wheel=id==SandboxCourseId.GravityGarage, crab=id==SandboxCourseId.TeleportPipes, drill=id==SandboxCourseId.DrillDropBridge;
     foreach(var name in new[]{"04 Timber bridge","Station_Start","Station_Goal","Anchor_Start","Anchor_Goal"}) {
      var oldVisual=f.TrackRoot.Find(name); if(oldVisual)Object.DestroyImmediate(oldVisual.gameObject);
     }
     foreach(var art in f.Scenery.Where(t=>t)) {
      art.SetParent(f.TrackRoot,true);
      foreach(var t in art.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="04 Timber bridge" || t.name.Contains("Station_Start") || t.name.Contains("Station_Goal")).ToArray()) if(t) Object.DestroyImmediate(t.gameObject);
      foreach(var collider in art.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
      art.localPosition=new Vector3(0,-7,18); art.localRotation=Quaternion.Euler(0,id==SandboxCourseId.GravityGarage?0:id==SandboxCourseId.TeleportPipes?15:-15,0);
     }
     f.Title=SandboxCourseCatalog.Title(id); f.Layout.CourseId=id; f.Layout.name="Level "+n+" - "+f.Title;
     f.UseStarterPalette=true; f.LimitedPathAssist=true; f.Timeout=60;
     var trackNew=SandboxFactoryAuthoring.AddTrack(f,false); trackNew.Supports=false; trackNew.ShowGeneratedVisuals=true;
     trackNew.DeckStyle=SandboxDeckStyle.CreekDeck;
     trackNew.DeckModule=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CreekDiorama/Prefabs/Modules/Deck_Straight_080.prefab");
     trackNew.DefaultWidth=crab?2.2f:2.8f; trackNew.Guardrails=false;
     var spline=new Spline();
     foreach(var v in new[]{new float3(0,4,0),new float3(0,3.7f,2),new float3(0,drill?3:2,9),new float3(0,drill?2:1.65f,15),new float3(0,drill?1:1.3f,22),new float3(0,drill?0:.95f,29)}) spline.Add(new BezierKnot(v),TangentMode.AutoSmooth);
     trackNew.Curve.Spline=spline;
     var sampled=SandboxTrackBaker.SamplePath(trackNew);
     float at=SandboxPuzzleEditing.Nearest(trackNew,trackNew.transform.TransformPoint(new Vector3(0,1,17)));
     if(wheel) {var p=(SandboxRoller)SandboxPuzzleEditing.Add(trackNew,typeof(SandboxRoller),at);p.Width=trackNew.DefaultWidth;p.Diameter=.20f;}
     if(crab) {var p=(SandboxCrabBridge)SandboxPuzzleEditing.Add(trackNew,typeof(SandboxCrabBridge),at);p.Length=7;p.HoleWidth=1.1f;p.LeftLaneWidth=p.RightLaneWidth=.55f;}
     if(drill) {var p=(SandboxDropBridge)SandboxPuzzleEditing.Add(trackNew,typeof(SandboxDropBridge),at);p.Length=3;p.Width=2.8f;p.TargetApproach=5;p.TargetLayer=3;p.TargetSize=.5f;p.SupportSide=-2;p.ReleaseSuspensionWithTarget=true;}
     f.BindStart=true;f.StartBinding=new SandboxTrackBinding{Track=trackNew,Distance=2.5f};
     f.Finish.transform.SetPositionAndRotation(trackNew.transform.TransformPoint(new Vector3(0,drill?1:2,28)),trackNew.transform.rotation); f.Finish.size=new Vector3(4,3,1);
     f.TestBounds.center=new Vector3(0,0,15);f.TestBounds.size=new Vector3(30,30,65);
     SandboxFactorySamples.AssignPresets(f);
     f.TestCar=f.Presets.Single(p=>p.name==(wheel?"Large front wheels":crab?"Wide crab":"High drill"));
     SandboxTrackEditing.Suspended=false;
     if(!SandboxTrackEditing.Flush(scene,true))throw new InvalidOperationException(trackNew.ValidationError);
     SandboxTrackHierarchyAuthoring.Apply(f);
     EditorSceneManager.SaveScene(scene);Save(n);
    } finally {SandboxTrackEditing.Suspended=suspended;EditorSceneManager.CloseScene(scene,true);}
   }
   SceneManager.SetActiveScene(original); AssetDatabase.SaveAssets();
  }
 }
}
