using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
using Beat=PhysicalAssembly.SandboxFirstEntryIntro.Beat;
namespace PhysicalAssembly.Tests {
    // Writes what the review camera sees, one image per frame, after everything else has moved for that frame.
    [DefaultExecutionOrder(32000)]
    public sealed class SandboxIntroRecorder : MonoBehaviour {
        public string Folder;public int Frames;public bool Recording;
        RenderTexture target;Texture2D texture;
        void LateUpdate(){
            var camera=Camera.main;if(!Recording||!camera)return;
            if(!target){target=new RenderTexture(1280,720,24);texture=new Texture2D(1280,720,TextureFormat.RGB24,false);}
            var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
            camera.targetTexture=oldTarget;RenderTexture.active=oldActive;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Folder,"frame"+Frames.ToString("D5")+".jpg"),texture.EncodeToJPG(92));Frames++;
        }
        void OnDestroy(){if(target){target.Release();Destroy(target);}if(texture)Destroy(texture);}
    }
    // The first-entry intro on a desktop camera: the same code path as the headset apart from the passthrough layer.
    public class SandboxIntroPlaybackTests {
        bool had;int old;float fixedStep;
        [SetUp] public void Enable(){
            had=PlayerPrefs.HasKey(SandboxFirstEntryIntro.SeenKey);old=PlayerPrefs.GetInt(SandboxFirstEntryIntro.SeenKey);PlayerPrefs.DeleteKey(SandboxFirstEntryIntro.SeenKey);
            DesignModeSession.SetEnabled(false);SandboxCampaignSession.Reset();
            // The intro's own clock runs fast here; the two physics runs still take their real time.
            SandboxFirstEntryIntro.Suppressed=false;SandboxFirstEntryIntro.Speed=8;
        }
        [TearDown] public void Disable(){
            SandboxFirstEntryIntro.Suppressed=true;SandboxFirstEntryIntro.Speed=1;
            if(had)PlayerPrefs.SetInt(SandboxFirstEntryIntro.SeenKey,old);else PlayerPrefs.DeleteKey(SandboxFirstEntryIntro.SeenKey);PlayerPrefs.Save();
        }
        static IEnumerator Until(Func<bool> done,float seconds,string what){
            float limit=Time.realtimeSinceStartup+seconds;
            while(!done()){
                if(Time.realtimeSinceStartup>limit)Assert.Fail("Timed out waiting for "+what+"; intro stage "+(SandboxFirstEntryIntro.Current?SandboxFirstEntryIntro.Current.Stage.ToString():"none"));
                yield return null;
            }
        }
        IEnumerator Load(){
            SceneManager.LoadScene("DownhillSandbox");yield return null;yield return null;
            fixedStep=Time.fixedDeltaTime;
            Assert.IsTrue(SandboxFirstEntryIntro.Playing,"Normal campaign entry to Level 1 starts the intro");
            yield return Until(()=>SandboxFirstEntryIntro.Current&&SandboxFirstEntryIntro.Current.Stage>=Beat.Darken,5,"the intro to lock the level");
        }
        static void AssertFreshLevelOne(WorkshopBootstrap owner,(Vector3 position,Quaternion rotation)[] poses,float fixedStep,string when){
            var level=owner.Downhill;var world=owner.World;
            Assert.IsFalse(SandboxFirstEntryIntro.Playing,when);Assert.IsTrue(owner.enabled,when);Assert.IsTrue(level.enabled,when);Assert.IsTrue(level.CanSwitchMode,when);
            Assert.IsTrue(owner.Arena.gameObject.activeSelf,"Level visible "+when);Assert.IsTrue(level.ActiveLayout.BuildingRoot.gameObject.activeSelf,"Building area visible "+when);
            Assert.IsNull(GameObject.Find("Sandbox intro"),"Nothing of the intro remains "+when);Assert.IsNull(GameObject.Find(SandboxFirstEntryIntro.TitleText),when);
            Assert.AreEqual(fixedStep,Time.fixedDeltaTime,"Physics step restored "+when);Assert.AreEqual(0,SandboxFragments.ActiveCount,"No particles left behind "+when);
            // The half-built starter car, exactly where the level put it.
            Assert.IsTrue(level.StarterCarBuilt,when);
            var car=world.GetConnectedParts(level.Core.InstanceId);
            Assert.AreEqual(4,car.Count,"Core, long block and two wheels "+when);Assert.AreEqual(3,world.Connections.Count,when);
            Assert.AreEqual(2,level.Kit.Count(p=>p.gameObject.activeInHierarchy&&p.Kind==PartKind.DownhillSmallWheel&&!car.Contains(p)),"Two loose wheels "+when);
            var kit=level.Kit.ToArray();
            for(int i=0;i<kit.Length;i++){
                Assert.Less(Vector3.Distance(poses[i].position,kit[i].MainBody.transform.position),.0002f,kit[i].Kind+" position "+when);
                Assert.Less(Quaternion.Angle(poses[i].rotation,kit[i].MainBody.transform.rotation),.05f,kit[i].Kind+" rotation "+when);
            }
            Assert.AreEqual(SandboxCourseId.BeautifulCorner,SandboxCampaignSession.Current);Assert.IsFalse(SandboxCampaignSession.IsComplete(SandboxCourseId.BeautifulCorner),"The intro never completes a level");
            AssertSteeringRim(owner,when);
        }
        // The steering rim under the build: the core at its centre, drawn where it is grabbed, and a hand on it takes hold.
        static void AssertSteeringRim(WorkshopBootstrap owner,string when){
            var level=owner.Downhill;var wheel=level.RotationWheel.transform;var up=level.Course.WorkbenchFrame.up;
            Assert.IsTrue(wheel.gameObject.activeInHierarchy,"The steering rim is shown "+when);
            var rim=wheel.Find("Steering rim").GetComponent<Renderer>();
            Assert.Less(Vector3.ProjectOnPlane(level.Core.MainBody.transform.position-rim.bounds.center,up).magnitude,.005f,"The core sits at the centre of the steering rim "+when);
            Assert.Less(Vector3.Distance(rim.bounds.center,wheel.position),.001f,"The rim is drawn where the wheel is grabbed "+when);
            var grip=wheel.TransformPoint(new Vector3(DownhillRotationWheel.Radius,0,0));
            Assert.IsTrue(level.RotationWheel.IsNearRim(grip),"A hand on the drawn rim is on the wheel "+when);
            Assert.IsTrue(level.BeginRotation(0,grip),"The steering rim can be grabbed "+when);level.EndRotation(0);
        }
        // Follows one physics run to its end, with a still from a second into it.
        static IEnumerator Watch(SandboxFirstEntryIntro intro,int index,Beat during,string still){
            float limit=Time.realtimeSinceStartup+40;bool captured=false;
            while(intro.Stage<=during){
                if(Time.realtimeSinceStartup>limit)Assert.Fail("Timed out waiting for run "+(index+1)+" to end");
                if(!captured&&intro.RunTime[index]>=1){captured=true;Capture(still);}
                yield return null;
            }
        }
        static (Vector3,Quaternion)[] KitPoses(DownhillController level)=>level.Kit.Select(p=>(p.MainBody.transform.position,p.MainBody.transform.rotation)).ToArray();

        [UnityTest] public IEnumerator CoreFailsTwiceThenHandsOverAFreshLevelOne(){
            yield return Load();
            var owner=Object.FindFirstObjectByType<WorkshopBootstrap>();var level=owner.Downhill;var intro=SandboxFirstEntryIntro.Current;
            Assert.IsFalse(level.CanSwitchMode,"Input stays locked");Assert.IsFalse(owner.Arena.gameObject.activeSelf,"The level starts hidden");
            foreach(var part in level.Kit)Assert.IsFalse(part.gameObject.activeInHierarchy,"The player's kit is put away");
            var poses=KitPoses(level);

            yield return Until(()=>intro.Stage>=Beat.Orb,30,"the spark");yield return null;
            Assert.AreEqual(SandboxFirstEntryIntro.AdriftParts,intro.Effects.Drifters,"Parts hang adrift in the dark for the spark to find");
            yield return Until(()=>intro.Stage>=Beat.CoreBirth,30,"the orb to settle");Capture("CoreIntro-1-orb.png");
            Assert.GreaterOrEqual(intro.Effects.Pushed,2,"The spark pushes aside the parts in its way");            yield return Until(()=>intro.Stage>=Beat.FirstCar,30,"the core");Capture("CoreIntro-2-core.png");
            Assert.Less(intro.Effects.DarkestRoom,.2f,"The room was darkened");
            // The pull: a blueprint of the build on the core, and tethers of light that catch each part and reel it in.
            var fx=intro.Effects;
            yield return Until(()=>fx.BlueprintSlots>0,30,"the blueprint");
            Assert.AreEqual(1,fx.Pulls);Assert.AreEqual(4,fx.BlueprintSlots,"A see-through copy of each part of car one");
            yield return Until(()=>fx.Tethers>0&&fx.Held>0,30,"the first part to be caught");Capture("CoreIntro-2b-pull.png");
            yield return Until(()=>fx.BlueprintSlots<4||intro.Stage>=Beat.Launch,30,"a part to land");Capture("CoreIntro-2c-pull.png");
            Assert.Less(fx.BlueprintSlots,4,"A landed part fills its place in the blueprint");
            yield return Until(()=>intro.Stage>=Beat.Launch,30,"car one");Capture("CoreIntro-3-first-car.png");
            Assert.AreEqual(0,fx.BlueprintSlots,"The blueprint is gone once the car is built");Assert.AreEqual(0,fx.Held);Assert.AreEqual(0,fx.Tethers,"No tether is left");
            var first=intro.Cars[0];float unit=level.TestScale*BuildingGeometry.Unit;
            Assert.AreEqual(2,first.Count(PartKind.DownhillSmallWheel),"Car one is the nonsense stack: two wheels");Assert.AreEqual(2,first.Count(PartKind.DownhillLongBlock)+first.Count(PartKind.DownhillShortBlock),"and two blocks");
            Assert.AreEqual(0,intro.Effects.Drifters,"The parts adrift are gone once the track appears");
            yield return Until(()=>intro.Stage>=Beat.FirstRun,30,"the launch");Capture("CoreIntro-4-on-track.png");
            Assert.IsTrue(owner.Arena.gameObject.activeSelf,"The track is revealed");Assert.IsFalse(level.ActiveLayout.BuildingRoot.gameObject.activeSelf,"Track view: no building area");
            foreach(var part in level.Kit)Assert.IsFalse(part.gameObject.activeInHierarchy,"The kit stays away during the runs");
            yield return Watch(intro,0,Beat.FirstRun,"CoreIntro-4b-first-run.png");Capture("CoreIntro-5-first-failure.png");
            Assert.AreEqual(1,intro.Failures);Assert.AreEqual(SandboxFirstEntryIntro.RunEnd.Rest,intro.RunEnded[0],"Car one is left alone until it has stopped by itself");
            Assert.IsFalse(SandboxFactoryLevel.Contains(level.FactoryLevel.Finish,first.Core.MainBody.transform.position),"Car one does not finish");
            float tilt=Vector3.Angle(first.Core.MainBody.transform.up,level.FactoryLevel.Start.up);
            Debug.Log($"INTRO_RUN car=1 unit={unit:F4} distance={intro.RunDistance[0]:F3} tilt={tilt:F1} seconds={intro.RunTime[0]:F2} ended={intro.RunEnded[0]}");
            Assert.IsTrue(tilt>20||intro.RunDistance[0]>unit,"Car one visibly fails: it topples or slides");
            // Nothing throws the parts aside when the core lets go: they are left as the car stopped.
            var cars=first.Core.GetComponentInParent<AssemblyWorld>();var across=level.FactoryLevel.Start.right;
            float[] Sideways()=>first.Attracted.Select(p=>Vector3.Dot(p.MainBody.Rigidbody.linearVelocity,across)).ToArray();
            var before=Sideways();
            yield return Until(()=>cars.GetConnectedParts(first.Core.InstanceId).Count==1,30,"the core to let go");
            var after=Sideways();
            for(int i=0;i<before.Length;i++)Assert.Less(Mathf.Abs(after[i]-before[i]),.5f,"Nothing pushes a part aside when the core lets go");
            yield return Until(()=>intro.Stage>=Beat.SecondCar&&fx.Tethers>0,30,"the second pull");Capture("CoreIntro-5b-pull.png");
            Assert.AreEqual(2,intro.Effects.Pulls);
            yield return Until(()=>intro.Stage>=Beat.SecondRun,30,"car two");Capture("CoreIntro-6-second-car.png");
            var second=intro.Cars[1];
            Assert.AreEqual(2,second.Count(PartKind.DownhillSmallWheel),"Car two is nearly the starter car: two wheels");Assert.AreEqual(1,second.Count(PartKind.DownhillLongBlock));Assert.AreEqual(0,second.Count(PartKind.DownhillShortBlock));
            yield return Watch(intro,1,Beat.SecondRun,"CoreIntro-6b-second-run.png");Capture("CoreIntro-7-second-failure.png");
            Assert.AreEqual(2,intro.Failures);Assert.AreNotEqual(SandboxFirstEntryIntro.RunEnd.Timeout,intro.RunEnded[1],"Car two fails by itself: it stops or runs off the track");
            Assert.IsFalse(SandboxFactoryLevel.Contains(level.FactoryLevel.Finish,second.Core.MainBody.transform.position),"Car two does not finish");
            Debug.Log($"INTRO_RUN car=2 unit={unit:F4} distance={intro.RunDistance[1]:F3} tilt={Vector3.Angle(second.Core.MainBody.transform.up,level.FactoryLevel.Start.up):F1} seconds={intro.RunTime[1]:F2} ended={intro.RunEnded[1]}");
            Assert.Greater(intro.RunDistance[1],2*unit,"Car two moves before it fails");
            yield return Until(()=>intro.Stage>=Beat.StarterCar,30,"the core to come home");Capture("CoreIntro-8-home.png");
            Assert.IsTrue(level.ActiveLayout.BuildingRoot.gameObject.activeSelf,"The building area is back");
            yield return Until(()=>fx.Pulls>=3&&fx.Tethers>0,30,"the last pull");Capture("CoreIntro-8b-pull.png");
            Assert.AreEqual(3,intro.Effects.Pulls);
            yield return Until(()=>intro.Stage>=Beat.Turn,30,"the starter car");Capture("CoreIntro-9-starter-car.png");
            Assert.AreEqual(0,fx.BlueprintSlots+fx.Held+fx.Tethers,"Nothing of the pull is left once the player's kit is back");
            // The steering rim stays under the core while the build is whirled around.
            float adrift=0,turnLimit=Time.realtimeSinceStartup+30;var rimRoot=level.RotationWheel.transform;var benchUp=level.Course.WorkbenchFrame.up;
            while(intro.Stage<Beat.Title){
                if(Time.realtimeSinceStartup>turnLimit)Assert.Fail("Timed out waiting for the turn");
                adrift=Mathf.Max(adrift,Vector3.ProjectOnPlane(level.Core.MainBody.transform.position-rimRoot.position,benchUp).magnitude);yield return null;
            }
            Assert.Less(adrift,.005f,"The steering rim stays under the core through the turn");
            var title=GameObject.Find(SandboxFirstEntryIntro.TitleText);Assert.IsNotNull(title);Assert.AreEqual("Your turn",title.GetComponent<TextMesh>().text,"The closing words");Assert.IsTrue(SandboxFirstEntryIntro.Playing,"Input stays locked during the title");
            yield return Until(()=>!SandboxFirstEntryIntro.Playing,30,"the end");yield return null;
            Assert.AreEqual(1,PlayerPrefs.GetInt(SandboxFirstEntryIntro.SeenKey),"Seen only after the whole intro");
            Assert.AreEqual(1,intro.Effects.Room,"Room brightness request cleared");
            AssertFreshLevelOne(owner,poses,fixedStep,"after the intro");
            Assert.NotNull(level.GetComponent<SandboxIntroMenuGuide>(),"Completed intro starts the core-to-palm guide");
            SandboxFirstEntryIntro.TryBegin(owner);yield return null;Assert.IsFalse(SandboxFirstEntryIntro.Playing,"It does not replay");
        }
        IEnumerator Interrupt(Beat at){
            yield return Load();
            var owner=Object.FindFirstObjectByType<WorkshopBootstrap>();var intro=SandboxFirstEntryIntro.Current;var poses=KitPoses(owner.Downhill);
            yield return Until(()=>intro.Stage>=at,60,at.ToString());yield return null;yield return null;
            Object.Destroy(intro);yield return null;yield return null;
            Assert.AreEqual(0,PlayerPrefs.GetInt(SandboxFirstEntryIntro.SeenKey),"An interrupted intro plays again next time");
            AssertFreshLevelOne(owner,poses,fixedStep,"after interrupting at "+at);
            Assert.IsNull(owner.Downhill.GetComponent<SandboxIntroMenuGuide>(),"An interrupted intro must not start menu guidance");
        }
        [UnityTest] public IEnumerator InterruptedWhileTheOrbFliesRestoresEverything()=>Interrupt(Beat.Orb);
        [UnityTest] public IEnumerator InterruptedDuringThePullRestoresEverything()=>Interrupt(Beat.FirstCar);
        [UnityTest] public IEnumerator InterruptedDuringAPhysicsRunRestoresEverything()=>Interrupt(Beat.FirstRun);
        [UnityTest] public IEnumerator InterruptedDuringTheTurnRestoresEverything()=>Interrupt(Beat.Turn);
        [UnityTest] public IEnumerator SuppressedIntroNeverStarts(){
            SandboxFirstEntryIntro.Suppressed=true;
            SceneManager.LoadScene("DownhillSandbox");yield return null;yield return null;yield return null;
            Assert.IsFalse(SandboxFirstEntryIntro.Playing);
            var owner=Object.FindFirstObjectByType<WorkshopBootstrap>();Assert.IsTrue(owner.Downhill.Core.gameObject.activeInHierarchy);Assert.IsTrue(owner.Arena.gameObject.activeSelf);
            yield return null;AssertSteeringRim(owner,"without the intro");
        }
        // Not part of the suite: run it by name. Plays the whole intro at normal speed and writes every frame to the
        // system temp folder so a review video can be encoded from them at the frame rate it logs. Silent.
        [UnityTest,Explicit("Writes about 1300 frames for a review video.")] public IEnumerator RecordPlaythroughForReview(){
            SandboxFirstEntryIntro.Speed=1;int oldRate=Application.targetFrameRate,oldSync=QualitySettings.vSyncCount;
            string folder=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"MetaHackthoneIntroRecording");
            if(System.IO.Directory.Exists(folder))System.IO.Directory.Delete(folder,true);System.IO.Directory.CreateDirectory(folder);
            var recorder=new GameObject("Intro recorder").AddComponent<SandboxIntroRecorder>();Object.DontDestroyOnLoad(recorder.gameObject);recorder.Folder=folder;
            try{
                yield return Load();
                // The game really runs at 30 frames a second while recording, so the intro's unscaled clock, its effects
                // and the physics all stay in step. (Time.captureFramerate leaves unscaled time running in real time.)
                QualitySettings.vSyncCount=0;Application.targetFrameRate=30;
                recorder.Recording=true;float began=Time.realtimeSinceStartup,limit=began+900;
                while(SandboxFirstEntryIntro.Playing){if(Time.realtimeSinceStartup>limit)Assert.Fail("Recording timed out");yield return null;}
                for(int i=0;i<45;i++)yield return null;
                float seconds=Time.realtimeSinceStartup-began;
                Debug.Log($"INTRO_RECORDED frames={recorder.Frames} seconds={seconds:F1} fps={recorder.Frames/seconds:F2} folder={folder}");
            }finally{Application.targetFrameRate=oldRate;QualitySettings.vSyncCount=oldSync;Object.Destroy(recorder.gameObject);}
        }
        // Review stills for whoever tunes the intro; the desktop camera follows the same focus the eye is led to.
        static void Capture(string name){
            var camera=Camera.main;if(!camera)return;var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            var target=new RenderTexture(1280,720,24);var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
                var folder=System.IO.Path.Combine(Application.dataPath,"../ArtSource/SandboxIntro");System.IO.Directory.CreateDirectory(folder);System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name),texture.EncodeToPNG());
            }finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;target.Release();Object.Destroy(target);Object.Destroy(texture);}
        }
    }
}
