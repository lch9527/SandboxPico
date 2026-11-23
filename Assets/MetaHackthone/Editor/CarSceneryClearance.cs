using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    // Conservative clearance for decoration bounds, including rotating parts.
    // The road width comes from the spline, not a fixed offset or nearest knot.
    public static class CarSceneryClearance
    {
        public struct Lane
        {
            public Matrix4x4 WorldToLane;
            public Vector3 Half;
            public Bounds WorldBounds;
        }
        public static List<Lane> Lanes(SandboxFactoryLevel factory)
        {
            var lanes=new List<Lane>();
            foreach(var track in factory.Tracks) {
                var samples=SandboxTrackBaker.SamplePath(track);float scale=track.transform.lossyScale.x;
                for(int i=1;i<samples.Count;i++) {
                    var a=track.transform.TransformPoint(samples[i-1].Position);var b=track.transform.TransformPoint(samples[i].Position);
                    var horizontal=b-a;horizontal.y=0;if(horizontal.sqrMagnitude<1e-10f)continue;
                    var center=(a+b)*.5f+Vector3.up*1.52f*scale;
                    var half=new Vector3((Mathf.Max(samples[i-1].Width,samples[i].Width)*.5f+.35f)*scale,Mathf.Abs(b.y-a.y)*.5f+1.48f*scale,horizontal.magnitude*.5f+.05f*scale);
                    var matrix=Matrix4x4.TRS(center,Quaternion.LookRotation(horizontal),Vector3.one);
                    var bounds=TransformBounds(new Bounds(Vector3.zero,half*2),matrix);
                    lanes.Add(new Lane{WorldToLane=matrix.inverse,Half=half,WorldBounds=bounds});
                }
            }
            return lanes;
        }
        public static Bounds TransformBounds(Bounds b,Matrix4x4 matrix)
        {
            var result=new Bounds(matrix.MultiplyPoint3x4(b.center),Vector3.zero);
            for(int i=0;i<8;i++)result.Encapsulate(matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
            return result;
        }
        public static Bounds SweptBounds(Renderer renderer)
        {
            var breeze=renderer.GetComponentInParent<CarSceneryBreeze>();
            if(!breeze)return renderer.bounds;
            var b=renderer.bounds;float radius=Vector3.Distance(b.center,breeze.transform.position)+b.extents.magnitude;
            return new Bounds(breeze.transform.position,Vector3.one*radius*2);
        }
        public static bool Overlaps(Bounds bounds,List<Lane> lanes)
        {
            foreach(var lane in lanes) {
                if(!bounds.Intersects(lane.WorldBounds))continue;
                var local=TransformBounds(bounds,lane.WorldToLane);
                if(local.Intersects(new Bounds(Vector3.zero,lane.Half*2)))return true;
            }
            return false;
        }
        public static IEnumerable<Transform> MoveGroups(SandboxFactoryLevel factory)
        {
            var root=factory.Layout.Course.SandboxFrame.Find("Environment/"+CarSceneryAuthoring.GroupName);
            if(!root)yield break;
            foreach(Transform child in root) {
                if(child.name.StartsWith("Freight creek")||child.name.Contains("cloud banks")||child.name=="Soft sky backdrop")continue;
                yield return child;
            }
        }
        public static void Clear(SandboxFactoryLevel factory,StringBuilder report)
        {
            var lanes=Lanes(factory);var groups=MoveGroups(factory).ToArray();
            float step=factory.Tracks.Min(t=>t.transform.lossyScale.x)*.5f;
            foreach(var group in groups) {
                // Anchored, sloping ground must be checked by triangles: its AABB extends above downhill road sections.
                if(QuarryIslandAuthoring.IsIsland(group.name)) {
                    if(group.GetComponentsInChildren<Renderer>().Any(r=>Intrudes(r,lanes)))throw new Exception("Anchored island blocks driving space");
                    continue;
                }
                var boxes=group.GetComponentsInChildren<Renderer>().Select(SweptBounds).ToArray();
                if(!boxes.Any(b=>Overlaps(b,lanes)))continue;
                var origin=group.position;bool found=false;
                for(int ring=1;ring<=160 && !found;ring++)for(int direction=0;direction<32 && !found;direction++) {
                    float angle=direction*Mathf.PI/16;var offset=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*step*ring;
                    bool hit=boxes.Any(b=>{b.center+=offset;return Overlaps(b,lanes);});
                    if(hit)continue;
                    // Keep freight terraces separated; their rocks move with the windmill.
                    if(group.name.StartsWith("Freight terrace")) {
                        var footprint=boxes.Aggregate((a,b)=>{a.Encapsulate(b);return a;});footprint.center+=offset;
                        if(groups.Where(g=>g!=group && g.name.StartsWith("Freight terrace")).Any(g=>g.GetComponentsInChildren<Renderer>().Any(r=>footprint.Intersects(r.bounds))))continue;
                    }
                    group.position=origin+offset;found=true;
                    if(PrefabUtility.IsPartOfPrefabInstance(group))PrefabUtility.RecordPrefabInstancePropertyModifications(group);
                    report.AppendLine($"{factory.Title}: {group.name}, world delta {offset:F4}");
                }
                if(!found)throw new InvalidOperationException("No clear placement found: "+group.name);
            }
            foreach(var group in groups)if(group.GetComponentsInChildren<Renderer>().Any(r=>Intrudes(r,lanes)))throw new Exception("Decoration remains inside road clearance: "+group.name);
        }
        public static bool Intrudes(Renderer renderer,List<Lane> lanes)
        {
            var mesh=renderer.GetComponent<MeshFilter>();
            bool rotating=renderer.GetComponentInParent<CarSceneryBreeze>();
            bool wind=renderer.sharedMaterials.Any(m=>m&&m.shader.name==SceneryWindClock.ShaderName);
            // Static batches can surround a road without occupying it (island rings,
            // separated supplies, bridge sockets). Their empty AABB space is not art.
            return mesh&&!rotating?MeshOverlaps(mesh,lanes,wind):Overlaps(SweptBounds(renderer),lanes);
        }
        [MenuItem("MetaHackthone/Creek Art/Clear scenery from all campaign roads")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play first.");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var setup=EditorSceneManager.GetSceneManagerSetup();bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
            var report=new StringBuilder();
            try {
                for(int n=1;n<=5;n++) {
                    var scene=EditorSceneManager.OpenScene($"Assets/MetaHackthone/Scenes/Level{n}.unity");var factory=SandboxFactoryAuthoring.Find(scene);
                    var before=CarSceneryAuthoring.GameplaySnapshot(factory);Clear(factory,report);
                    if(CarSceneryAuthoring.GameplaySnapshot(factory)!=before)throw new Exception("Road clearance changed gameplay");
                    EditorSceneManager.SaveScene(scene);
                    string path=$"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab";var contents=PrefabUtility.LoadPrefabContents(path);
                    try { factory=contents.GetComponent<SandboxFactoryLevel>();before=CarSceneryAuthoring.GameplaySnapshot(factory);Clear(factory,report);if(CarSceneryAuthoring.GameplaySnapshot(factory)!=before)throw new Exception("Road clearance changed prefab gameplay");PrefabUtility.SaveAsPrefabAsset(contents,path); }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                }
                Directory.CreateDirectory("Logs/SceneryClearance");File.WriteAllText("Logs/SceneryClearance/Moved-props.txt",report.ToString());AssetDatabase.SaveAssets();Debug.Log("ROAD_CLEARANCE_COMPLETE\n"+report);
            } finally { SandboxTrackEditing.Suspended=suspended;if(setup.Any(s=>s.isLoaded))EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
        public static void ApplyBoth() { Apply();LightweightBoundaryAuthoring.Apply(); }
        public static void FinishAudit() { LightweightBoundaryAuthoring.Apply();AuditExisting(); }
        public static bool TriangleOverlaps(Vector3 a,Vector3 b,Vector3 c,Vector3 half)
        {
            if(Mathf.Min(a.x,Mathf.Min(b.x,c.x))>half.x || Mathf.Max(a.x,Mathf.Max(b.x,c.x)) < -half.x)return false;
            if(Mathf.Min(a.y,Mathf.Min(b.y,c.y))>half.y || Mathf.Max(a.y,Mathf.Max(b.y,c.y)) < -half.y)return false;
            if(Mathf.Min(a.z,Mathf.Min(b.z,c.z))>half.z || Mathf.Max(a.z,Mathf.Max(b.z,c.z)) < -half.z)return false;
            bool Separates(Vector3 axis) {
                float x=Vector3.Dot(a,axis),y=Vector3.Dot(b,axis),z=Vector3.Dot(c,axis);
                float r=Vector3.Dot(half,new Vector3(Mathf.Abs(axis.x),Mathf.Abs(axis.y),Mathf.Abs(axis.z)));
                return Mathf.Min(x,Mathf.Min(y,z))>r || Mathf.Max(x,Mathf.Max(y,z)) < -r;
            }
            bool Edge(Vector3 e)=>Separates(Vector3.Cross(e,Vector3.right))||Separates(Vector3.Cross(e,Vector3.up))||Separates(Vector3.Cross(e,Vector3.forward));
            return !Separates(Vector3.Cross(b-a,c-a)) && !Edge(b-a) && !Edge(c-b) && !Edge(a-c);
        }
        public static bool MeshOverlaps(MeshFilter filter,List<Lane> lanes,bool includeWind=false)
        {
            var renderer=filter.GetComponent<Renderer>();var vertices=filter.sharedMesh.vertices;var triangles=filter.sharedMesh.triangles;
            var wind=new List<Vector4>();var rope=new List<Vector4>();
            if(includeWind){filter.sharedMesh.GetUVs(3,wind);filter.sharedMesh.GetUVs(4,rope);}
            foreach(var lane in lanes) {
                if(!renderer.bounds.Intersects(lane.WorldBounds))continue;
                var matrix=lane.WorldToLane*filter.transform.localToWorldMatrix;
                for(int i=0;i<triangles.Length;i+=3) {
                    var half=lane.Half;
                    if(wind.Count==vertices.Length) {
                        float dx=0,dz=0;
                        for(int j=0;j<3;j++){int v=triangles[i+j];float sway=Mathf.Abs((vertices[v].y-wind[v].x)*wind[v].y);dx=Mathf.Max(dx,sway);dz=Mathf.Max(dz,sway*.35f);if(rope.Count==vertices.Length&&rope[v].x>.5f)dz=Mathf.Max(dz,Mathf.Abs(rope[v].w));}
                        var x=matrix.MultiplyVector(Vector3.right*dx);var z=matrix.MultiplyVector(Vector3.forward*dz);
                        half+=new Vector3(Mathf.Abs(x.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(z.z));
                    }
                    if(TriangleOverlaps(matrix.MultiplyPoint3x4(vertices[triangles[i]]),matrix.MultiplyPoint3x4(vertices[triangles[i+1]]),matrix.MultiplyPoint3x4(vertices[triangles[i+2]]),half))return true;
                }
            }
            return false;
        }
        public static void AuditExisting()
        {
            var report=new StringBuilder();
            for(int n=1;n<=5;n++) {
                var contents=PrefabUtility.LoadPrefabContents($"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab");
                try {
                    var factory=contents.GetComponent<SandboxFactoryLevel>();var lanes=Lanes(factory);
                    var environment=factory.Layout.Course.SandboxFrame.Find("Environment");var ours=environment.Find(CarSceneryAuthoring.GroupName);
                    foreach(var filter in environment.GetComponentsInChildren<MeshFilter>()) {
                        var renderer=filter.GetComponent<Renderer>();if(!renderer || !renderer.enabled || filter.transform.IsChildOf(ours))continue;
                        if(!lanes.Any(l=>l.WorldBounds.Intersects(renderer.bounds)))continue;
                        if(MeshOverlaps(filter,lanes))report.AppendLine($"Level {n}: "+AnimationUtility.CalculateTransformPath(filter.transform,environment));
                    }
                }finally{PrefabUtility.UnloadPrefabContents(contents);}
            }
            Directory.CreateDirectory("Logs/SceneryClearance");File.WriteAllText("Logs/SceneryClearance/Existing-geometry-overlaps.txt",report.ToString());Debug.Log("EXISTING_SCENERY_AUDIT\n"+report);
        }
    }
}
