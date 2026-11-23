using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PhysicalAssembly
{
    // Shared stone art for sandbox puzzles (Resources/SandboxStone). Meshes come
    // from the Beautiful Corner kit and must stay readable for fracturing.
    public sealed class SandboxStoneLibrary : ScriptableObject
    {
        public Mesh Stone;
        public Material StoneMaterial;
        public Mesh[] Pebbles;
        static SandboxStoneLibrary cached;
        public static SandboxStoneLibrary Load() { if(!cached) cached=Resources.Load<SandboxStoneLibrary>("SandboxStone"); return cached; }

        // Adds a collider-free stone that fills the unit cube of `parent` (a scaled primitive).
        public static Transform AddStoneVisual(Transform parent,string label,float fill=1.15f)
        {
            var library=Load(); if(!library || !library.Stone || !library.StoneMaterial) return null;
            var go=new GameObject(label); go.transform.SetParent(parent,false);
            var bounds=library.Stone.bounds; float scale=fill/Mathf.Max(.0001f,Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z));
            go.transform.localScale=Vector3.one*scale; go.transform.localPosition=-bounds.center*scale;
            go.AddComponent<MeshFilter>().sharedMesh=library.Stone;
            go.AddComponent<MeshRenderer>().sharedMaterial=library.StoneMaterial;
            return go.transform;
        }

        // Heaps small stones over a roller lying along local X. Visual only: the
        // roller's own collider keeps deciding how the car is deflected.
        public static Transform AddGravel(Transform root,string label,float width,float diameter,float height,int seed)
        {
            var library=Load(); if(!library || library.Pebbles==null || library.Pebbles.Length==0 || !library.StoneMaterial) return null;
            var gravel=new GameObject(label).transform; gravel.SetParent(root,false);
            var previous=Random.state; Random.InitState(seed);
            try {
                float radius=diameter*.5f, stone=diameter*.62f;
                int columns=Mathf.Clamp(Mathf.CeilToInt(width/(stone*.6f)),4,36);
                for(int column=0;column<columns;column++) for(int row=0;row<2;row++) {
                    var mesh=library.Pebbles[Random.Range(0,library.Pebbles.Length)]; if(!mesh) continue;
                    // Row 0 crowns the bump; row 1 scatters down both flanks to the road.
                    float angle=(row==0?Random.Range(-35f,35f):Random.Range(50f,95f)*(column%2==0?1:-1))*Mathf.Deg2Rad;
                    float x=Mathf.Clamp((column+.5f)/columns*width-width*.5f+Random.Range(-.2f,.2f)*stone,-width*.5f+stone*.3f,width*.5f-stone*.3f);
                    float size=stone*Random.Range(.75f,1.15f)*(row==0?1:.8f);
                    var bounds=mesh.bounds; float scale=size/Mathf.Max(.0001f,Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z));
                    var rotation=Quaternion.Euler(Random.Range(-20f,20f),Random.Range(0f,360f),Random.Range(-20f,20f));
                    // Keep each stone's top at or just under the collider surface.
                    float reach=Mathf.Max(0,radius-bounds.extents.y*scale);
                    var center=new Vector3(x,height+radius+Mathf.Cos(angle)*reach,Mathf.Sin(angle)*Mathf.Max(reach,radius*.9f));
                    center.y=Mathf.Max(center.y,height+bounds.extents.y*scale*.6f);
                    var go=new GameObject("Gravel stone"); go.transform.SetParent(gravel,false);
                    go.transform.localRotation=rotation; go.transform.localScale=Vector3.one*scale;
                    go.transform.localPosition=center-rotation*(bounds.center*scale);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=library.StoneMaterial;
                    renderer.shadowCastingMode=ShadowCastingMode.Off;
                }
            } finally { Random.state=previous; }
            return gravel;
        }
    }

    // Pre-fractured stone: the mesh is cut once into capped chunks (cached), and
    // each break releases them as short-lived rigid bodies.
    public sealed class SandboxStoneShatter : MonoBehaviour
    {
        public const int ChunkCount=10;
        public const float Lifetime=2.5f, FadeSeconds=.4f;
        sealed class Chunk { public Mesh Mesh; public Vector3 Center, Size; }
        struct Vertex { public Vector3 Position; public Vector2 UV; }
        static readonly Dictionary<Mesh,List<Chunk>> cache=new();
        readonly List<Collider> colliders=new();
        readonly List<Transform> pieces=new();
        float age;
        Vector3 viewScale;
        public int PieceCount => pieces.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() { cache.Clear(); }

        // `visual` is the intact stone's transform; chunks appear exactly where it was.
        public static SandboxStoneShatter Spawn(Transform visual,Mesh mesh,Material material,Vector3 hitPoint,Vector3 carryVelocity)
        {
            if(!Application.isPlaying || !visual || !mesh || !mesh.isReadable) return null;
            var chunks=Fracture(mesh); if(chunks.Count==0) return null;
            var root=new GameObject("Shattered stone"); SceneManager.MoveGameObjectToScene(root,visual.gameObject.scene);
            var shatter=root.AddComponent<SandboxStoneShatter>();
            var scale=visual.lossyScale; shatter.viewScale=scale; float world=Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
            float extent=Mathf.Max(mesh.bounds.size.x,mesh.bounds.size.y,mesh.bounds.size.z)*world;
            // Launch speed that would lift a chunk about half a stone height against gravity.
            float speed=Mathf.Sqrt(2*Physics.gravity.magnitude*extent*.5f);
            foreach(var chunk in chunks) {
                var body=new GameObject("Stone chunk"); body.transform.SetParent(root.transform,false);
                // Dimensions are baked into the collider and the child; the rigidbody root stays unscaled.
                body.transform.SetPositionAndRotation(visual.TransformPoint(chunk.Center),visual.rotation);
                var view=new GameObject("Chunk mesh"); view.transform.SetParent(body.transform,false); view.transform.localScale=scale;
                view.AddComponent<MeshFilter>().sharedMesh=chunk.Mesh;
                var renderer=view.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material; renderer.shadowCastingMode=ShadowCastingMode.Off;
                var box=body.AddComponent<BoxCollider>(); box.size=Vector3.Max(Vector3.Scale(chunk.Size,scale)*.8f,Vector3.one*extent*.04f);
                var rb=body.AddComponent<Rigidbody>(); rb.mass=.02f; rb.angularDamping=.2f;
                var outward=body.transform.position-hitPoint; outward=outward.sqrMagnitude>1e-10f?outward.normalized:Random.onUnitSphere;
                rb.linearVelocity=carryVelocity*.4f+(outward+Vector3.up*.5f).normalized*speed*Random.Range(.5f,1f);
                rb.angularVelocity=Random.insideUnitSphere*12;
                shatter.colliders.Add(box); shatter.pieces.Add(body.transform);
            }
            // Debris is for show: it must never wedge the bridge deck or the car.
            foreach(var other in Physics.OverlapSphere(visual.position,extent*8,~0,QueryTriggerInteraction.Ignore))
                if(other.attachedRigidbody) shatter.Ignore(other);
            return shatter;
        }
        public void Ignore(Collider other)
        {
            if(!other) return;
            foreach(var collider in colliders) if(collider && collider!=other) Physics.IgnoreCollision(collider,other,true);
        }
        void Update()
        {
            age+=Time.deltaTime;
            if(age>=Lifetime+FadeSeconds) { Destroy(gameObject); return; }
            if(age<Lifetime) return;
            float shrink=Mathf.Clamp01(1-(age-Lifetime)/FadeSeconds);
            foreach(var piece in pieces) if(piece && piece.childCount>0) piece.GetChild(0).localScale=viewScale*shrink;
        }

        static List<Chunk> Fracture(Mesh mesh)
        {
            if(cache.TryGetValue(mesh,out var ready) && ready.Count>0 && ready[0].Mesh) return ready;
            var positions=mesh.vertices; var uvs=mesh.uv; var triangles=mesh.triangles;
            var whole=new List<Vertex>(triangles.Length);
            foreach(int index in triangles) whole.Add(new Vertex{Position=positions[index],UV=index<uvs.Length?uvs[index]:Vector2.zero});
            var parts=new List<List<Vertex>>{whole};
            var previous=Random.state; Random.InitState(mesh.vertexCount*7919+17);
            try {
                for(int guard=0;parts.Count<ChunkCount && guard<ChunkCount*4;guard++) {
                    // Always cut the largest remaining piece across its longest axis, with some tilt.
                    int largest=0; float volume=-1;
                    for(int i=0;i<parts.Count;i++) { var size=Bounds(parts[i]).size; float v=size.x*size.y*size.z; if(v>volume) { volume=v; largest=i; } }
                    var bounds=Bounds(parts[largest]); var axis=Vector3.right;
                    if(bounds.size.y>=bounds.size.x && bounds.size.y>=bounds.size.z) axis=Vector3.up; else if(bounds.size.z>=bounds.size.x) axis=Vector3.forward;
                    var normal=(axis+Random.insideUnitSphere*.6f).normalized;
                    var point=bounds.center+Vector3.Scale(Random.insideUnitSphere,bounds.extents)*.25f;
                    Split(parts[largest],normal,point,out var front,out var back);
                    if(front.Count<12 || back.Count<12) continue;
                    parts[largest]=front; parts.Add(back);
                }
            } finally { Random.state=previous; }
            var chunks=new List<Chunk>();
            foreach(var part in parts) {
                var bounds=Bounds(part); var vertices=new Vector3[part.Count]; var uv=new Vector2[part.Count]; var indices=new int[part.Count];
                for(int i=0;i<part.Count;i++) { vertices[i]=part[i].Position-bounds.center; uv[i]=part[i].UV; indices[i]=i; }
                var chunk=new Mesh{name="Stone chunk",hideFlags=HideFlags.HideAndDontSave};
                chunk.vertices=vertices; chunk.uv=uv; chunk.triangles=indices; chunk.RecalculateNormals(); chunk.RecalculateBounds();
                chunks.Add(new Chunk{Mesh=chunk,Center=bounds.center,Size=bounds.size});
            }
            cache[mesh]=chunks; return chunks;
        }
        static Bounds Bounds(List<Vertex> triangles)
        {
            var bounds=new Bounds(triangles[0].Position,Vector3.zero);
            foreach(var vertex in triangles) bounds.Encapsulate(vertex.Position);
            return bounds;
        }
        // Clips a triangle soup by a plane and closes both halves with a centroid fan.
        static void Split(List<Vertex> source,Vector3 normal,Vector3 point,out List<Vertex> front,out List<Vertex> back)
        {
            front=new List<Vertex>(); back=new List<Vertex>(); var cuts=new List<Vertex>();
            var polygonFront=new List<Vertex>(4); var polygonBack=new List<Vertex>(4);
            for(int t=0;t<source.Count;t+=3) {
                polygonFront.Clear(); polygonBack.Clear(); int cutStart=cuts.Count;
                for(int i=0;i<3;i++) {
                    var a=source[t+i]; var b=source[t+(i+1)%3];
                    float da=Vector3.Dot(a.Position-point,normal), db=Vector3.Dot(b.Position-point,normal);
                    if(da>=0) polygonFront.Add(a); else polygonBack.Add(a);
                    if((da>=0)!=(db>=0)) {
                        float k=da/(da-db);
                        var cut=new Vertex{Position=Vector3.Lerp(a.Position,b.Position,k),UV=Vector2.Lerp(a.UV,b.UV,k)};
                        polygonFront.Add(cut); polygonBack.Add(cut); cuts.Add(cut);
                    }
                }
                Fan(polygonFront,front); Fan(polygonBack,back);
                if(cuts.Count-cutStart!=2) cuts.RemoveRange(cutStart,cuts.Count-cutStart);
            }
            if(cuts.Count<6) return;
            var centre=Vector3.zero; foreach(var cut in cuts) centre+=cut.Position; centre/=cuts.Count;
            var middle=new Vertex{Position=centre,UV=new Vector2(.5f,.5f)};
            for(int i=0;i<cuts.Count;i+=2) {
                var a=cuts[i]; var b=cuts[i+1];
                bool towardNormal=Vector3.Dot(Vector3.Cross(a.Position-centre,b.Position-centre),normal)>0;
                // The front half's cap faces against the normal; the back half's faces along it.
                if(towardNormal) { front.Add(middle); front.Add(b); front.Add(a); back.Add(middle); back.Add(a); back.Add(b); }
                else { front.Add(middle); front.Add(a); front.Add(b); back.Add(middle); back.Add(b); back.Add(a); }
            }
        }
        static void Fan(List<Vertex> polygon,List<Vertex> output)
        {
            for(int i=1;i+1<polygon.Count;i++) { output.Add(polygon[0]); output.Add(polygon[i]); output.Add(polygon[i+1]); }
        }
    }
}
