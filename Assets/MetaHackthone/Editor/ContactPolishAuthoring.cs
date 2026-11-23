using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using static PhysicalAssembly.Editor.EnvironmentPolishAuthoring;

namespace PhysicalAssembly.Editor
{
    // Artwork only: no road, physics, vehicle or progression data is regenerated here.
    public static class ContactPolishAuthoring
    {
        const string Art = "Assets/CreekDiorama";
        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/" + name + ".mat");
        static bool Visible(Transform t) { while (t) { if (!t.gameObject.activeSelf) return false; t=t.parent; } return true; }
        static bool Ground(MeshFilter f) => f.name.EndsWith("ground turf") || (f.transform.parent && f.transform.parent.name=="03 Meadow caps");
        internal static MeshFilter[] VisibleGround(GameObject root) => root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh && Visible(f.transform) && f.GetComponent<Renderer>() && f.GetComponent<Renderer>().enabled && (Ground(f) || (f.name=="Botanical moss" && f.transform.parent.name=="Stratified rock and soil"))).ToArray();
        static MeshFilter[] Water(GameObject root) => root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh && f.GetComponent<Renderer>() && f.GetComponent<Renderer>().enabled && Visible(f.transform) && f.GetComponent<Renderer>().sharedMaterial.shader.name=="Creek Diorama/Living Water" && !f.name.Contains("spring")).ToArray();
        public sealed class Surface
        {
            readonly List<(Vector3 a,Vector3 b,Vector3 c)> triangles=new();
            public Surface(IEnumerable<MeshFilter> filters)
            {
                foreach(var f in filters) { var v=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();var ix=f.sharedMesh.triangles;
                    for(int i=0;i<ix.Length;i+=3)triangles.Add((v[ix[i]],v[ix[i+1]],v[ix[i+2]])); }
            }
            public bool Height(Vector3 p,out float y,float ceiling=float.PositiveInfinity)
            {
                y=float.NegativeInfinity;
                foreach(var t in triangles) {
                    var a=t.a;var b=t.b;var c=t.c;
                    if(p.x<Mathf.Min(a.x,Mathf.Min(b.x,c.x)) || p.x>Mathf.Max(a.x,Mathf.Max(b.x,c.x)) || p.z<Mathf.Min(a.z,Mathf.Min(b.z,c.z)) || p.z>Mathf.Max(a.z,Mathf.Max(b.z,c.z)))continue;
                    float den=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(den)<1e-10f)continue;
                    float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/den;
                    float w=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/den;
                    if(u<-.00001f || w<-.00001f || u+w>1.00001f)continue;
                    float h=u*a.y+w*b.y+(1-u-w)*c.y;if(h<=ceiling)y=Mathf.Max(y,h);
                }
                return !float.IsNegativeInfinity(y);
            }
            public bool Occludes(Vector3 from,Vector3 to)
            {
                var direction=to-from;var segment=new Bounds((from+to)*.5f,new Vector3(Mathf.Abs(direction.x),Mathf.Abs(direction.y),Mathf.Abs(direction.z)));
                foreach(var t in triangles) {
                    var box=new Bounds(t.a,Vector3.zero);box.Encapsulate(t.b);box.Encapsulate(t.c);if(!box.Intersects(segment))continue;
                    var e1=t.b-t.a;var e2=t.c-t.a;var h=Vector3.Cross(direction,e2);float determinant=Vector3.Dot(e1,h);
                    if(Mathf.Abs(determinant)<1e-9f)continue;float inv=1/determinant;var s=from-t.a;float u=inv*Vector3.Dot(s,h);if(u<0||u>1)continue;
                    var q=Vector3.Cross(s,e1);float v=inv*Vector3.Dot(direction,q);if(v<0||u+v>1)continue;
                    float distance=inv*Vector3.Dot(e2,q);if(distance>.0001f&&distance<.995f)return true;
                }
                return false;
            }
        }
        static Vector3Int Key(Vector3 p) => new(Mathf.RoundToInt(p.x*100000),Mathf.RoundToInt(p.y*100000),Mathf.RoundToInt(p.z*100000));
        public static void FinalCourseComposition(SandboxFactoryLevel factory,string key)
        {
            var art=factory.Layout.Course.SandboxFrame.Find("Environment/Car campaign scenery");
            var pose=factory.Layout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance);
            var forward=pose.rotation*Vector3.forward;var right=pose.rotation*Vector3.right;
            var points=factory.Tracks.SelectMany(t=>t.Samples.Select(t.transform.TransformPoint)).ToArray();
            var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);
            float span=Mathf.Max(bounds.size.x,bounds.size.z,1);
            var backdrop=art.Find("Horizon transition islands");
            if(backdrop){backdrop.SetPositionAndRotation(new Vector3(bounds.center.x,bounds.min.y-.1f*span,bounds.center.z),pose.rotation);backdrop.localScale=Vector3.one*span/art.lossyScale.x;}
            var clouds=art.Find("Layered realistic cloud banks");
            if(clouds)for(int i=0;i<clouds.childCount;i++) {
                var cloud=clouds.GetChild(i);float angle=Mathf.Lerp(-62,62,i/(float)(clouds.childCount-1));
                var direction=Quaternion.AngleAxis(angle,Vector3.up)*forward;
                cloud.position=new Vector3(bounds.center.x,pose.position.y+.35f+(i%3)*.55f,bounds.center.z)+direction*span*(1.65f+(i%2)*.25f);
            }
            TrayWaterRepair.Fit(factory,key);
        }
        public static void Terrain(GameObject root,string key)
        {
            int id=0;
            var grounds=root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh && Ground(f)).ToArray();
            foreach(var f in grounds) {
                var old=f.transform.Find("Continuous turf rim");if(old)Object.DestroyImmediate(old.gameObject);
                var verts=f.sharedMesh.vertices;var ix=f.sharedMesh.triangles;
                var edges=new Dictionary<(Vector3Int,Vector3Int),(Vector3 a,Vector3 b,int count)>();
                void Edge(Vector3 a,Vector3 b) {var ka=Key(a);var kb=Key(b);var k=(ka,kb);var reverse=(kb,ka);if(edges.TryGetValue(reverse,out var e))edges[reverse]=(e.a,e.b,e.count+1);else if(edges.TryGetValue(k,out e))edges[k]=(e.a,e.b,e.count+1);else edges[k]=(a,b,1);}
                for(int i=0;i<ix.Length;i+=3){Edge(verts[ix[i]],verts[ix[i+1]]);Edge(verts[ix[i+1]],verts[ix[i+2]]);Edge(verts[ix[i+2]],verts[ix[i]]);}
                var v=new List<Vector3>();var tr=new List<int>();var center=f.sharedMesh.bounds.center;
                bool island=f.name.EndsWith("ground turf");float depth=island?.045f:.025f;
                var bedrock=island?f.transform.parent.GetComponentsInChildren<MeshFilter>().FirstOrDefault(m=>m.name.EndsWith("ground bedrock")):null;
                var rockVertices=bedrock?bedrock.sharedMesh.vertices.Select(p=>f.transform.InverseTransformPoint(bedrock.transform.TransformPoint(p))).ToArray():Array.Empty<Vector3>();
                void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){int n=v.Count;v.AddRange(new[]{a,b,c,d});tr.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
                foreach(var e in edges.Values.Where(e=>e.count==1)) {
                    Vector3 Outer(Vector3 p){if(island){if(rockVertices.Length>0)p=rockVertices.OrderBy(q=>(q-p).sqrMagnitude).First()+Vector3.up*.01f;p.x=center.x+(p.x-center.x)*1.012f;p.z=center.z+(p.z-center.z)*1.012f;}return p;}
                    var a=Outer(e.a);var b=Outer(e.b);
                    if(island)Quad(e.a,a,b,e.b);
                    Quad(a,a-Vector3.up*depth,b-Vector3.up*depth,b);
                }
                var mesh=new Mesh{name="Closed turf edge",indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                RenderMesh(f.transform,"Continuous turf rim",Save(mesh,key+"-TurfRim"+id++),f.GetComponent<Renderer>().sharedMaterial);
            }
            var surface=new Surface(VisibleGround(root));
            int raised=0;
            var factory=root.GetComponent<SandboxFactoryLevel>();var lanes=factory?CarSceneryClearance.Lanes(factory):null;
            foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Grass_Soft_Tuft" || t.name=="Fern_Rosette" || t.name=="Flowers_Daisies" || t.name=="Flowers_Lupin")) {
                var p=t.position;float lift=0;
                var rr=t.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();if(rr.Length==0)continue;
                var plantBounds=rr[0].bounds;foreach(var r in rr)plantBounds.Encapsulate(r.bounds);
                // A later authored landing may cover the lower half of an older clump.
                // Lift the still-visible clump to that landing; fully buried decoration stays buried.
                if(surface.Height(p,out float landing) && landing>p.y && landing<plantBounds.max.y-.005f && landing-p.y<.3f)lift=landing-p.y+.002f;
                foreach(var f in t.GetComponentsInChildren<MeshFilter>(true)) {
                    var bounds=f.sharedMesh.bounds;
                    // Each tuft has several blades spread over a footprint; their roots must
                    // clear the local slope, not just the terrain directly under the pivot.
                    foreach(float x in new[]{bounds.min.x,bounds.center.x,bounds.max.x})foreach(float z in new[]{bounds.min.z,bounds.center.z,bounds.max.z}) {
                        var sample=f.transform.TransformPoint(new Vector3(x,bounds.min.y,z));
                        if(surface.Height(sample,out float y,sample.y+.06f) && y>sample.y)lift=Mathf.Max(lift,y-sample.y+.002f);
                    }
                }
                if(lift>.0005f && lift<.305f) {
                    t.position=p+Vector3.up*lift;raised++;
                    if(lanes!=null && t.GetComponentsInChildren<MeshFilter>(true).Any(f=>f.GetComponent<Renderer>().enabled && CarSceneryClearance.MeshOverlaps(f,lanes))) {
                        t.position=p;foreach(var r in rr)r.enabled=false;
                    }
                }
            }
            // Editable plant instances are the source of the static meadow batch.
            foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>raised>0 && t.name=="10 Botanical dressing").ToArray()) {
                var editable=t.Cast<Transform>().FirstOrDefault(c=>c.name.StartsWith("Editable placement"));
                var source=editable?editable.Find("Moss ivy ferns and meadow"):null;
                var destination=t.Find("Render batches/Moss ivy ferns and meadow");if(!source || !destination)continue;
                foreach(Transform child in destination.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
                var filters=source.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh && f.GetComponent<Renderer>().enabled);
                int batch=0;
                foreach(var group in filters.GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial)) {
                    var mesh=new Mesh{name="Grounded meadow",indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=destination.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
                    RenderMesh(destination,group.Key.name,Save(mesh,key+"-GroundedMeadow"+batch++),group.Key);
                }
            }
            Debug.Log("CONTACT_TERRAIN "+key+" rims="+id+" grass_adjusted="+raised);
        }
        static float Floor(GameObject root) {
            var rr=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>Visible(r.transform) && (r.name=="Basalt enamel" || r.name.Contains("Assembly tabletop"))).ToArray();
            return rr.Length>0?rr.Min(r=>r.bounds.max.y):root.transform.position.y;
        }
        static float BaseHeight(Vector3 top,Surface water,Surface terrain,float floor,out bool wet)
        {wet=water.Height(top,out float w,top.y-.025f);bool land=terrain.Height(top,out float g,top.y-.025f);return land && (!wet || g>w+.025f)?g:wet?w:floor;}
        static void Upright(Transform t,Quaternion worldRotation) => t.rotation=worldRotation;
        static Transform Foundation(Transform parent,Vector3 p,float width,bool wet)
        {
            float h=Mathf.Max(.045f,width*.48f);var center=p+Vector3.up*(wet?-.16f*h:0);
            var stone=Rock(parent,wet?"Waterline stone foundation":"Grounded stone foundation",parent.InverseTransformPoint(center),new Vector3(width*1.12f,h,width*.72f)/parent.lossyScale.x);
            Upright(stone,Quaternion.identity);var renderers=stone.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);stone.position+=center-bounds.center;return stone;
        }
        public static void Roads(SandboxFactoryLevel factory,string key)
        {
            var water=new Surface(Water(factory.gameObject));var terrain=new Surface(VisibleGround(factory.gameObject));
            var lanes=CarSceneryClearance.Lanes(factory);var wood=Mat("Botanical oak");var metal=Mat("Botanical bronze");float floor=Floor(factory.gameObject);int index=0;
            foreach(var track in factory.Tracks) {
                foreach(string name in new[]{"Timber trestles and floating foundations","Grounded timber trestles","Trestle render batches"}){var old=track.transform.Find(name);if(old)Object.DestroyImmediate(old.gameObject);}
                var art=Group(track.transform,"Grounded timber trestles");var samples=SandboxTrackBaker.SamplePath(track);float last=-100,scale=track.transform.lossyScale.x;
                foreach(var s in samples) {
                    if(s.Distance<.5f || s.Distance-last<4.2f || s.Distance>samples.Last().Distance-.6f)continue;
                    if(factory.Puzzles.Any(p=>p && p.Track==track && p.ReplacesRoad && Mathf.Abs(s.Distance-p.Distance)<p.Span*.5f+1.3f))continue;
                    last=s.Distance;var t=Group(art,"Vertical pier "+index++);t.localPosition=s.Position;t.localRotation=Quaternion.LookRotation(s.Forward,s.Up);
                    Box(t,"Deck cross bearer",new Vector3(0,-.23f,0),new Vector3(s.Width+.3f,.24f,.28f),wood);
                    var top=t.TransformPoint(new Vector3(0,-.3f,0));float baseY=BaseHeight(top,water,terrain,floor,out bool wet);
                    var foot=new Vector3(top.x,baseY,top.z);float width=s.Width*scale;
                    Foundation(t,foot,width,wet);float stoneTop=baseY+Mathf.Max(.045f,width*.48f)*(wet?.3f:.45f);
                    for(int side=-1;side<=1;side+=2) {
                        var a=t.TransformPoint(new Vector3(side*s.Width*.36f,-.3f,0));var b=new Vector3(a.x,Mathf.Min(stoneTop,a.y-.02f),a.z);
                        Beam(t,"Vertical oak pile",t.InverseTransformPoint(a),t.InverseTransformPoint(b),.18f,wood);
                        Box(t,"Underdeck stringer",new Vector3(side*s.Width*.36f,-.14f,0),new Vector3(.14f,.17f,1.9f),wood);
                        var brace=Vector3.Lerp(a,b,.27f);Beam(t,"Pier knee brace",t.InverseTransformPoint(brace),new Vector3(side*s.Width*.49f,-.28f,.6f),.12f,wood);
                        var band=Box(t,"Pile iron collar",t.InverseTransformPoint(Vector3.Lerp(a,b,.12f)),new Vector3(.22f,.13f,.22f),metal);Upright(band,Quaternion.identity);
                    }
                    if(t.GetComponentsInChildren<MeshFilter>().Any(f=>CarSceneryClearance.MeshOverlaps(f,lanes)))Object.DestroyImmediate(t.gameObject);
                }
                // Seats and iron cheeks explain the connection between deck ends and the rock landing.
                foreach(var s in new[]{samples[0],samples.Last()}) {
                    var t=Group(art,"Rock landing anchor");t.localPosition=s.Position;t.localRotation=Quaternion.LookRotation(s.Forward,s.Up);
                    Box(t,"Oak landing sill",new Vector3(0,-.24f,0),new Vector3(s.Width+.35f,.28f,.7f),wood);
                    Rock(t,"Stone abutment seat",new Vector3(0,-.54f,0),new Vector3(s.Width+.55f,.4f,.65f));
                    for(int side=-1;side<=1;side+=2) {
                        Box(t,"Iron landing cheek",new Vector3(side*(s.Width*.5f+.06f),-.22f,0),new Vector3(.07f,.28f,.55f),metal);
                        Box(t,"Oak keyed end",new Vector3(side*s.Width*.36f,-.14f,0),new Vector3(.23f,.12f,.9f),wood);
                    }
                }
                foreach(var collider in art.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                var batches=Group(track.transform,"Trestle render batches");BakeStatic(art,batches,key+"-GroundedTrestles");art.gameObject.SetActive(false);
            }
        }
        public static void GroundBridge(SandboxDropBridge bridge)
        {
            var root=bridge.GetComponentInParent<SandboxFactoryLevel>();var art=bridge.Generated.transform.Find("Timber lifting rig");
            foreach(var old in art.Cast<Transform>().Where(t=>t.name=="Hinge stone footing" || t.name=="Waterline stone foundation" || t.name=="Grounded stone foundation" || t.name=="Foot shoe" || t.name=="Bridge bank connection" || t.name=="Tower deck shoe").ToArray())Object.DestroyImmediate(old.gameObject);
            // The lifting gantry belongs to the stationary deck edge. Ordinary trestles
            // below the road carry the load to the water; gantry poles stop at the deck.
            var samples=root.Tracks.SelectMany(track=>SandboxTrackBaker.SamplePath(track).Select(s=>track.transform.TransformPoint(s.Position))).ToArray();
            var seats=new HashSet<string>();
            foreach(var post in art.Cast<Transform>().Where(t=>t.name=="Hinge tower" || t.name=="Latch tower").ToArray()) {
                var top=post.TransformPoint(Vector3.up*.5f);float width=post.lossyScale.x;
                var anchor=post.name=="Hinge tower"?bridge.Pivot.position:bridge.TargetBody.transform.position;
                var road=post.name=="Hinge tower"?anchor:samples.OrderBy(p=>(p-anchor).sqrMagnitude).First();
                var bottom=new Vector3(top.x,road.y-width*.35f,top.z);post.position=(top+bottom)*.5f;post.rotation=Quaternion.identity;
                post.localScale=new Vector3(width,Vector3.Distance(top,bottom),width)/art.lossyScale.x;
                var shoe=Box(art,"Tower deck shoe",art.InverseTransformPoint(bottom+Vector3.up*width*.4f),new Vector3(width*1.5f,width*.9f,width*1.5f)/art.lossyScale.x,Mat("Botanical bronze"));shoe.rotation=Quaternion.identity;
                if(seats.Add(post.name)){var seat=Box(art,"Bridge bank connection",art.InverseTransformPoint(new Vector3(road.x,road.y-width,road.z)),new Vector3(bridge.Width+.75f,.18f,.42f),Mat("Botanical oak"));seat.rotation=bridge.Pivot.rotation;}
            }
            float scale=bridge.transform.lossyScale.x;
            foreach(var p in new[]{bridge.Pivot.position,bridge.Pivot.position+bridge.Pivot.forward*bridge.Length*scale}) {
                var t=Group(art,"Bridge bank connection");t.position=p;t.rotation=bridge.Pivot.rotation;
                Box(t,"Hinge bearing sill",new Vector3(0,-.22f,0),new Vector3(bridge.Width+.3f,.22f,.4f),Mat("Botanical oak"));
                for(int side=-1;side<=1;side+=2)Box(t,"Bronze deck socket",new Vector3(side*(bridge.Width*.5f+.05f),-.12f,0),new Vector3(.08f,.27f,.36f),Mat("Botanical bronze"));
            }
        }
        public static void Foam(SandboxFactoryLevel factory,string key)
        {
            var material=Mat("Contact shore foam");if(!material){material=new Material(Shader.Find("Creek Diorama/Contact Foam"));AssetDatabase.CreateAsset(material,Art+"/Materials/Contact shore foam.mat");}
            var waters=Water(factory.gameObject);int id=0,total=0;
            foreach(var water in waters) {
                var old=water.transform.Find("Shore contact foam");if(old)Object.DestroyImmediate(old.gameObject);
                float height=water.GetComponent<Renderer>().bounds.center.y;var waterBounds=water.sharedMesh.bounds;
                var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();var seen=new HashSet<string>();
                var filters=factory.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh && f.GetComponent<Renderer>() && f.GetComponent<Renderer>().enabled && Visible(f.transform) && f!=water && f.GetComponent<Renderer>().sharedMaterial.renderQueue<3000 && !f.GetComponent<Renderer>().sharedMaterial.name.Contains("leaf") && !f.GetComponent<Renderer>().sharedMaterial.name.Contains("Pine") && !f.GetComponent<Renderer>().sharedMaterial.name.Contains("Cloud")).ToArray();
                foreach(var f in filters) {
                    var b=f.GetComponent<Renderer>().bounds;if(b.min.y>height+.0001f || b.max.y<height-.0001f || !b.Intersects(water.GetComponent<Renderer>().bounds))continue;
                    var v=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();var ix=f.sharedMesh.triangles;
                    for(int j=0;j<ix.Length;j+=3) {
                        var points=new List<Vector3>(3);var vv=new[]{v[ix[j]],v[ix[j+1]],v[ix[j+2]]};
                        for(int k=0;k<3;k++){var a=vv[k];var c=vv[(k+1)%3];if((a.y-height)*(c.y-height)<0)points.Add(Vector3.Lerp(a,c,(height-a.y)/(c.y-a.y)));}
                        if(points.Count!=2 || (points[0]-points[1]).sqrMagnitude<.00000001f)continue;
                        var a0=points[0];var b0=points[1];string ka=Key(a0).ToString(),kb=Key(b0).ToString();string token=string.CompareOrdinal(ka,kb)<0?ka+kb:kb+ka;if(!seen.Add(token))continue;
                        var mid=water.transform.InverseTransformPoint((a0+b0)*.5f);if(mid.x<waterBounds.min.x || mid.x>waterBounds.max.x || mid.z<waterBounds.min.z || mid.z>waterBounds.max.z)continue;
                        var across=Vector3.Cross(Vector3.up,(b0-a0).normalized)*.028f;
                        // Both sides are drawn; opaque geometry naturally hides the land-facing half.
                        foreach(int side in new[]{-1,1}) {
                            int n=vertices.Count;foreach(var p in new[]{a0,b0,b0+across*side,a0+across*side})vertices.Add(water.transform.InverseTransformPoint(p+Vector3.up*.002f));
                            uv.AddRange(new[]{new Vector2(0,0),new Vector2(Vector3.Distance(a0,b0)*35,0),new Vector2(Vector3.Distance(a0,b0)*35,1),new Vector2(0,1)});
                            triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
                        }
                    }
                }
                if(vertices.Count==0)continue;
                var mesh=new Mesh{name="Water intersection ribbons",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.RecalculateNormals();
                RenderMesh(water.transform,"Shore contact foam",Save(mesh,key+"-ShoreFoam"+id++),material);total+=triangles.Count/3;
            }
            Debug.Log("CONTACT_FOAM "+key+" water_surfaces="+id+" triangles="+total);
        }
    }
}
