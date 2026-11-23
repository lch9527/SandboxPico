using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
using static PhysicalAssembly.Editor.EnvironmentPolishAuthoring;

namespace PhysicalAssembly.Editor
{
    public static class GroundedSceneryRepair
    {
        static bool Visible(Transform t){while(t){if(!t.gameObject.activeSelf)return false;t=t.parent;}return true;}
        public static void Prepare(SandboxFactoryLevel factory,string key)
        {
            if(factory.Layout.CourseId==SandboxCourseId.BeautifulCorner)RepairCompressedTerrain(factory,key);
            FlattenWater(factory,key);
            GroundPlants(factory,key);
        }
        static void RepairCompressedTerrain(SandboxFactoryLevel factory,string key)
        {
            var creek=factory.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Beautiful Corner art - editable copy");
            float side=Mathf.Sign(creek.InverseTransformPoint(factory.Finish.transform.position).x);
            int id=0;
            foreach(var f in creek.GetComponentsInChildren<MeshFilter>().Where(f=>f.transform.parent.name=="Stratified rock and soil")) {
                var match=Regex.Match(AssetDatabase.GetAssetPath(f.sharedMesh),@"Level1 lowered terrace ([0-9a-f]{32})\.asset$");
                if(!match.Success)continue;
                var original=AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value));
                if(!original || original.vertexCount!=f.sharedMesh.vertexCount)throw new InvalidOperationException("Cannot identify the original terrace geometry.");
                var before=original.vertices;var after=f.sharedMesh.vertices;
                var toCreek=creek.worldToLocalMatrix*f.transform.localToWorldMatrix;var fromCreek=toCreek.inverse;
                float drop=0;
                for(int i=0;i<before.Length;i++){var p=toCreek.MultiplyPoint3x4(before[i]);if(p.x*side>.65f && p.y>.42f)drop=Mathf.Max(drop,p.y-toCreek.MultiplyPoint3x4(after[i]).y);}
                // Keep the riverbed fixed and compress the cliff monotonically. The old
                // smoothstep subtraction reversed vertex order through the lower rocks.
                const float floor=.17f, oldShelf=.606f;
                float ratio=Mathf.Max(.15f,(oldShelf-floor-drop)/(oldShelf-floor));
                for(int i=0;i<before.Length;i++) {
                    var p=toCreek.MultiplyPoint3x4(before[i]);float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.25f,.65f,p.x*side));
                    float compressed=p.y<=floor?p.y:p.y<=oldShelf?floor+(p.y-floor)*ratio:p.y-drop;
                    p.y=Mathf.Lerp(p.y,compressed,weight);before[i]=fromCreek.MultiplyPoint3x4(p);
                }
                var mesh=Object.Instantiate(original);mesh.name="Monotonic closed terrace";mesh.vertices=before;mesh.RecalculateNormals();mesh.RecalculateBounds();
                f.sharedMesh=Save(mesh,key+"-ClosedTerrace"+id++);
            }
        }
        static void FlattenWater(SandboxFactoryLevel factory,string key)
        {
            int id=0;
            foreach(var f in factory.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="Living river surface")) {
                var source=f.sharedMesh;var positions=source.vertices;float height=positions.Average(p=>p.y);
                var vertices=new List<Vector3>();var indices=new List<int>();var map=new Dictionary<Vector2Int,int>();var remap=new int[positions.Length];
                for(int i=0;i<positions.Length;i++) {
                    var p=positions[i];var k=new Vector2Int(Mathf.RoundToInt(p.x*100000),Mathf.RoundToInt(p.z*100000));
                    if(!map.TryGetValue(k,out int j)){j=vertices.Count;map[k]=j;vertices.Add(new Vector3(k.x*.00001f,height,k.y*.00001f));}remap[i]=j;
                }
                var triangles=source.triangles;var seen=new HashSet<(int,int,int)>();
                for(int i=0;i<triangles.Length;i+=3) {
                    int a=remap[triangles[i]],b=remap[triangles[i+1]],c=remap[triangles[i+2]];
                    float area=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).y;
                    if(Mathf.Abs(area)<1e-10f)continue;if(area<0)(b,c)=(c,b);
                    var sorted=new[]{a,b,c}.OrderBy(v=>v).ToArray();if(!seen.Add((sorted[0],sorted[1],sorted[2])))continue;
                    indices.AddRange(new[]{a,b,c});
                }
                var mesh=new Mesh{name="Continuous level river",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                f.sharedMesh=Save(mesh,key+"-ContinuousRiver"+id++);
            }
        }
        static void GroundPlants(SandboxFactoryLevel factory,string key)
        {
            var ground=new ContactPolishAuthoring.Surface(ContactPolishAuthoring.VisibleGround(factory.gameObject));
            var lanes=CarSceneryClearance.Lanes(factory);int moved=0,removed=0;
            foreach(var dressing in factory.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="10 Botanical dressing").ToArray()) {
                var source=dressing.Cast<Transform>().FirstOrDefault(t=>t.name.StartsWith("Editable placement"));if(!source)continue;
                foreach(string groupName in new[]{"Layered canopy","Moss ivy ferns and meadow"}) {
                    var group=source.Find(groupName);if(!group)continue;
                    foreach(Transform plant in group) {
                        var filters=plant.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.GetComponent<Renderer>() && m.GetComponent<Renderer>().enabled).ToArray();if(filters.Length==0)continue;
                        var p=plant.position;bool tree=groupName=="Layered canopy";
                        float ceiling=tree?float.PositiveInfinity:p.y+.08f;
                        bool supported=ground.Height(p,out float y,ceiling);
                        if(!supported) {
                            // Rim ivy may hang just outside the cap. Find its nearby bank,
                            // without treating the inactive, pre-edit terrain as support.
                            for(int ring=1;ring<=6&&!supported;ring++)for(int i=0;i<16&&!supported;i++) {
                                float angle=i*Mathf.PI/8;var q=p+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*.05f*ring;
                                supported=ground.Height(q,out y,ceiling);
                            }
                        }
                        if(!supported || (tree?Mathf.Abs(p.y-y)<.012f:p.y-y<.012f) || Mathf.Abs(p.y-y)>2)continue;
                        plant.position=new Vector3(p.x,y+.002f,p.z);moved++;
                        if(filters.Any(m=>CarSceneryClearance.MeshOverlaps(m,lanes))) {
                            foreach(var m in filters)m.GetComponent<Renderer>().enabled=false;removed++;
                        }
                    }
                    // Canopies are rebuilt by Trees after this pass, retaining their wind pivots.
                    if(groupName=="Moss ivy ferns and meadow") {
                        var target=dressing.Find("Render batches/"+groupName);
                        if(target)Combine(group,target,key+"-GroundedPlants");
                    }
                }
            }
            Debug.Log("SCENERY_ROOTS "+key+" grounded="+moved+" lane_conflicts_removed="+removed);
        }
        public static void AnchorWaterfalls(GameObject root,string key)
        {
            var ground=new ContactPolishAuthoring.Surface(ContactPolishAuthoring.VisibleGround(root));
            var stoneFilters=root.GetComponentsInChildren<MeshFilter>().Where(m=>m.GetComponent<Renderer>()&&m.GetComponent<Renderer>().enabled&&(m.transform.parent.name=="Stratified rock and soil"||m.transform.parent.name=="Story props"||m.name.EndsWith("ground bedrock"))).ToArray();
            var solid=new ContactPolishAuthoring.Surface(stoneFilters);
            var factory=root.GetComponent<SandboxFactoryLevel>();
            var eye=factory.Layout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance).position;
            var lanes=CarSceneryClearance.Lanes(factory);
            var visibilityMeshes=stoneFilters.Concat(factory.Tracks.SelectMany(track=>track.GetComponentsInChildren<MeshFilter>()).Where(m=>m.GetComponent<Renderer>()&&m.GetComponent<Renderer>().enabled)).Distinct().ToArray();
            var view=new ContactPolishAuthoring.Surface(visibilityMeshes);
            var rivers=root.GetComponentsInChildren<MeshFilter>(true).Where(m=>Visible(m.transform) && m.GetComponent<Renderer>() && m.GetComponent<Renderer>().enabled && m.GetComponent<Renderer>().sharedMaterial.shader.name=="Creek Diorama/Living Water" && !m.name.Contains("spring")).ToArray();
            var water=new ContactPolishAuthoring.Surface(rivers);
            foreach(var f in root.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.name.StartsWith("Flowing curtain")).ToArray()) {
                string number=f.name.Split(' ').Last();
                var original=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/CreekDiorama/Meshes/Flowing_curtain_"+number+".asset");if(!original)continue;
                var t=f.transform;t.localPosition=Vector3.zero;t.localRotation=Quaternion.identity;t.localScale=Vector3.one;
                var verts=original.vertices;float high=verts.Max(p=>p.y),low=verts.Min(p=>p.y);
                var lip=verts.Where(p=>p.y>high-.006f).Aggregate(Vector3.zero,(a,b)=>a+b)/verts.Count(p=>p.y>high-.006f);
                var foot=verts.Where(p=>p.y<low+.006f).Aggregate(Vector3.zero,(a,b)=>a+b)/verts.Count(p=>p.y<low+.006f);
                var worldLip=t.TransformPoint(lip);var worldFoot=t.TransformPoint(foot);
                var oldFlow=Vector3.ProjectOnPlane(worldFoot-worldLip,Vector3.up).normalized;
                float run=Vector3.ProjectOnPlane(worldFoot-worldLip,Vector3.up).magnitude;
                var oldRight=Vector3.Cross(Vector3.up,oldFlow);
                float halfWidth=verts.Where(p=>p.y>high-.006f).Max(p=>Mathf.Abs(Vector3.Dot(t.TransformPoint(p)-worldLip,oldRight)));
                Vector3 mouth=default,outward=default;float best=float.PositiveInfinity,waterY=0;
                // Choose a supported bank with a clear drop into the same water sheet.
                for(int ring=0;ring<=26;ring++)for(int k=0;k<(ring==0?1:24);k++) {
                    float angle=k*Mathf.PI/12;var candidate=worldLip+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*ring*.075f;
                    if(!ground.Height(candidate,out float y))continue;candidate.y=y;
                    for(int d=0;d<16;d++) {
                        var direction=Quaternion.AngleAxis(d*22.5f,Vector3.up)*Vector3.forward;
                        var edge=candidate+direction*.14f;
                        var downstream=edge+direction*run;var upstream=candidate-direction*.14f;
                        if(!water.Height(downstream,out float wy) || y-wy<.28f)continue;
                        if(ground.Height(downstream,out float gy) && gy>y-.16f)continue;
                        if(!ground.Height(upstream,out float uy) || Mathf.Abs(uy-y)>.08f)continue;
                        if(solid.Height(upstream,out float obstruction)&&obstruction>y+.08f)continue;
                        var springBounds=new Bounds(edge-direction*.38f+Vector3.up*.035f,new Vector3(Mathf.Max(halfWidth*3,.85f),.3f,.95f));
                        if(CarSceneryClearance.Overlaps(springBounds,lanes))continue;
                        var towardEye=Vector3.ProjectOnPlane(eye-edge,Vector3.up).normalized;
                        if(Vector3.Dot(direction,towardEye)<.1f)continue;
                        float score=(candidate-worldLip).sqrMagnitude+.10f*(1-Vector3.Dot(direction,oldFlow));
                        if(score>=best)continue;
                        bool blocked=false;var side=Vector3.Cross(Vector3.up,direction);
                        for(int step=0;step<=5&&!blocked;step++)foreach(float across in new[]{-.8f,0,.8f}) {
                            float fraction=step/5f;var sample=edge+direction*run*fraction+side*halfWidth*across;
                            sample.y=Mathf.Lerp(y+.015f,wy+.01f,fraction);
                            if(solid.Height(sample,out float rock)&&rock>sample.y-.005f){blocked=true;break;}
                        }
                        if(blocked)continue;
                        int visibleSamples=0;
                        for(int step=0;step<=4;step++) {
                            float fraction=step/4f;var sample=edge+direction*run*fraction;sample.y=Mathf.Lerp(y+.025f,wy+.035f,fraction);
                            if(!view.Occludes(eye,sample))visibleSamples++;
                        }
                        // A road can pass over the spring, but most of the falling
                        // sheet must remain visible rather than only its splash.
                        if(visibleSamples<3)continue;
                        best=score;mouth=edge+Vector3.up*.015f;outward=direction;waterY=wy;
                    }
                }
                if(float.IsPositiveInfinity(best))throw new InvalidOperationException("No supported waterfall bank: "+key+" "+f.name);
                var turn=Quaternion.FromToRotation(oldFlow,outward);
                var newVertices=verts.Select(p=>{
                    var world=t.TransformPoint(p);var delta=turn*Vector3.ProjectOnPlane(world-worldLip,Vector3.up);
                    float fraction=Mathf.Clamp01((p.y-low)/(high-low));
                    var next=mouth+delta;next.y=Mathf.Lerp(waterY+.005f,mouth.y,fraction);return t.InverseTransformPoint(next);
                }).ToArray();
                var mesh=Object.Instantiate(original);mesh.name="Bank anchored waterfall";mesh.vertices=newVertices;mesh.RecalculateNormals();mesh.RecalculateBounds();f.sharedMesh=Save(mesh,key+"-GroundedFall"+number);
                var impact=mouth+turn*Vector3.ProjectOnPlane(worldFoot-worldLip,Vector3.up);impact.y=waterY+.006f;
                foreach(string effect in new[]{"Impact ripples ","Airborne droplets "}) {
                    var child=t.parent.Find(effect+number);if(!child)continue;child.localPosition=Vector3.zero;child.localRotation=Quaternion.identity;child.localScale=Vector3.one;
                    var points=child.GetComponent<MeshFilter>().sharedMesh.vertices;var center=points.Aggregate(Vector3.zero,(a,b)=>a+b)/points.Length;
                    child.position+=impact-child.TransformPoint(center);
                }
                Debug.Log("WATERFALL_BANK "+key+" "+number+" mouth="+mouth+" water="+waterY);
            }
        }
        public static void ClearWaterfallDressing(GameObject root,string key)
        {
            var volumes=root.GetComponentsInChildren<MeshFilter>().Where(m=>m.name.StartsWith("Flowing curtain")).Select(m=>{var b=m.GetComponent<Renderer>().bounds;b.Expand(.12f);return b;}).ToArray();
            foreach(var dressing in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="10 Botanical dressing").ToArray()) {
                var source=dressing.Cast<Transform>().FirstOrDefault(t=>t.name.StartsWith("Editable placement"));
                var meadow=source?source.Find("Moss ivy ferns and meadow"):null;if(!meadow)continue;bool changed=false;
                foreach(Transform plant in meadow) {
                    var renderers=plant.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();
                    if(!renderers.Any(r=>volumes.Any(b=>b.Intersects(r.bounds))))continue;
                    foreach(var r in renderers)r.enabled=false;changed=true;
                }
                if(changed)Combine(meadow,dressing.Find("Render batches/Moss ivy ferns and meadow"),key+"-WaterfallMeadow");
            }
        }
    }
}
