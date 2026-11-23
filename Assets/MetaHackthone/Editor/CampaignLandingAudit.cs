using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class CampaignLandingAudit
    {
        public static string Check(SandboxFactoryLevel f)=>Check(f,out _);
        public static string Check(SandboxFactoryLevel f,out float excessHeight)
        {
            excessHeight=0;
            var env=f.Layout.Course.SandboxFrame.Find("Environment");var probes=new List<(string name,Vector3 point)>();
            var track=f.Tracks[0];var samples=track.Samples;float width=track.DefaultWidth*track.transform.lossyScale.x;
            for(int side=0;side<2;side++) {
                int i=side==0?0:samples.Length-1,j=side==0?Mathf.Min(8,samples.Length-1):Mathf.Max(0,samples.Length-9);
                Vector3 p=track.transform.TransformPoint(samples[i]),dir=p-track.transform.TransformPoint(samples[j]);
                float slope=dir.y/Mathf.Max(new Vector2(dir.x,dir.z).magnitude,.001f);dir.y=0;dir.Normalize();var right=Vector3.Cross(Vector3.up,dir);
                foreach(float across in new[]{-.42f,0,.42f})foreach(float back in new[]{0f,.45f})
                    probes.Add(($"{(side==0?"Start":"Finish")} width {across:F2} back {back:F2}",p+right*width*across+dir*width*back+Vector3.up*Mathf.Clamp(slope*width*back,-.05f,.05f)));
            }
            var heights=Enumerable.Repeat(float.NegativeInfinity,probes.Count).ToArray();var names=new string[probes.Count];
            foreach(var filter in env.GetComponentsInChildren<MeshFilter>()) {
                var renderer=filter.GetComponent<Renderer>();if(!renderer || !renderer.enabled)continue;
                // Ground only: clouds, water, props, trees and the playable deck cannot stand in for an island.
                if(!renderer.sharedMaterials.All(m=>m && new[]{"limestone","moss","stone","ground","grass"}.Any(s=>m.name.ToLowerInvariant().Contains(s))))continue;
                var collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;
                try {for(int n=0;n<probes.Count;n++) {
                    var p=probes[n].point;
                    if(collider.Raycast(new Ray(p+Vector3.up*30,Vector3.down),out var hit,60) && hit.point.y>heights[n]){heights[n]=hit.point.y;names[n]=filter.name;}
                }}finally{Object.DestroyImmediate(collider);}
            }
            var result=new StringBuilder();for(int n=0;n<probes.Count;n++) {
                float gap=probes[n].point.y-heights[n];
                if(float.IsFinite(gap))excessHeight=Mathf.Max(excessHeight,-gap);
                float riseAllowance=probes[n].name.EndsWith("0.45")?.10f:.015f;
                if(!float.IsFinite(gap) || gap < -riseAllowance || gap>.15f)result.AppendLine($"{probes[n].name}: gap={gap:F3}m; ground={names[n]??"none"}");
            }
            return result.ToString();
        }
        public static void Audit()
        {
            Directory.CreateDirectory("Logs/CampaignLandings");var report=new StringBuilder();
            for(int n=1;n<=5;n++) {
                var go=PrefabUtility.LoadPrefabContents($"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab");
                try {var f=go.GetComponent<SandboxFactoryLevel>();string failures=Check(f);report.AppendLine($"Level {n}: {(failures.Length==0?"SUPPORTED":"NEEDS REPAIR")}");report.Append(failures);}
                finally{PrefabUtility.UnloadPrefabContents(go);}
            }
            File.WriteAllText("Logs/CampaignLandings/Audit.txt",report.ToString());Debug.Log(report.ToString());
        }
    }
}
