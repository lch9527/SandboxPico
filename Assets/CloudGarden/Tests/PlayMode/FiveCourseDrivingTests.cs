using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace PhysicalAssembly.Tests {
 public class FiveCourseDrivingTests {
  DownhillController level;
  [UnityTearDown] public IEnumerator Cleanup(){if(level){typeof(DownhillController).GetProperty("GarageComparing").SetValue(level,false);level.RestoreBuildImmediately();}yield return null;DesignModeSession.SetEnabled(false);SandboxCampaignSession.Reset();SceneManager.LoadScene("MainMenu");yield return null;}
  [UnityTest] public IEnumerator TutorialCarReachesGoalRepeatedly()=>Run(DesignLevelId.BeautifulCorner);
  [UnityTest] public IEnumerator WheelCarReachesGoalRepeatedly()=>Run(DesignLevelId.GravityGarage);
  [UnityTest] public IEnumerator CrabCarReachesGoalRepeatedly()=>Run(DesignLevelId.WaterfallValley);
  [UnityTest] public IEnumerator DrillCarReachesGoalRepeatedly()=>Run(DesignLevelId.DrillDropBridge);
  // Level 5's default car must break the stone, cross the drop bridge and finish.
  [UnityTest] public IEnumerator FinalRunDefaultCarReachesGoalRepeatedly()=>Run(DesignLevelId.FactoryFinalRun);
  [UnityTest] public IEnumerator IncorrectCarsFailPhysically(){
   yield return Run(DesignLevelId.GravityGarage,"Reversed wheels",false);
   yield return Run(DesignLevelId.WaterfallValley,"Narrow car",false);
   yield return Run(DesignLevelId.DrillDropBridge,"No drill",false);
   yield return Run(DesignLevelId.DrillDropBridge,"Low drill",false);
  }
  IEnumerator Run(DesignLevelId course,string preset=null,bool success=true){
   foreach(var id in new[]{course}) {
    DesignModeSession.SetEnabled(true);DesignModeSession.SelectLevel(id);
    SceneManager.LoadScene("DownhillSandbox");yield return null;yield return null;
    level=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
    Assert.AreEqual((SandboxCourseId)id,level.CurrentCourse);
    Assert.IsTrue(id==DesignLevelId.BeautifulCorner ? level.TryLoadDesignPreset() : level.TryLoadFactoryCar(preset==null?level.FactoryLevel.TestCar:level.FactoryLevel.Presets.Single(p=>p.name==preset)));
    level.FactoryLevel.DebugPhysics=true;
    if(!success)level.FactoryLevel.Timeout=15;
    level.Settings.CountdownStep=.01f;level.Settings.GoDuration=.01f;level.Settings.TransitionDuration=0;
    level.LaunchDecomposeSeconds=level.LaunchTravelSeconds=level.LaunchReconstructSeconds=.01f;
    for(int attempt=0;attempt<(success?5:1);attempt++) {
     yield return null;
     Assert.IsTrue(level.TryStart());
     // Automated runs own focus; reuse the existing comparison guard.
     typeof(DownhillController).GetProperty("GarageComparing").SetValue(level,true);
     float deadline=Time.realtimeSinceStartup+8;
     while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline)yield return null;
     Assert.AreEqual(DownhillState.Running,level.State);
     deadline=Time.realtimeSinceStartup+65;
     while(level.State==DownhillState.Running && Time.realtimeSinceStartup<deadline)yield return null;
     Debug.Log(string.Join("; ",Object.FindObjectsByType<SandboxPuzzleContact>(FindObjectsSortMode.None).Select(p=>p.name+":"+p.LastSurface+":"+p.Normal)));
     Assert.AreEqual(success?DownhillState.Success:DownhillState.Failed,level.State,id+" attempt "+attempt+" position "+(level.SimulationCore?level.FactoryLevel.TrackRoot.InverseTransformPoint(level.SimulationCore.MainBody.transform.position).ToString():"no car"));
     typeof(DownhillController).GetProperty("GarageComparing").SetValue(level,false);
     level.RestoreBuildImmediately();yield return null;
    }
    level.RestoreBuildImmediately();yield return null;
   }
  }
 }
}
