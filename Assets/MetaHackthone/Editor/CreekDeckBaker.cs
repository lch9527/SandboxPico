using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalAssembly.Editor
{
    // Render-only geometry. The smooth Road mesh remains authoritative for physics.
    public static class CreekDeckBaker
    {
        public const string ModulePath="Assets/CreekDiorama/Prefabs/Modules/Deck_Straight_080.prefab";
        struct Vertex
        {
            public Vector3 p,n; public Vector2 uv;
            public static Vertex Lerp(Vertex a,Vertex b,float t) => new Vertex {p=Vector3.Lerp(a.p,b.p,t),n=Vector3.Lerp(a.n,b.n,t),uv=Vector2.Lerp(a.uv,b.uv,t)};
        }
        sealed class Buffer
        {
            public Material material;
            public readonly List<Vector3> vertices=new(),normals=new();
            public readonly List<Vector2> uv=new();
            public readonly List<int> indices=new();
        }
        sealed class CutFace
        {
            public SandboxTrackBaker.Sample a,b;
            public Vector3 normal;
            public readonly List<(Vector3 a,Vector3 b)> edges=new();
        }
        static void WritePolygon(Buffer buffer,List<Vertex> polygon,SandboxTrackBaker.Sample a,SandboxTrackBaker.Sample b,bool cap=false)
        {
            int start=buffer.vertices.Count;
            foreach(var v in polygon) {
                float t=Mathf.InverseLerp(a.Distance,b.Distance,v.p.z);
                var right=Vector3.Slerp(a.Right,b.Right,t);var up=Vector3.Slerp(a.Up,b.Up,t);var forward=Vector3.Slerp(a.Forward,b.Forward,t);
                float width=Mathf.Lerp(a.Width,b.Width,t);
                buffer.vertices.Add(Vector3.Lerp(a.Position,b.Position,t)+right*(v.p.x*width)+up*v.p.y);
                buffer.normals.Add((right*(cap?v.n.x/width:v.n.x)+up*v.n.y+forward*v.n.z).normalized);buffer.uv.Add(v.uv);
            }
            for(int j=1;j<polygon.Count-1;j++){buffer.indices.Add(start);buffer.indices.Add(start+j);buffer.indices.Add(start+j+1);}
            if(buffer.vertices.Count>2000000)throw new InvalidOperationException("Deck art exceeds mesh budget; split the track.");
        }
        static int[] ConnectedSolids(Vector3[] positions,int[] triangles)
        {
            var parent=Enumerable.Range(0,positions.Length).ToArray();
            int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
            void Join(int a,int b){parent[Root(a)]=Root(b);}
            var welded=new Dictionary<Vector3Int,int>();
            for(int i=0;i<positions.Length;i++) {
                var key=Vector3Int.RoundToInt(positions[i]*100000f);
                if(welded.TryGetValue(key,out var old))Join(i,old);else welded.Add(key,i);
            }
            for(int i=0;i<triangles.Length;i+=3){Join(triangles[i],triangles[i+1]);Join(triangles[i],triangles[i+2]);}
            return triangles.Where((_,i)=>i%3==0).Select(Root).ToArray();
        }
        static void CloseCuts(Buffer buffer,IEnumerable<CutFace> faces)
        {
            // The gallery module consists of convex beveled planks and fasteners.
            // Cap each connected solid separately, including corners cut by two planes.
            foreach(var face in faces) {
                var axis=Vector3.Cross(face.normal,Vector3.up).normalized;
                var vertical=Vector3.Cross(face.normal.normalized,axis);
                var points=face.edges.SelectMany(e=>new[]{e.a,e.b}).Distinct()
                    .OrderBy(p=>Vector3.Dot(p,axis)).ThenBy(p=>Vector3.Dot(p,vertical)).ToList();
                if(points.Count<3)continue;
                float Cross(Vector3 a,Vector3 b,Vector3 c)=>Vector3.Dot(Vector3.Cross(b-a,c-a),face.normal);
                var hull=new List<Vector3>();
                foreach(var p in points){while(hull.Count>=2 && Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=1e-9f)hull.RemoveAt(hull.Count-1);hull.Add(p);}
                int lower=hull.Count;
                for(int i=points.Count-2;i>=0;i--){var p=points[i];while(hull.Count>lower && Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=1e-9f)hull.RemoveAt(hull.Count-1);hull.Add(p);}
                hull.RemoveAt(hull.Count-1);
                if(hull.Count<3)continue;
                WritePolygon(buffer,hull.Select(p=>new Vertex {p=p,n=face.normal,uv=new Vector2(Vector3.Dot(p,axis),Vector3.Dot(p,vertical))}).ToList(),face.a,face.b,true);
            }
        }
        static List<Vertex> Clip(List<Vertex> input,Func<Vector3,float> plane)
        {
            var output=new List<Vertex>(); if(input.Count==0)return output;
            var a=input[input.Count-1]; float da=plane(a.p);
            foreach(var b in input) {
                float db=plane(b.p);
                if((da>=0)!=(db>=0))output.Add(Vertex.Lerp(a,b,da/(da-db)));
                if(db>=0)output.Add(b);
                a=b;da=db;
            }
            return output;
        }
        public static void Build(SandboxTrackGenerator track,List<SandboxTrackBaker.Sample> path,Transform parent,List<Mesh> owned)
        {
            var source=track.DeckModule;
            if(!source)throw new InvalidOperationException("Assign the Creek deck module before rebuilding.");
            var entry=source.transform.Find("Anchor_Entry");var exit=source.transform.Find("Anchor_Exit");
            if(!entry || !exit || Mathf.Abs(entry.localPosition.x)+Mathf.Abs(entry.localPosition.y)+Mathf.Abs(exit.localPosition.x)+Mathf.Abs(exit.localPosition.y)>.0001f || exit.localPosition.z<=entry.localPosition.z)
                throw new InvalidOperationException("Deck anchors must follow local +Z at surface height zero.");
            var filters=source.GetComponentsInChildren<MeshFilter>();
            if(filters.Length==0)throw new InvalidOperationException("Deck module contains no readable meshes.");
            var bounds=new Bounds();bool first=true;
            foreach(var f in filters) {
                if(!f.sharedMesh || !f.sharedMesh.isReadable)throw new InvalidOperationException("Deck source meshes must be readable.");
                var matrix=source.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;
                foreach(var v in f.sharedMesh.vertices){var p=matrix.MultiplyPoint3x4(v);if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}
            }
            float width=bounds.size.x, length=exit.localPosition.z-entry.localPosition.z;
            if(width<.0001f)throw new InvalidOperationException("Deck module has zero width.");
            float scale=track.DefaultWidth/width,pitch=length*scale,total=path[path.Count-1].Distance;
            int repeats=Mathf.CeilToInt(total/pitch);
            if(repeats>4096)throw new InvalidOperationException("Too many deck modules; widen or split this track.");
            var buffers=new List<Buffer>();var puzzles=SandboxPuzzleEditing.Puzzles(track).Where(p=>p.ReplacesRoad).ToArray();
            foreach(var f in filters) {
                var mesh=f.sharedMesh;var renderer=f.GetComponent<MeshRenderer>();
                if(!renderer || renderer.sharedMaterials.Length<mesh.subMeshCount || renderer.sharedMaterials.Any(m=>!m))throw new InvalidOperationException("Deck module has missing materials.");
                var matrix=source.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;
                var positions=mesh.vertices;var normals=mesh.normals;var uvs=mesh.uv;
                if(normals.Length!=positions.Length || uvs.Length!=positions.Length)throw new InvalidOperationException("Deck meshes need normals and UVs.");
                for(int sub=0;sub<mesh.subMeshCount;sub++) {
                    var material=renderer.sharedMaterials[sub];var buffer=buffers.FirstOrDefault(b=>b.material==material);
                    if(buffer==null){buffer=new Buffer {material=material};buffers.Add(buffer);}
                    var triangles=mesh.GetTriangles(sub);var solids=ConnectedSolids(positions,triangles);
                    for(int repeat=0;repeat<repeats;repeat++) {
                        var caps=new Dictionary<(int,int,int,int),CutFace>();
                        for(int tri=0;tri<triangles.Length;tri+=3) {
                            var polygon=new List<Vertex>();
                            for(int j=0;j<3;j++) {
                                int index=triangles[tri+j];var p=matrix.MultiplyPoint3x4(positions[index]);
                                polygon.Add(new Vertex {p=new Vector3((p.x-bounds.center.x)/width,p.y*scale,(p.z-entry.localPosition.z)*scale+repeat*pitch),n=matrix.inverse.transpose.MultiplyVector(normals[index]).normalized,uv=uvs[index]});
                            }
                            float min=polygon.Min(v=>v.p.z),max=polygon.Max(v=>v.p.z);
                            int lo=0,hi=path.Count-1;
                            while(lo+1<hi){int mid=(lo+hi)/2;if(path[mid].Distance<=min)lo=mid;else hi=mid;}
                            for(int i=lo+1;i<path.Count && path[i-1].Distance<=max;i++) {
                                var a=path[i-1];var b=path[i];
                                var cut=puzzles.FirstOrDefault(p=>(a.Distance+b.Distance)*.5f>p.Distance-p.Span*.5f && (a.Distance+b.Distance)*.5f<p.Distance+p.Span*.5f);
                                var left=cut?cut.RoadBands(a.Width):new[]{new Vector2(-a.Width*.5f,a.Width*.5f)};
                                var right=cut?cut.RoadBands(b.Width):new[]{new Vector2(-b.Width*.5f,b.Width*.5f)};
                                for(int band=0;band<left.Length;band++) {
                                    float T(Vector3 p)=>(p.z-a.Distance)/(b.Distance-a.Distance);
                                    Func<Vector3,float>[] planes={p=>p.z-a.Distance,p=>b.Distance-p.z,
                                        p=>p.x-Mathf.Lerp(left[band].x/a.Width,right[band].x/b.Width,T(p)),
                                        p=>Mathf.Lerp(left[band].y/a.Width,right[band].y/b.Width,T(p))-p.x};
                                    var clipped=polygon;
                                    foreach(var plane in planes)clipped=Clip(clipped,plane);
                                    if(clipped.Count<3)continue;
                                    WritePolygon(buffer,clipped,a,b);
                                    for(int plane=0;plane<4;plane++) {
                                        // Sampling divides the surface for bending; only exposed ends need caps.
                                        if(plane<2) {
                                            float distance=plane==0?a.Distance:b.Distance;
                                            bool end=distance<.0001f || total-distance<.0001f;
                                            if(!end && !puzzles.Any(p=>Mathf.Abs(distance-(p.Distance-p.Span*.5f))<.0001f || Mathf.Abs(distance-(p.Distance+p.Span*.5f))<.0001f))continue;
                                        }
                                        if(!polygon.Any(v=>planes[plane](v.p)<-1e-6f))continue;
                                        var key=(i,band,plane,solids[tri/3]);
                                        if(!caps.TryGetValue(key,out var face)) {
                                            float slope=(plane==2?right[band].x/b.Width-left[band].x/a.Width:right[band].y/b.Width-left[band].y/a.Width)/(b.Distance-a.Distance);
                                            face=new CutFace {a=a,b=b,normal=plane==0?Vector3.back:plane==1?Vector3.forward:plane==2?new Vector3(-1,0,slope):new Vector3(1,0,-slope)};
                                            caps.Add(key,face);
                                        }
                                        for(int j=0;j<clipped.Count;j++) {
                                            var p=clipped[j].p;var q=clipped[(j+1)%clipped.Count].p;
                                            if(Mathf.Abs(planes[plane](p))<1e-5f && Mathf.Abs(planes[plane](q))<1e-5f && (p-q).sqrMagnitude>1e-12f)face.edges.Add((p,q));
                                        }
                                    }
                                }
                            }
                        }
                        CloseCuts(buffer,caps.Values);
                    }
                }
            }
            for(int i=0;i<buffers.Count;i++) {
                var b=buffers[i];if(b.indices.Count==0)continue;
                var mesh=new Mesh {name="CreekDeck_"+i,indexFormat=IndexFormat.UInt32};owned.Add(mesh);
                mesh.SetVertices(b.vertices);mesh.SetNormals(b.normals);mesh.SetUVs(0,b.uv);mesh.SetTriangles(b.indices,0);mesh.RecalculateBounds();mesh.RecalculateTangents();
                var go=new GameObject(mesh.name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=b.material;
            }
        }
    }
}
