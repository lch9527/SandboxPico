#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace CloudGarden.Integration
{
    // Development-build render A/B test: which effect costs the GPU time in the sandbox view.
    // Cold-launch with --ez metahackthone.render_ab true, enter the sandbox by hand and keep
    // looking at the workbench. Only renderer visibility changes; it is restored afterwards.
    public sealed class QuestRenderAB : MonoBehaviour
    {
        const string Prefix="QUEST_RENDER_AB ";
        const string BoundsShader="Creek Diorama/Assembly Bounds", NeonShader="Creek Diorama/Neon Frame";
        const string LightweightBoundsShader="Creek Diorama/Lightweight Range Frame";
        const float Settle=2, Sample=4;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            using var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity=player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent=activity.Call<AndroidJavaObject>("getIntent");
            if(intent.Call<bool>("getBooleanExtra","metahackthone.render_live",false)) {
                bool liveAuto=intent.Call<bool>("getBooleanExtra","metahackthone.render_ab_auto",false);
                intent.Call("removeExtra","metahackthone.render_live"); intent.Call("removeExtra","metahackthone.render_ab_auto");
                var liveRunner=new GameObject("Quest render live"); DontDestroyOnLoad(liveRunner);
                var liveTest=liveRunner.AddComponent<QuestRenderAB>(); liveTest.live=true; liveTest.auto=liveAuto;
                return;
            }
            if(!intent.Call<bool>("getBooleanExtra","metahackthone.render_ab",false)) return;
            bool auto=intent.Call<bool>("getBooleanExtra","metahackthone.render_ab_auto",false);
            bool compare=intent.Call<bool>("getBooleanExtra","metahackthone.render_ab_spacewarp",false);
            intent.Call("removeExtra","metahackthone.render_ab"); intent.Call("removeExtra","metahackthone.render_ab_auto"); intent.Call("removeExtra","metahackthone.render_ab_spacewarp");
            var runner=new GameObject("Quest render A/B");
            DontDestroyOnLoad(runner); var test=runner.AddComponent<QuestRenderAB>(); test.auto=auto; test.compare=compare;
        }

        // Unattended mode: enters the sandbox itself and turns the rig so the resting headset faces the workbench.
        bool auto;
        // SpaceWarp comparison: SpaceWarp on/off and a resolution cap, with longer samples.
        bool compare;
        // Upper limit on the dynamic-resolution viewport for the current variant (1 = no cap).
        float viewportCap=1;
        Transform target;

        // Live mode: render settings are switched from the PC while someone wears the headset and
        // judges the picture. Once a second it reads one line of key=value pairs from
        // <persistentDataPath>/render_live.txt and logs what is active plus the GPU numbers.
        // Keys: eye_scale, foveation, dynamic_foveation, sharpen (none|normal|quality),
        // dynamic_resolution (on|off), min_scale, depth_texture (on|off), msaa. The command is
        // re-applied every second because the game re-applies its own render settings.
        // With the auto extra it enters the sandbox itself and keeps facing the workbench.
        // Nothing is restored; relaunch to reset.
        bool live;
        const string LivePrefix="QUEST_RENDER_LIVE ";
        IEnumerator Live()
        {
            string path=System.IO.Path.Combine(Application.persistentDataPath,"render_live.txt"), last="";
            try { if(System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch(Exception e) { Debug.Log(LivePrefix+"WARN "+e.Message); }
            Debug.Log(LivePrefix+"READY file="+path+" auto="+auto);
            if(auto) {
                float deadline=Time.realtimeSinceStartup+30;
                while(!GardenNavigation.Current && Time.realtimeSinceStartup<deadline) yield return null;
                yield return new WaitForSecondsRealtime(3);
                if(GardenNavigation.Current) { PhysicalAssembly.DesignModeSession.SetEnabled(false); GardenNavigation.Current.EnterSandbox(); }
                deadline=Time.realtimeSinceStartup+120;
                while(Time.realtimeSinceStartup<deadline) {
                    var nav=GardenNavigation.Current;
                    if(nav && nav.DownhillWorkshop && !nav.IsTransitioning) break;
                    yield return null;
                }
                bool entered=GardenNavigation.Current && GardenNavigation.Current.DownhillWorkshop;
                if(entered) { yield return new WaitForSecondsRealtime(5); compare=true; target=GardenNavigation.Current.DownhillWorkshop.Downhill.Course.BuildArea.transform; }
                Debug.Log(LivePrefix+"SANDBOX entered="+entered);
            }
            var displays=new List<UnityEngine.XR.XRDisplaySubsystem>();
            var gpu=new List<float>(); var util=new List<float>(); var viewport=new List<float>();
            int frames=0, dropped=OVRPlugin.GetPerfMetricsInt(OVRPlugin.PerfMetrics.Compositor_DroppedFrameCount_Int)??0;
            float next=Time.realtimeSinceStartup+1;
            while(true) {
                frames++; viewport.Add(UnityEngine.XR.XRSettings.renderViewportScale);
                Add(gpu,OVRPlugin.PerfMetrics.App_GpuTime_Float); Add(util,OVRPlugin.PerfMetrics.System_GpuUtilPercentage_Float);
                if(Time.realtimeSinceStartup>=next) {
                    SubsystemManager.GetSubsystems(displays);
                    string text=last;
                    try { if(System.IO.File.Exists(path)) text=System.IO.File.ReadAllText(path).Trim(); } catch(Exception e) { Debug.Log(LivePrefix+"WARN "+e.Message); }
                    bool changed=text!=last; last=text;
                    if(last.Length>0) ApplyLive(last,displays,changed);
                    Aim();
                    int now=OVRPlugin.GetPerfMetricsInt(OVRPlugin.PerfMetrics.Compositor_DroppedFrameCount_Int)??0;
                    var manager=OVRManager.instance; float eye=UnityEngine.XR.XRSettings.eyeTextureResolutionScale;
                    float mean=viewport.Count>0?viewport.Average():1;
                    Debug.Log(LivePrefix+"STATE fps="+frames+" dropped="+(now-dropped)+" app_gpu="+Mean(gpu)+" gpu_util="+Mean(util)
                        +" eye_scale="+F(eye)+" viewport="+F(mean)+" effective_scale="+F(eye*mean)+" eye_tex="+UnityEngine.XR.XRSettings.eyeTextureWidth+"x"+UnityEngine.XR.XRSettings.eyeTextureHeight
                        +" dynamic_resolution="+(manager && manager.enableDynamicResolution)+" min_scale="+(manager?F(manager.minDynamicResolutionScale):"n/a")+" max_scale="+(manager?F(manager.maxDynamicResolutionScale):"n/a")
                        +" foveation="+(displays.Count>0?F(displays[0].foveatedRenderingLevel):"n/a")+" dynamic_foveation="+OVRManager.useDynamicFoveatedRendering+" ovr_foveation="+OVRManager.foveatedRenderingLevel+" ffr_supported="+OVRManager.fixedFoveatedRenderingSupported
                        +" sharpen="+(manager?manager.sharpenType.ToString():"n/a")+" msaa="+QualitySettings.antiAliasing
                        +" depth_texture="+((UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)?.supportsCameraDepthTexture)
                        +" aim_error="+F(AimError())+" command=["+last+"]");
                    frames=0; dropped=now; gpu.Clear(); util.Clear(); viewport.Clear(); next=Time.realtimeSinceStartup+1;
                }
                yield return null;
            }
        }
        static void ApplyLive(string text,List<UnityEngine.XR.XRDisplaySubsystem> displays,bool log)
        {
            var manager=OVRManager.instance;
            var urp=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            foreach(var pair in text.Split(new[]{' ',',',';'},StringSplitOptions.RemoveEmptyEntries)) {
                int split=pair.IndexOf('='); if(split<1) { if(log) Debug.Log(LivePrefix+"BAD "+pair); continue; }
                string key=pair.Substring(0,split).Trim().ToLowerInvariant(), value=pair.Substring(split+1).Trim().ToLowerInvariant();
                bool on=value=="on" || value=="true" || value=="1";
                bool number=float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out float amount);
                switch(key) {
                    case "eye_scale":
                        if(!number) goto default;
                        // URP copies its render scale into the eye-buffer scale every frame, so both are set.
                        if(urp && urp.renderScale!=amount) urp.renderScale=amount;
                        if(UnityEngine.XR.XRSettings.eyeTextureResolutionScale!=amount) UnityEngine.XR.XRSettings.eyeTextureResolutionScale=amount;
                        if(manager) manager.maxDynamicResolutionScale=amount;
                        break;
                    case "foveation": if(!number) goto default; foreach(var d in displays) d.foveatedRenderingLevel=amount; break;
                    case "dynamic_foveation": OVRManager.useDynamicFoveatedRendering=on; break;
                    case "sharpen":
                        if(manager) manager.sharpenType=value=="quality"?OVRPlugin.LayerSharpenType.Quality:value=="normal"?OVRPlugin.LayerSharpenType.Normal:OVRPlugin.LayerSharpenType.None;
                        break;
                    case "dynamic_resolution":
                        if(manager && manager.enableDynamicResolution!=on) manager.enableDynamicResolution=on;
                        if(!on) { UnityEngine.XR.XRSettings.renderViewportScale=1; ScalableBufferManager.ResizeBuffers(1,1); }
                        break;
                    case "min_scale": if(!number) goto default; if(manager) manager.minDynamicResolutionScale=amount; break;
                    // The Meta SDK's own foveation call: 0 off, 1 low, 2 medium, 3 high, 4 high top.
                    case "ovr_foveation": if(!number) goto default; if((int)OVRManager.foveatedRenderingLevel!=(int)amount) OVRManager.foveatedRenderingLevel=(OVRManager.FoveatedRenderingLevel)(int)amount; break;
                    case "depth_texture": if(urp && urp.supportsCameraDepthTexture!=on) urp.supportsCameraDepthTexture=on; break;
                    case "msaa": if(!number) goto default; if(urp && urp.msaaSampleCount!=(int)amount) urp.msaaSampleCount=(int)amount; break;
                    default: if(log) Debug.Log(LivePrefix+"BAD "+pair); break;
                }
            }
            if(log) Debug.Log(LivePrefix+"APPLIED "+text);
        }

        IEnumerator Start()
        {
            if(live) { yield return Live(); yield break; }
            Debug.Log(Prefix+"WAITING auto="+auto);
            float deadline=Time.realtimeSinceStartup+30;
            if(auto) {
                while(!GardenNavigation.Current && Time.realtimeSinceStartup<deadline) yield return null;
                yield return new WaitForSecondsRealtime(3);
                if(GardenNavigation.Current) { PhysicalAssembly.DesignModeSession.SetEnabled(false); GardenNavigation.Current.EnterSandbox(); }
            }
            deadline=Time.realtimeSinceStartup+240;
            while(Time.realtimeSinceStartup<deadline) {
                var nav=GardenNavigation.Current;
                if(nav && nav.DownhillWorkshop && !nav.IsTransitioning) break;
                yield return null;
            }
            if(!GardenNavigation.Current || !GardenNavigation.Current.DownhillWorkshop) { Debug.Log(Prefix+"FAIL sandbox was not entered"); Destroy(gameObject); yield break; }
            yield return new WaitForSecondsRealtime(8);
            if(auto) target=GardenNavigation.Current.DownhillWorkshop.Downhill.Course.BuildArea.transform;

            var all=FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var clouds=all.Where(r=>r.name.StartsWith("Cloud puff ")).ToArray();
            var bounds=all.Where(r=>Uses(r,BoundsShader)).ToArray();
            var boundary=all.Where(r=>Uses(r,BoundsShader) || Uses(r,NeonShader) || Uses(r,LightweightBoundsShader)).ToArray();
            var transparent=all.Where(r=>!clouds.Contains(r) && !boundary.Contains(r) && r.sharedMaterials.Any(m=>m && m.renderQueue>=2500)).ToArray();
            var cloudMaterials=clouds.Select(r=>r.sharedMaterials).ToArray();
            Debug.Log(Prefix+"BEGIN renderers="+all.Length+" clouds="+clouds.Length+" bounds_shader="+bounds.Length+" boundary="+boundary.Length+" other_transparent="+transparent.Length
                +" bounds_names="+string.Join("|",bounds.Select(r=>r.name+"@"+r.transform.lossyScale.ToString("F2"))));

            var opaque=all.Where(r=>!clouds.Contains(r) && !boundary.Contains(r) && !transparent.Contains(r)).ToArray();
            var displays=new List<UnityEngine.XR.XRDisplaySubsystem>(); SubsystemManager.GetSubsystems(displays);
            float foveation=displays.Count>0?displays[0].foveatedRenderingLevel:0;
            Debug.Log(Prefix+"SETTINGS eye_scale="+F(UnityEngine.XR.XRSettings.eyeTextureResolutionScale)+" eye_tex="+UnityEngine.XR.XRSettings.eyeTextureWidth+"x"+UnityEngine.XR.XRSettings.eyeTextureHeight
                +" msaa="+QualitySettings.antiAliasing+" foveation="+F(foveation)+" opaque="+opaque.Length);
            var urp=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            int msaa=urp?urp.msaaSampleCount:0; float eyeScale=UnityEngine.XR.XRSettings.eyeTextureResolutionScale;
            bool spaceWarp=OVRManager.GetSpaceWarp();
            Debug.Log(Prefix+"SPACEWARP requested="+spaceWarp);
            var variants=new (string name,Action apply)[] {
                ("baseline",()=>{}),
                ("cloud_shells_off",()=>{ foreach(var r in clouds) r.sharedMaterials=new[]{r.sharedMaterials[0]}; }),
                ("clouds_off",()=>Show(clouds,false)),
                ("boundary_all_off",()=>Show(boundary,false)),
                ("other_transparent_off",()=>Show(transparent,false)),
                ("opaque_scene_off",()=>Show(opaque,false)),
                ("everything_off",()=>Show(all,false)),
                ("spacewarp_off",()=>OVRManager.SetSpaceWarp(false)),
                ("foveation_high",()=>{ foreach(var d in displays) d.foveatedRenderingLevel=1; }),
                ("msaa_2",()=>{ if(urp) urp.msaaSampleCount=2; }),
                ("eye_scale_1.25",()=>UnityEngine.XR.XRSettings.eyeTextureResolutionScale=1.25f),
                ("eye_scale_1.0",()=>UnityEngine.XR.XRSettings.eyeTextureResolutionScale=1),
                ("eye_scale_1.0_everything_off",()=>{ UnityEngine.XR.XRSettings.eyeTextureResolutionScale=1; Show(all,false); }),
                ("eye_scale_1.0_foveation_high",()=>{ UnityEngine.XR.XRSettings.eyeTextureResolutionScale=1; foreach(var d in displays) d.foveatedRenderingLevel=1; }),
                ("eye_scale_1.0_msaa_2",()=>{ UnityEngine.XR.XRSettings.eyeTextureResolutionScale=1; if(urp) urp.msaaSampleCount=2; }),
                ("baseline_again",()=>{}),
            };
            // Caps are a fraction of the 1.6x eye buffer: 0.75 = 1.2x and 0.625 = 1.0x the headset default.
            float hz=OVRManager.display!=null?OVRManager.display.displayFrequency:0;
            Debug.Log(Prefix+"DISPLAY hz="+F(hz)+" available="+(OVRManager.display!=null?string.Join("/",OVRManager.display.displayFrequenciesAvailable):""));
            if(compare) variants=new (string name,Action apply)[] {
                ("spacewarp_120hz",()=>SetHz(120)),
                ("spacewarp_90hz",()=>SetHz(90)),
                ("no_spacewarp_72hz",()=>{ OVRManager.SetSpaceWarp(false); SetHz(72); }),
                ("no_spacewarp_90hz",()=>{ OVRManager.SetSpaceWarp(false); SetHz(90); }),
                ("spacewarp_120hz_again",()=>SetHz(120)),
            };
            float settle=compare?5:Settle, sample=compare?6:Sample;
            for(int round=1;round<=2;round++)
                foreach(var variant in variants) {
                    Aim(); variant.apply();
                    float settled=Time.realtimeSinceStartup+settle;
                    while(Time.realtimeSinceStartup<settled) { CapViewport(); yield return null; }
                    var frames=new List<float>(); var gpu=new List<float>(); var cpu=new List<float>(); var util=new List<float>(); var viewport=new List<float>();
                    int dropped=OVRPlugin.GetPerfMetricsInt(OVRPlugin.PerfMetrics.Compositor_DroppedFrameCount_Int)??0;
                    float start=Yaw(), drift=0, end=Time.realtimeSinceStartup+sample;
                    while(Time.realtimeSinceStartup<end) {
                        CapViewport(); viewport.Add(UnityEngine.XR.XRSettings.renderViewportScale);
                        frames.Add(Time.unscaledDeltaTime*1000);
                        Add(gpu,OVRPlugin.PerfMetrics.App_GpuTime_Float); Add(cpu,OVRPlugin.PerfMetrics.App_CpuTime_Float); Add(util,OVRPlugin.PerfMetrics.System_GpuUtilPercentage_Float);
                        drift=Mathf.Max(drift,Mathf.Abs(Mathf.DeltaAngle(start,Yaw())));
                        yield return null;
                    }
                    frames.Sort();
                    Debug.Log(Prefix+"RESULT round="+round+" variant="+variant.name
                        +" fps="+F(frames.Count/sample)+" dropped="+((OVRPlugin.GetPerfMetricsInt(OVRPlugin.PerfMetrics.Compositor_DroppedFrameCount_Int)??0)-dropped)+" viewport_mean="+Mean(viewport)+" frame_ms_median="+F(frames[frames.Count/2])+" frame_ms_p95="+F(frames[(int)((frames.Count-1)*.95f)])
                        +" app_gpu="+Mean(gpu)+" app_cpu="+Mean(cpu)+" gpu_util="+Mean(util)
                        +" head_yaw="+F(start)+" head_pitch="+F(Pitch())+" yaw_drift="+F(drift)+" aim_error="+F(AimError())+" hz="+F(OVRManager.display!=null?OVRManager.display.displayFrequency:0)+" asw_mode="+OVRPlugin.GetPerfMetricsInt(OVRPlugin.PerfMetrics.Compositor_SpaceWarp_Mode_Int)+" viewport="+F(UnityEngine.XR.XRSettings.renderViewportScale)+" eye_tex="+UnityEngine.XR.XRSettings.eyeTextureWidth+"x"+UnityEngine.XR.XRSettings.eyeTextureHeight);
                    // Restore everything before the next variant.
                    viewportCap=1; SetHz(hz); Show(all,true); if(spaceWarp && !OVRManager.GetSpaceWarp()) OVRManager.SetSpaceWarp(true); UnityEngine.XR.XRSettings.eyeTextureResolutionScale=eyeScale; if(urp) urp.msaaSampleCount=msaa; foreach(var d in displays) d.foveatedRenderingLevel=foveation;
                    for(int i=0;i<clouds.Length;i++) if(clouds[i]) clouds[i].sharedMaterials=cloudMaterials[i];
                }
            Debug.Log(Prefix+"DONE");
            Destroy(gameObject);
        }

        // Runs after OVRManager's update has chosen the dynamic-resolution viewport for this frame.
        void CapViewport()
        { if(viewportCap<1 && UnityEngine.XR.XRSettings.renderViewportScale>viewportCap) UnityEngine.XR.XRSettings.renderViewportScale=viewportCap; }
        void Aim()
        {
            var camera=Camera.main; if(!target || !camera) return;
            if(compare) {
                // Look straight at the building area, pitch included, like a player standing in front of it.
                var look=Quaternion.FromToRotation(camera.transform.forward,(target.position-camera.transform.position).normalized);
                look.ToAngleAxis(out float angle,out var axis);
                camera.transform.root.RotateAround(camera.transform.position,axis,angle);
                return;
            }
            var to=target.position-camera.transform.position; to.y=0; var forward=camera.transform.forward; forward.y=0;
            camera.transform.root.RotateAround(camera.transform.position,Vector3.up,Vector3.SignedAngle(forward,to,Vector3.up));
        }
        float AimError()
        {
            var camera=Camera.main; if(!target || !camera) return -1;
            return Vector3.Angle(camera.transform.forward,target.position-camera.transform.position);
        }
        static void SetHz(float value) { if(OVRManager.display!=null && value>0 && OVRManager.display.displayFrequency!=value) OVRManager.display.displayFrequency=value; }
        static bool Uses(Renderer renderer,string shader) => renderer.sharedMaterials.Any(m=>m && m.shader && m.shader.name==shader);
        static void Show(Renderer[] renderers,bool value) { foreach(var r in renderers) if(r) r.enabled=value; }
        static void Add(List<float> values,OVRPlugin.PerfMetrics metric) { var value=OVRPlugin.GetPerfMetricsFloat(metric); if(value.HasValue) values.Add(value.Value); }
        static string Mean(List<float> values) => values.Count==0?"n/a":values.Average().ToString("G5",CultureInfo.InvariantCulture);
        static string F(float value) => value.ToString("F2",CultureInfo.InvariantCulture);
        static float Yaw() { var camera=Camera.main; return camera?camera.transform.eulerAngles.y:0; }
        static float Pitch() { var camera=Camera.main; return camera?Mathf.DeltaAngle(0,camera.transform.eulerAngles.x):0; }
    }
}
#endif
