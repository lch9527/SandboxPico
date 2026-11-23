using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    [Ignore("Legacy 2.5D garage and 3D valley courses are no longer reachable: the campaign is five factory courses and advancing requires a published factory prefab.")]
    public sealed class CreekCampaignTests
    {
        DownhillController activeLevel;
        static void Comparing(DownhillController level,bool value) => typeof(DownhillController).GetProperty("GarageComparing").SetValue(level,value);
        [UnityTest] public IEnumerator SavedFirstPresetFinishesFiveRuns() => Runs(false);
        [UnityTest] public IEnumerator SavedSecondPresetFinishesFiveRuns() => Runs(true);

        IEnumerator Runs(bool second)
        {
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("DownhillSandbox"); yield return null;
            yield return null;
            var workshop=Object.FindFirstObjectByType<WorkshopBootstrap>();
            var level=workshop.Downhill;
            activeLevel=level;
            SandboxCampaignSession.Complete(SandboxCourseId.BeautifulCorner); Assert.IsTrue(level.TryAdvanceLevel()); yield return null;
            float firstScale=level.TestScale;
            if(second) { SandboxCampaignSession.Complete(SandboxCourseId.GravityGarage); Assert.IsTrue(level.TryAdvanceLevel()); }
            Assert.That(level.TestScale,Is.EqualTo(firstScale*(second?.6f:1f)).Within(.00001f));
            for(int attempt=0;attempt<5;attempt++) {
                Assert.AreEqual(second?SandboxCourseId.TeleportPipes:SandboxCourseId.GravityGarage,level.CurrentCourse);
                DesignModeSession.SetEnabled(true);
                DesignModeSession.SelectLevel(second?DesignLevelId.WaterfallValley:DesignLevelId.GravityGarage);
                Assert.IsTrue(level.TryLoadDesignerExample(),"Preset construction failed");
                DesignModeSession.SetEnabled(false);
                Assert.IsTrue(level.TryStart());
                // This unattended physics test owns the run. Use the existing
                // comparison guard so editor focus changes cannot cancel it.
                Comparing(level,true);
                Assert.That(level.SimulationCore.GeometryScale,Is.EqualTo(firstScale*(second?.6f:1f)).Within(.00001f));
                float deadline=Time.realtimeSinceStartup+90;
                while(!level.IsBuilding && !level.VictoryInProgress && level.State!=DownhillState.Success && level.State!=DownhillState.Failed && Time.realtimeSinceStartup<deadline)
                    yield return null;
                Assert.IsTrue(level.VictoryInProgress || level.State==DownhillState.Success,
                    "Attempt "+attempt+": "+workshop.Session.Status+" / "+(level.Run3D?level.Run3D.Feedback:level.GarageResult?.Outcome)+" position="+(level.SimulationCore?level.SimulationCore.transform.position.ToString("F4"):"returned")+" result="+JsonUtility.ToJson(level.GarageResult));
                Debug.Log("CREEK_PRESET_PASS level="+(second?2:1)+" attempt="+(attempt+1));
                // A level with a finish crowd rests longer: the crowd swarms the car, then lifts the core.
                float returnDeadline=Time.realtimeSinceStartup+10;
                while(level.VictoryInProgress && Time.realtimeSinceStartup<returnDeadline) yield return null;
                Assert.IsFalse(level.VictoryInProgress,"Victory return did not finish");
                Comparing(level,false);
                Debug.Log("CREEK_RETURN_PASS attempt="+(attempt+1));
            }
        }
        [TearDown] public void Cleanup() { if(activeLevel)Comparing(activeLevel,false); DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset(); }
    }
}
