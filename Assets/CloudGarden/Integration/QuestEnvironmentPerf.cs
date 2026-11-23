#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PhysicalAssembly;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace CloudGarden.Integration
{
    // Opt-in diagnostic only. Gameplay starts, retries and level changes remain manual.
    public sealed class QuestEnvironmentPerf : MonoBehaviour
    {
        const string Prefix="QUEST_ENV_PERF ";
        static readonly string[] Cards={"WORKBENCH BUILD","HANDFLEET","Narrow Gap","Shadow","Car Workshop","Cloud Garden"};
        static QuestEnvironmentPerf current;
        StreamWriter csv,events; string folder; bool finished,loading;
        MainMenuBootstrap menu; DownhillController level; int number,run; float readyAt,chunkAt;
        string state; readonly List<float> times=new(180); int focus,present,dropped;
        readonly double[] sums=new double[7]; readonly int[] counts=new int[7];
        readonly List<Counter> counters=new();
        sealed class Counter { public string Name;public ProfilerRecorder Recorder;public double Sum;public int Count; }
        public bool Finished=>finished;
        public int SelectedLevel=>number;
        public string CaptureFolder=>folder;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic()=>current=null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity=player.GetStatic<AndroidJavaObject>("currentActivity");using var intent=activity.Call<AndroidJavaObject>("getIntent");
            if(!intent.Call<bool>("getBooleanExtra","metahackthone.environment_perf",false))return;
            intent.Call("removeExtra","metahackthone.environment_perf");BeginManual();
#endif
        }
        public static QuestEnvironmentPerf BeginManual()
        {
            if(current)return current;
            var go=new GameObject("Manual environment performance capture");DontDestroyOnLoad(go);
            current=go.AddComponent<QuestEnvironmentPerf>();return current;
        }
        void Start()
        {
            folder=Path.Combine(Application.persistentDataPath,"EnvironmentPerf",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);
            csv=new StreamWriter(Path.Combine(folder,"samples.csv"));events=new StreamWriter(Path.Combine(folder,"events.csv"));
            events.WriteLine("utc,event,level,run,state,realtime_seconds");
            AddCounter("Main Thread",ProfilerCategory.Internal);AddCounter("Render Thread",ProfilerCategory.Internal);
            AddCounter("Draw Calls Count",ProfilerCategory.Render);AddCounter("SetPass Calls Count",ProfilerCategory.Render);AddCounter("Triangles Count",ProfilerCategory.Render);
            AddCounter("GC Allocated In Frame",ProfilerCategory.Memory);AddCounter("Total Used Memory",ProfilerCategory.Memory);
            csv.WriteLine("utc,level,run,state,warmup,level_seconds,frames,fps,p50_frame_ms,p95_frame_ms,max_frame_ms,focused_fraction,present_fraction,app_gpu_raw,app_cpu_raw,gpu_util_raw,cpu_average_util_raw,cpu_worst_util_raw,viewport,eye_scale,display_hz,asw_mode,compositor_dropped,eye_width,eye_height,main_thread_ns,render_thread_ns,draw_calls,setpass_calls,triangles,gc_bytes,used_memory_bytes");csv.Flush();
            File.WriteAllText(Path.Combine(folder,"metadata.txt"),"manual-playtest-v2\nUTC="+DateTime.UtcNow.ToString("O")+"\nUnity="+Application.unityVersion+"\ndevice="+SystemInfo.deviceModel+"\nGPU="+SystemInfo.graphicsDeviceName+"\nAPI="+SystemInfo.graphicsDeviceType+"\ndevelopment="+Debug.isDebugBuild+"\nOVR *_raw columns retain native API units; use Perfetto GPU/render-stage timings to corroborate bottlenecks. Main/Render Thread include waits, not just active CPU work. Empty fields mean unavailable. Warmup: first 8 seconds after ready. FPS denominator uses realtime; exclude unfocused/unworn samples and load/warmup from conclusions. Counters sampled each frame, CSV flushed each second.\n"+string.Join("\n",counters.Select(c=>c.Name+": "+c.Recorder.Valid)));
            SceneManager.sceneLoaded+=SceneLoaded;WorkshopButton.Activated+=ButtonActivated;
            Event("BEGIN");DesignModeSession.SetEnabled(false);
            if(SceneManager.GetActiveScene().name!=GardenNavigation.MenuScene)SceneManager.LoadScene(GardenNavigation.MenuScene);
            else StartCoroutine(ConfigureScene());
        }
        void AddCounter(string name,ProfilerCategory category)
        {
            var c=new Counter{Name=name};try{c.Recorder=ProfilerRecorder.StartNew(category,name,1);}catch(Exception e){Debug.LogWarning(Prefix+name+": "+e.Message);}counters.Add(c);
        }
        void SceneLoaded(Scene scene,LoadSceneMode mode)=>StartCoroutine(ConfigureScene());
        IEnumerator ConfigureScene()
        {
            yield return null;yield return null;
            menu=FindFirstObjectByType<MainMenuBootstrap>();
            if(menu){ConfigureMenu();loading=false;yield break;}
            if(finished)yield break;
            var owner=FindFirstObjectByType<WorkshopBootstrap>();
            if(!owner||!owner.Downhill||!owner.Downhill.FactoryLevel){Event("LOAD_FAILED");loading=false;yield break;}
            level=owner.Downhill;
            if(SandboxCourseCatalog.Number(level.FactoryLevel.Layout.CourseId)!=number){Event("WRONG_LEVEL");ReturnToMenu();yield break;}
            if(number==5){var car=Resources.Load<SandboxTestCarAsset>("EnvironmentPerfLevel5");if(!car||!level.TryLoadFactoryCar(car)){Event("CAR_FAILED");ReturnToMenu();yield break;}}
            if(!level.Core){Event("CAR_MISSING");ReturnToMenu();yield break;}
            level.Presentation.ExitButton.Pressed=ReturnToMenu;
            readyAt=Time.realtimeSinceStartup;run=0;state=level.State.ToString();ClearChunk();loading=false;Event("READY");
        }
        void ConfigureMenu()
        {
            var buttons=menu.Panel.GetComponentsInChildren<WorkshopButton>(true);
            for(int i=0;i<Cards.Length;i++){
                var button=buttons.Single(b=>b.name==Cards[i]);int n=i+1;
                button.Pressed=i<5?()=>SelectLevel(n):FinishCapture;
                var labels=button.GetComponentsInChildren<TextMesh>();
                labels[0].text=i<5?"LEVEL "+n:"FINISH TEST";
                labels[1].text=i<5?"Look around, then press START.":"Save this playtest's measurements.";
            }
            foreach(var label in menu.Panel.GetComponentsInChildren<TextMesh>())if(label.text=="Choose your mode")label.text="PERFORMANCE PLAYTEST";
            menu.Show();menu.SetStatus(finished?"Saved. You can remove the headset.":"Select 1-5. QUIT returns here. FINISH TEST saves.");Event("MENU");
        }
        public void SelectLevel(int n)
        {
            if(finished||loading||n<1||n>5)return;
            FlushChunk();number=n;run=0;state=null;level=null;menu=null;loading=true;Event("LOAD");
            DesignModeSession.SetEnabled(true);DesignModeSession.SelectLevel(DesignLevelCatalog.Levels[n-1]);
            StartCoroutine(LoadNextFrame(GardenNavigation.DownhillScene));
        }
        public void ReturnToMenu()
        {
            if(loading)return;FlushChunk();Event("QUIT");level=null;loading=true;DesignModeSession.SetEnabled(false);
            StartCoroutine(LoadNextFrame(GardenNavigation.MenuScene));
        }
        IEnumerator LoadNextFrame(string scene){yield return null;SceneManager.LoadScene(scene);}
        void ButtonActivated(WorkshopButton b){if(!finished)Event("BUTTON_"+b.Command.Replace(","," "));}
        void Update()
        {
            if(menu&&menu.Visible&&!menu.PlacementActive)menu.SetStatus(finished?"Saved. You can remove the headset.":"Select 1-5. QUIT returns here. FINISH TEST saves.");
            if(finished||loading||!level||csv==null)return;
            var next=level.State.ToString();if(next!=state){FlushChunk();state=next;if(level.State==DownhillState.Running)run++;Event("STATE");ClearChunk();}
            times.Add(Time.unscaledDeltaTime*1000);
#if UNITY_ANDROID && !UNITY_EDITOR
            if(OVRManager.hasVrFocus)focus++;if(OVRPlugin.userPresent)present++;
            Add(0,OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_GpuTime_Float));Add(1,OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_CpuTime_Float));
            Add(2,OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.System_GpuUtilPercentage_Float));
            Add(3,OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.System_CpuUtilAveragePercentage_Float));Add(4,OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.System_CpuUtilWorstPercentage_Float));
#else
            focus++;present++;
#endif
            Add(5,XRSettings.renderViewportScale);Add(6,XRSettings.eyeTextureResolutionScale);
            foreach(var c in counters)if(c.Recorder.Valid&&c.Recorder.Count>0){c.Sum+=c.Recorder.LastValue;c.Count++;}
            if(Time.realtimeSinceStartup-chunkAt>=1)FlushChunk();
        }
        void Add(int i,float? v){if(v.HasValue){sums[i]+=v.Value;counts[i]++;}}
        static string F(double v)=>v.ToString("F4",CultureInfo.InvariantCulture);
        static int Dropped(){
#if UNITY_ANDROID && !UNITY_EDITOR
            return OVRPlugin.GetPerfMetricsInt(OVRPlugin.PerfMetrics.Compositor_DroppedFrameCount_Int)??0;
#else
            return 0;
#endif
        }
        void FlushChunk()
        {
            if(times.Count==0||csv==null)return;
            float now=Time.realtimeSinceStartup;times.Sort();int count=times.Count;float hz=0;int asw=-1;
#if UNITY_ANDROID && !UNITY_EDITOR
            hz=OVRManager.display!=null?OVRManager.display.displayFrequency:0;asw=OVRPlugin.GetPerfMetricsInt(OVRPlugin.PerfMetrics.Compositor_SpaceWarp_Mode_Int)??-1;
#endif
            var fields=new List<string>{DateTime.UtcNow.ToString("O"),number.ToString(),run.ToString(),state,chunkAt-readyAt<8?"1":"0",F(now-readyAt),count.ToString(),F(count/Math.Max(.0001,now-chunkAt)),F(times[count/2]),F(times[Mathf.CeilToInt(count*.95f)-1]),F(times[count-1]),F((double)focus/count),F((double)present/count)};
            for(int i=0;i<sums.Length;i++)fields.Add(counts[i]>0?F(sums[i]/counts[i]):"");
            fields.AddRange(new[]{F(hz),asw.ToString(),Math.Max(0,Dropped()-dropped).ToString(),XRSettings.eyeTextureWidth.ToString(),XRSettings.eyeTextureHeight.ToString()});
            foreach(var c in counters)fields.Add(c.Count>0?F(c.Sum/c.Count):"");
            csv.WriteLine(string.Join(",",fields));csv.Flush();ClearChunk();
        }
        void ClearChunk(){chunkAt=Time.realtimeSinceStartup;times.Clear();focus=present=0;dropped=Dropped();Array.Clear(sums,0,sums.Length);Array.Clear(counts,0,counts.Length);foreach(var c in counters){c.Sum=0;c.Count=0;}}
        void Event(string name){events?.WriteLine(string.Join(",",DateTime.UtcNow.ToString("O"),name,number,run,state,F(Time.realtimeSinceStartup)));events?.Flush();Debug.Log(Prefix+name+" level="+number+" run="+run+" state="+state);}
        public void FinishCapture(){if(finished)return;FlushChunk();Event("DONE");finished=true;Close();Debug.Log(Prefix+"SAVED path="+folder);}
        void OnApplicationPause(bool paused){if(!finished){FlushChunk();Event(paused?"PAUSE":"RESUME");ClearChunk();}}
        void Close(){csv?.Dispose();csv=null;events?.Dispose();events=null;foreach(var c in counters)c.Recorder.Dispose();counters.Clear();}
        void OnDestroy(){SceneManager.sceneLoaded-=SceneLoaded;WorkshopButton.Activated-=ButtonActivated;FlushChunk();Close();if(current==this)current=null;}
    }
}
#endif
