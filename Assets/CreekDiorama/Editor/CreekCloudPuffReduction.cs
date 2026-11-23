using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Lowers the triangle count of the CloudPuff A-D meshes without changing their shape.
// The authored puffs are level-4 icospheres (2,562 vertices, 5,120 triangles, drawn once for the body and
// once for the haze shell) whose vertices were pushed out along their directions and then flattened
// underneath. The flattening folds the surface as seen from the origin, so it cannot be re-sampled by
// casting rays at the finished mesh. Instead each vertex of a coarser geodesic sphere is located in the
// authored sphere's own triangle (by direction) and takes that triangle's positions and vertex colours
// (lobe phase, top mask, crease depth) by interpolation: the same surface, sampled more thinly.
// The mesh assets are overwritten in place, so every scene, prefab and material keeps its reference.
public static class CreekCloudPuffReduction
{
    const string Folder="Assets/CreekDiorama/Meshes/Effects/";
    // Geodesic frequency: 20*n*n triangles. 16 is the authored density; 11 gives 2,420 triangles (-53%).
    public const int Frequency=11;

    [MenuItem("MetaHackthone/Creek Art/Reduce cloud puff triangles")]
    public static void Reduce()
    {
        Sphere(Frequency,out var directions,out var triangles);
        // The authored directions and the generator, both private to the authoring tool.
        var authoring=typeof(CreekDiorama.Editor.CreekCloudPuffAuthoring); var hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
        var arguments=new object[]{4,null,null}; authoring.GetMethod("Icosphere",hidden).Invoke(null,arguments);
        var authored=(List<Vector3>)arguments[1]; var authoredTriangles=(List<int>)arguments[2];
        int changed=0; var report=new System.Text.StringBuilder();
        for(int variant=0;variant<4;variant++) {
            char letter=(char)('A'+variant);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"CloudPuff "+letter+".asset");
            if(!mesh) { report.Append($" {letter}: missing;"); continue; }
            // Always start from the authored puff (the generator is seeded, so this is exact), then thin it.
            mesh=(Mesh)authoring.GetMethod("Puff",hidden).Invoke(null,new object[]{variant});
            if(mesh.vertexCount!=authored.Count) throw new System.InvalidOperationException($"CloudPuff {letter}: generator returned {mesh.vertexCount} vertices, expected {authored.Count}.");
            var source=mesh.vertices; var colours=mesh.colors; var bounds=mesh.bounds; int before=mesh.GetTriangles(0).Length/3;
            var vertices=new Vector3[directions.Count]; var tints=new Color[directions.Count];
            for(int v=0;v<directions.Count;v++) {
                var d=directions[v]; bool hit=false;
                for(int t=0;t<authoredTriangles.Count && !hit;t+=3) {
                    int ia=authoredTriangles[t], ib=authoredTriangles[t+1], ic=authoredTriangles[t+2];
                    Vector3 a=authored[ia], b=authored[ib], c=authored[ic];
                    if(Vector3.Dot(d,a+b+c)<2.5f) continue;     // far from this triangle
                    // Ray from the origin along d against the authored sphere's triangle abc (Moller-Trumbore).
                    Vector3 e1=b-a, e2=c-a, p=Vector3.Cross(d,e2); float det=Vector3.Dot(e1,p);
                    if(Mathf.Abs(det)<1e-12f) continue;
                    float inv=1/det; Vector3 s=-a; float u=Vector3.Dot(s,p)*inv; if(u<-1e-4f || u>1+1e-4f) continue;
                    Vector3 q=Vector3.Cross(s,e1); float w=Vector3.Dot(d,q)*inv; if(w<-1e-4f || u+w>1+1e-4f) continue;
                    if(Vector3.Dot(e2,q)*inv<=0) continue;
                    vertices[v]=source[ia]*(1-u-w)+source[ib]*u+source[ic]*w;
                    tints[v]=colours[ia]*(1-u-w)+colours[ib]*u+colours[ic]*w;
                    hit=true;
                }
                if(!hit) throw new System.InvalidOperationException($"CloudPuff {letter}: direction {d} is in no authored triangle.");
            }
            // Rewrite the asset's own mesh rather than copying another mesh over it: after a copy that changes
            // the vertex count the editor kept drawing part of the old index buffer (whole faces went missing).
            mesh.Clear();
            mesh.SetVertices(vertices); mesh.SetColors(tints);
            mesh.subMeshCount=2; mesh.SetTriangles(triangles,0); mesh.SetTriangles(triangles,1);
            mesh.RecalculateNormals();
            // Keep the triangles facing out, whatever the generator's winding was.
            var normals=mesh.normals; float outward=0; for(int i=0;i<normals.Length;i+=37) outward+=Vector3.Dot(normals[i],vertices[i]);
            if(outward<0) { var flipped=new List<int>(triangles); for(int i=0;i<flipped.Count;i+=3) (flipped[i+1],flipped[i+2])=(flipped[i+2],flipped[i+1]); mesh.SetTriangles(flipped,0); mesh.SetTriangles(flipped,1); mesh.RecalculateNormals(); }
            mesh.bounds=bounds;      // room for displacement, drift and the haze shell, as authored
            mesh.UploadMeshData(false); EditorUtility.SetDirty(mesh);
            changed++; report.Append($" {letter}: {before} -> {triangles.Count/3} triangles;");
        }
        AssetDatabase.SaveAssets();
        foreach(char letter in "ABCD") AssetDatabase.ImportAsset(Folder+"CloudPuff "+letter+".asset",ImportAssetOptions.ForceUpdate);
        Debug.Log($"CLOUD_PUFF_REDUCTION changed {changed}:{report}");
    }

    // A geodesic sphere: each icosahedron face split into n*n triangles, vertices welded and pushed to radius 1.
    static void Sphere(int n,out List<Vector3> vertices,out List<int> triangles)
    {
        float t=(1+Mathf.Sqrt(5))/2;
        var corners=new[]{new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1)};
        var faces=new[]{0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
        vertices=new List<Vector3>(); triangles=new List<int>(); var welded=new Dictionary<Vector3Int,int>();
        for(int f=0;f<faces.Length;f+=3) {
            Vector3 a=corners[faces[f]].normalized, b=corners[faces[f+1]].normalized, c=corners[faces[f+2]].normalized;
            var grid=new int[n+1,n+1];
            for(int i=0;i<=n;i++) for(int j=0;j<=n-i;j++) {
                var point=(a*(n-i-j)+b*i+c*j).normalized;
                var key=Vector3Int.RoundToInt(point*20000);
                if(!welded.TryGetValue(key,out int index)) { index=vertices.Count; vertices.Add(point); welded.Add(key,index); }
                grid[i,j]=index;
            }
            for(int i=0;i<n;i++) for(int j=0;j<n-i;j++) {
                triangles.Add(grid[i,j]); triangles.Add(grid[i+1,j]); triangles.Add(grid[i,j+1]);
                if(i+j<n-1) { triangles.Add(grid[i+1,j]); triangles.Add(grid[i+1,j+1]); triangles.Add(grid[i,j+1]); }
            }
        }
    }
}
