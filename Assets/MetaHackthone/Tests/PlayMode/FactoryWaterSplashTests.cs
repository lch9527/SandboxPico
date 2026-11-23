using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    // Campaign water is scenery without colliders; the factory run treats its flat surfaces as a hazard.
    public sealed class FactoryWaterSplashTests
    {
        DownhillController level;
        static void Comparing(DownhillController l,bool value) => typeof(DownhillController).GetProperty("GarageComparing").SetValue(l,value);

        [UnityTest] public IEnumerator FallingIntoCampaignRiverSplashesOnceAndFails()
        {
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("DownhillSandbox"); yield return null; yield return null;
            var workshop=Object.FindFirstObjectByType<WorkshopBootstrap>(); level=workshop.Downhill;
            Assert.IsNotNull(level.FactoryLevel,"Campaign starts on a factory course");
            Assert.Greater(level.FactoryWaterSurfaceCount,0,"The Level 1 river is detected as a water surface");

            var cues=new List<SandboxSoundCue>(); level.Audio.CuePlayed+=cues.Add;
            var splashes=0; level.Feedback.Presented+=e=>{ if(e.Cue==SandboxSoundCue.Splash) splashes++; };
            Assert.IsTrue(level.TryStart()); Comparing(level,true);
            float deadline=Time.realtimeSinceStartup+5;
            while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.AreEqual(DownhillState.Running,level.State);

            // Driving on the track above the river is not a water failure.
            for(int i=0;i<20;i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(DownhillState.Running,level.State,"Run continues away from the water: "+workshop.Session.Status);
            Assert.AreEqual(0,splashes);

            // Drop the car just above the middle of a river triangle.
            var river=level.FactoryLevel.GetComponentsInChildren<MeshFilter>(true).First(f=>f.name=="Living river surface");
            var mesh=river.sharedMesh; var t=mesh.triangles; var v=mesh.vertices;
            var centre=river.transform.TransformPoint((v[t[0]]+v[t[1]]+v[t[2]])/3f);
            var bodies=level.SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies).Select(b=>b.Rigidbody).ToArray();
            float lowest=level.SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies).SelectMany(b=>b.Colliders).Min(c=>c.bounds.min.y);
            var core=level.SimulationCore.MainBody.Rigidbody.position;
            var shift=new Vector3(centre.x-core.x,centre.y+.05f-lowest,centre.z-core.z);
            foreach(var body in bodies) { body.position+=shift; body.transform.position+=shift; body.linearVelocity=Vector3.down*.5f; body.angularVelocity=Vector3.zero; }

            deadline=Time.realtimeSinceStartup+3;
            while(level.State==DownhillState.Running && Time.realtimeSinceStartup<deadline) yield return new WaitForFixedUpdate();
            Assert.AreEqual(DownhillState.Failed,level.State,"Falling into the river ends the run at once");
            StringAssert.Contains("water",workshop.Session.Status);
            Assert.AreEqual(1,splashes,"One splash event");
            CollectionAssert.Contains(cues,SandboxSoundCue.Splash,"The splash is actually played");
            for(int i=0;i<10;i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(1,splashes,"No repeat splash after the run ends");

            // Retry starts clean: the car is back on the track and dry.
            Assert.IsTrue(level.TryRetry());
            deadline=Time.realtimeSinceStartup+5;
            while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<deadline) yield return null;
            for(int i=0;i<10;i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(DownhillState.Running,level.State);
            Assert.AreEqual(1,splashes);
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(level) Comparing(level,false);
            SandboxCampaignSession.Reset(); DesignModeSession.SetEnabled(false);
            SceneManager.LoadScene("MainMenu"); yield return null;
        }
    }
}
