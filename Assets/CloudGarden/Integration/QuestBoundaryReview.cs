#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhysicalAssembly;
using UnityEngine;

namespace CloudGarden.Integration
{
    // Opt-in diagnostic APK only. The legacy resource is generated for the build
    // and removed afterwards, so ordinary game builds never include it.
    public sealed class QuestBoundaryReview : MonoBehaviour
    {
        const string Prefix="BOUNDARY_REVIEW ";
        Transform lightweight,legacy,target;
        int variant=-1;
        bool savedActive;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            using var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity=player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent=activity.Call<AndroidJavaObject>("getIntent");
            if(!intent.Call<bool>("getBooleanExtra","metahackthone.boundary_review",false))return;
            intent.Call("removeExtra","metahackthone.boundary_review");
            var go=new GameObject("Boundary performance review");DontDestroyOnLoad(go);go.AddComponent<QuestBoundaryReview>();
        }
        IEnumerator Start()
        {
            float deadline=Time.realtimeSinceStartup+60;
            while(!GardenNavigation.Current && Time.realtimeSinceStartup<deadline)yield return null;
            if(!GardenNavigation.Current){Debug.LogError(Prefix+"FAIL navigation unavailable");yield break;}
            yield return new WaitForSecondsRealtime(3);
            DesignModeSession.SetEnabled(false);GardenNavigation.Current.EnterSandbox();
            deadline=Time.realtimeSinceStartup+90;
            while((!GardenNavigation.Current.DownhillWorkshop || GardenNavigation.Current.IsTransitioning) && Time.realtimeSinceStartup<deadline)yield return null;
            yield return new WaitForSecondsRealtime(8);
            var workshop=GardenNavigation.Current.DownhillWorkshop;
            if(!workshop || !workshop.Downhill.Course.BuildArea){Debug.LogError(Prefix+"FAIL build area unavailable");yield break;}
            var volume=workshop.Downhill.Course.BuildArea.GetComponent<SandboxBuildVolume>();
            var asset=Resources.Load<GameObject>("BoundaryReviewLegacy");
            if(!volume || !volume.Artwork || !asset){Debug.LogError(Prefix+"FAIL diagnostic resource missing");yield break;}
            lightweight=volume.Artwork;savedActive=lightweight.gameObject.activeSelf;target=volume.transform;
            legacy=Instantiate(asset,volume.transform,false).transform;legacy.localScale=workshop.Downhill.Course.BuildArea.Size;
            Debug.Log(Prefix+"BEGIN old_triangles="+Triangles(legacy)+" new_triangles="+Triangles(lightweight)+" area="+legacy.localScale);
            try {
                string[] names={"legacy_glass","lightweight_edges","boundary_off","lightweight_edges","legacy_glass","boundary_off"};
                foreach(var name in names) {
                    variant=name=="legacy_glass"?0:name=="lightweight_edges"?1:2;
                    Aim();Apply();yield return new WaitForSecondsRealtime(3);
                    var frames=new List<float>();var gpu=new List<float>();var cpu=new List<float>();var viewport=new List<float>();
                    var camera=Camera.main;var rotation=camera?camera.transform.rotation:Quaternion.identity;float drift=0;
                    float start=Time.realtimeSinceStartup,end=start+5;
                    while(Time.realtimeSinceStartup<end) {
                        frames.Add(Time.unscaledDeltaTime*1000);Add(gpu,OVRPlugin.PerfMetrics.App_GpuTime_Float);Add(cpu,OVRPlugin.PerfMetrics.App_CpuTime_Float);
                        viewport.Add(UnityEngine.XR.XRSettings.renderViewportScale);
                        if(camera)drift=Mathf.Max(drift,Quaternion.Angle(rotation,camera.transform.rotation));
                        yield return null;
                    }
                    frames.Sort();
                    Debug.Log(Prefix+"RESULT variant="+name+" fps="+F(frames.Count/(Time.realtimeSinceStartup-start))+" median_ms="+F(frames[frames.Count/2])+" p95_ms="+F(frames[(int)((frames.Count-1)*.95f)])+" gpu_ms="+Mean(gpu)+" cpu_ms="+Mean(cpu)+" viewport="+Mean(viewport)+" head_drift_degrees="+F(drift)+" eye="+UnityEngine.XR.XRSettings.eyeTextureWidth+"x"+UnityEngine.XR.XRSettings.eyeTextureHeight);
                }
                Debug.Log(Prefix+"DONE");
            } finally { Restore(); }
            Destroy(gameObject);
        }
        void LateUpdate()=>Apply();
        void Apply(){if(variant<0)return;if(lightweight)lightweight.gameObject.SetActive(variant==1);if(legacy)legacy.gameObject.SetActive(variant==0);}
        void Restore(){variant=-1;if(lightweight)lightweight.gameObject.SetActive(savedActive);if(legacy)Destroy(legacy.gameObject);}
        void OnDestroy()=>Restore();
        void Aim(){var camera=Camera.main;if(!camera || !target)return;var delta=Quaternion.FromToRotation(camera.transform.forward,(target.position-camera.transform.position).normalized);delta.ToAngleAxis(out float angle,out var axis);camera.transform.root.RotateAround(camera.transform.position,axis,angle);}
        static int Triangles(Transform root)=>root.GetComponentsInChildren<MeshFilter>(true).Sum(m=>Enumerable.Range(0,m.sharedMesh.subMeshCount).Sum(s=>(int)m.sharedMesh.GetIndexCount(s)/3));
        static void Add(List<float> values,OVRPlugin.PerfMetrics metric){var value=OVRPlugin.GetPerfMetricsFloat(metric);if(value.HasValue)values.Add(value.Value);}
        static string Mean(List<float> values)=>values.Count==0?"unavailable":F(values.Average());
        static string F(float value)=>value.ToString("F3",CultureInfo.InvariantCulture);
    }
}
#endif
