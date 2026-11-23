using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicalAssembly
{
    public sealed partial class DownhillController
    {
        public float WheelStepDegrees=5, WheelTickVolume=.18f;
        public float LaunchDecomposeSeconds=.15f, LaunchTravelSeconds=.5f, LaunchReconstructSeconds=.2f; // + Settings.LaunchPause = 1 s
        public int WheelStepsPlayed { get; private set; }
        readonly List<SandboxReturnEffect> launchEffects=new();
        readonly List<(Renderer renderer,bool enabled)> launchRenderers=new();
        float wheelRaw, wheelAccumulated, wheelApplied, wheelCredit;
        bool wheelRebase;
        bool CampaignPresentation => Settings.Campaign && Course.SandboxPolish;
        void ResetWheelSteps(){wheelRaw=wheelAccumulated=wheelApplied=wheelCredit=0;wheelRebase=false;}
        void SuspendWheelSteps(){wheelRebase=true;wheelCredit=0;}
        void ApplyWheelStep(float raw)
        {
            if(wheelRebase){wheelRaw=raw;wheelAccumulated=wheelApplied;wheelRebase=false;return;}
            wheelAccumulated+=Mathf.DeltaAngle(wheelRaw,raw);wheelRaw=raw;
            float step=Mathf.Max(.1f,WheelStepDegrees);
            float radius=moving.Max(s=>Vector3.Distance(s.Body.Rigidbody.position,rotationPivot));
            float speed=Mathf.Min(180,Mathf.Rad2Deg*1.5f/Mathf.Max(.05f,radius));
            wheelCredit=Mathf.Min(step,wheelCredit+speed*Time.fixedDeltaTime);
            if(wheelCredit<step || Mathf.Abs(wheelAccumulated-wheelApplied)<step)return;
            wheelApplied+=Mathf.Sign(wheelAccumulated-wheelApplied)*step;wheelCredit-=step;
            var delta=Quaternion.AngleAxis(wheelApplied,Course.WorkbenchFrame.up);
            foreach(var s in moving){var rb=s.Body.Rigidbody;var target=delta*Course.WorkbenchFrame.rotation*s.Rotation;var turn=target*Quaternion.Inverse(rb.rotation);rb.MovePosition(rotationPivot+turn*(rb.position-rotationPivot));rb.MoveRotation(target);}
            Presentation.FollowCore(Core);
            if(!RotationWheel.transform.IsChildOf(Presentation.WorkbenchVisuals))RotationWheel.transform.rotation=delta*wheelStartRotation;
            Physics.SyncTransforms();WheelStepsPlayed++;
            if(Feedback) Feedback.Emit(SandboxSoundCue.WheelTick,RotationWheel.transform.position,null,default,WheelTickVolume/.18f);
        }
        void ClearLaunchEffects()
        {
            foreach(var effect in launchEffects)if(effect){effect.Restore();Destroy(effect.gameObject);}launchEffects.Clear();
            foreach(var item in launchRenderers)if(item.renderer)item.renderer.enabled=item.enabled;launchRenderers.Clear();
        }
        void PrepareLaunchEffects(List<(AssemblyBody body,Vector3 position,Quaternion rotation)> destinations)
        {
            foreach(var item in destinations){item.body.Rigidbody.position=item.position;item.body.Rigidbody.rotation=item.rotation;item.body.transform.SetPositionAndRotation(item.position,item.rotation);}
            foreach(var pair in simulationOriginals){
                var effect=new GameObject("Launch transfer "+pair.Value.name).AddComponent<SandboxReturnEffect>();effect.transform.SetParent(transform,false);launchEffects.Add(effect);
                var body=pair.Key.MainBody;
                effect.Initialize(pair.Value,pair.Key,Course.WorkbenchFrame,Course.WorkbenchFrame.InverseTransformPoint(body.transform.position),Quaternion.Inverse(Course.WorkbenchFrame.rotation)*body.transform.rotation,
                    LaunchDecomposeSeconds,LaunchTravelSeconds,LaunchReconstructSeconds,Mathf.Max(1,SandboxFragments.Budget/Mathf.Max(1,simulationOriginals.Count)),false);
                foreach(var renderer in pair.Key.VisualRenderers){launchRenderers.Add((renderer,renderer.enabled));renderer.enabled=false;}
            }
        }
        IEnumerator LaunchTransfer()
        {
            float age=0,total=Mathf.Max(.01f,LaunchDecomposeSeconds)+Mathf.Max(.01f,LaunchTravelSeconds)+Mathf.Max(.01f,LaunchReconstructSeconds);
            bool complete=false;
            while(!complete){yield return null;age+=Time.unscaledDeltaTime;complete=true;foreach(var effect in launchEffects)if(effect && !effect.Advance(Time.unscaledDeltaTime))complete=false;Presentation.SetCoursePosition(Vector3.Lerp(Presentation.BuildPosition,Presentation.TestPosition,Mathf.SmoothStep(0,1,age/total)));}
            ClearLaunchEffects();
            Physics.SyncTransforms();testStart=Capture(SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies)).ToArray();transition=null;BeginCountdown();
        }
    }
}
