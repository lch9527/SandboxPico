using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAssembly.Editor {
 [InitializeOnLoad]
 public static class SceneryMotionPreview {
  const string Menu="MetaHackthone/Design Mode/Preview Scenery Motion";
  static readonly Dictionary<CarSceneryBreeze,Quaternion> poses=new();
  static double start;
  static bool running;
  static SceneryWindSettings windSettings;
  public static bool Running=>running;
  static SceneryMotionPreview() {
   EditorApplication.update+=Tick;
   EditorSceneManager.sceneSaving+=(s,p)=>Stop();
   EditorSceneManager.sceneClosing+=(s,r)=>Stop();
   EditorApplication.playModeStateChanged+=s=>{Stop();if(s==PlayModeStateChange.EnteredEditMode)SceneryWindClock.Set(false,false,0);};
   AssemblyReloadEvents.beforeAssemblyReload+=Stop;
   Undo.undoRedoPerformed+=Stop;
   Selection.selectionChanged+=Stop;
   EditorSceneManager.activeSceneChangedInEditMode+=(a,b)=>Stop();
   SceneView.duringSceneGui+=v=>{if(running && Event.current.type==EventType.MouseDown)Stop();};
   if(!Application.isPlaying)SceneryWindClock.Set(false,false,0);
  }
  [MenuItem(Menu)] public static void Toggle() {
   if(running){Stop();return;}
   if(EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())return;
   windSettings=null;
   foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
    if(!windSettings)windSettings=root.GetComponentInChildren<SceneryWindSettings>();
   if(windSettings)windSettings.Apply();else SceneryWindClock.Configure(1,1);
   foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
    foreach(var motion in root.GetComponentsInChildren<CarSceneryBreeze>())
     if(motion.enabled)poses[motion]=motion.transform.localRotation;
   AnimationMode.StartAnimationMode();running=true;start=EditorApplication.timeSinceStartup;
   SceneryWindClock.Set(true,true,0);
  }
  [MenuItem(Menu,true)] static bool Validate(){UnityEditor.Menu.SetChecked(Menu,running);return !EditorApplication.isPlayingOrWillChangePlaymode;}
  public static void Stop(){
   if(!running)return;
   running=false;AnimationMode.StopAnimationMode();poses.Clear();SceneryWindClock.Set(Application.isPlaying,false,0);SceneView.RepaintAll();
  }
  static void Tick(){
   if(!running)return;
   if(!AnimationMode.InAnimationMode()){running=false;poses.Clear();SceneryWindClock.Set(false,false,0);return;}
   SceneryWindClock.Set(true,true,(float)(EditorApplication.timeSinceStartup-start));
   if(windSettings)windSettings.Apply();
   AnimationMode.BeginSampling();
   foreach(var pair in poses){
    if(!pair.Key)continue;
    var target=pair.Key.transform;var pose=pair.Value;
    for(int i=0;i<4;i++){
     string property="m_LocalRotation."+"xyzw"[i];
     AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("",typeof(Transform),property),
      new PropertyModification{target=target,propertyPath=property,value=pose[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture)},true);
    }
    target.localRotation=pair.Key.EvaluateRotation(pose,(float)(EditorApplication.timeSinceStartup-start));
   }
   AnimationMode.EndSampling();SceneView.RepaintAll();
  }
 }
}
