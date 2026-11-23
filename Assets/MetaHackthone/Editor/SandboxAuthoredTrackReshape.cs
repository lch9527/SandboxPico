using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    // Authored levels keep their saved road, rails and puzzles: the track baker refuses to regenerate them.
    // This reshapes only the start of such a track. The leading control points move, that part of the road
    // collider and deck art is rebaked with the normal track baker on a scratch copy, and the result is
    // spliced onto the saved meshes at the first unchanged control point. Everything after that point,
    // including hand-edited rails and puzzle objects, is left exactly as saved.
    public static class SandboxAuthoredTrackReshape
    {
        // points: new track-local positions for control points 0..points.Length-1.
        // The control point after them is the join: it and everything beyond it stay unchanged.
        // cutAt: optional distance along the current track, at or after the join, where saved and rebaked
        // meshes are spliced. Put it in a gap between deck planks so no plank is split; the curve is
        // unchanged beyond the join, so both meshes agree there.
        public static string ReshapeStart(SandboxTrackGenerator track, Vector3[] points, float cutAt=-1)
        {
            if(Application.isPlaying) throw new InvalidOperationException("Stop Play first.");
            if(!track || !track.AuthoredObjects) throw new InvalidOperationException("Use Rebuild Road and Supports for generated tracks.");
            if(!track.Generated) throw new InvalidOperationException("The track has no saved road.");
            var spline=track.Curve.Spline; int join=points.Length;
            if(join<1 || join>spline.Count-2) throw new InvalidOperationException("Keep at least two unchanged control points after the reshaped start.");
            Vector3 joinPoint=spline[join].Position;
            var oldPath=SandboxTrackBaker.SamplePath(track);
            float oldJoin=JoinDistance(oldPath,joinPoint);

            var filters=track.Generated.GetComponentsInChildren<MeshFilter>(true)
                .Where(f=>f.transform.parent==track.Generated.transform && (f.name=="Road" || f.name.StartsWith("CreekDeck_",StringComparison.Ordinal))).ToArray();
            foreach(var f in filters) {
                var m=f.sharedMesh;
                if(!m || !m.isReadable || m.subMeshCount!=1 || m.colors.Length>0 || m.uv2.Length>0) throw new InvalidOperationException(f.name+": unsupported saved mesh layout.");
                if(!(track.transform.worldToLocalMatrix*f.transform.localToWorldMatrix).isIdentity) throw new InvalidOperationException(f.name+" was moved; reset its transform before reshaping.");
            }

            var owned=new List<Object>();
            var scratchObject=new GameObject("Reshape scratch track") { hideFlags=HideFlags.DontSave };
            owned.Add(scratchObject);
            try {
                SceneManager.MoveGameObjectToScene(scratchObject,track.gameObject.scene);
                scratchObject.transform.SetPositionAndRotation(track.transform.position,track.transform.rotation);
                scratchObject.transform.localScale=track.transform.lossyScale;
                var scratch=scratchObject.AddComponent<SandboxTrackGenerator>();
                EditorUtility.CopySerialized(track.Curve,scratch.Curve);
                EditorUtility.CopySerialized(track,scratch);
                // Rails, supports and puzzle cut-outs all lie beyond the join and stay as saved.
                scratch.Generated=null; scratch.BakedSignature=""; scratch.Guardrails=false; scratch.Supports=false; scratch.LiveRebuild=false;
                var s=scratch.Curve.Spline;
                var saved=new BezierKnot[s.Count]; var modes=new TangentMode[s.Count];
                for(int i=0;i<s.Count;i++) { saved[i]=s[i]; modes[i]=s.GetTangentMode(i); }
                for(int i=0;i<join;i++) { var k=s[i]; k.Position=points[i]; s[i]=k; }
                SandboxTrackBaker.SmoothTangents(scratch);
                for(int i=join;i<s.Count;i++) { s.SetTangentMode(i,modes[i]); s[i]=saved[i]; }
                if(!SandboxTrackBaker.Rebuild(scratch,true)) throw new InvalidOperationException(scratch.ValidationError);
                foreach(var f in scratch.Generated.GetComponentsInChildren<MeshFilter>(true)) owned.Add(f.sharedMesh);
                var newPath=SandboxTrackBaker.SamplePath(scratch);
                float newJoin=JoinDistance(newPath,joinPoint), delta=newJoin-oldJoin;
                if(cutAt>=0 && cutAt<oldJoin) throw new InvalidOperationException("The splice must be at or after the join.");
                float oldCut=cutAt>=0?cutAt:oldJoin, newCut=oldCut+delta;

                var oldSamples=oldPath.Select(p=>p.Position).ToArray(); var newSamples=newPath.Select(p=>p.Position).ToArray();
                var oldCum=oldPath.Select(p=>p.Distance).ToArray(); var newCum=newPath.Select(p=>p.Distance).ToArray();
                var pairs=new List<(MeshFilter target,Mesh source)>();
                foreach(var f in filters) {
                    var match=scratch.Generated.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(n=>f.name=="Road"?n.name=="Road":
                        n.name.StartsWith("CreekDeck_",StringComparison.Ordinal) && n.GetComponent<Renderer>().sharedMaterial==f.GetComponent<Renderer>().sharedMaterial);
                    if(!match) throw new InvalidOperationException("The baker produced no replacement for "+f.name+".");
                    pairs.Add((f,match.sharedMesh));
                }
                var report=new System.Text.StringBuilder();
                foreach(var (target,source) in pairs) {
                    var mesh=target.sharedMesh;
                    var vertices=new List<Vector3>(); var normals=new List<Vector3>(); var tangents=new List<Vector4>(); var uv=new List<Vector2>(); var triangles=new List<int>();
                    // The driving collider splits at the join, a sample boundary in both meshes, so no seam
                    // opens in the road surface. Deck art splits at the plank gap.
                    bool road=target.name=="Road";
                    float keepNew=road?newJoin:newCut, keepOld=road?oldJoin:oldCut;
                    int fresh=Append(source,p=>Along(newSamples,newCum,p)<=keepNew,vertices,normals,tangents,uv,triangles);
                    int kept=Append(mesh,p=>Along(oldSamples,oldCum,p)>=keepOld,vertices,normals,tangents,uv,triangles);
                    if(fresh==0 || kept==0) throw new InvalidOperationException(target.name+": splice lost one side of the join.");
                    mesh.Clear(); mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTangents(tangents); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
                    mesh.RecalculateBounds(); mesh.UploadMeshData(false); EditorUtility.SetDirty(mesh);
                    var collider=target.GetComponent<MeshCollider>();
                    if(collider) { collider.sharedMesh=null; collider.sharedMesh=mesh; EditorUtility.SetDirty(collider); }
                    report.Append($"{target.name}: {fresh} new + {kept} kept triangles; ");
                }

                Undo.RecordObject(track.Curve,"Reshape track start"); Undo.RecordObject(track,"Reshape track start");
                for(int i=0;i<join;i++) { spline.SetTangentMode(i,s.GetTangentMode(i)); spline[i]=s[i]; }
                track.Samples=newSamples; track.BakedSignature=SandboxTrackBaker.Signature(track); track.ValidationError="";
                EditorUtility.SetDirty(track.Curve); EditorUtility.SetDirty(track);

                // Saved puzzles keep their places on the unchanged road, so their distances follow the new start length.
                foreach(var puzzle in track.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SandboxPuzzle>(true)).Where(p=>p.Track==track)) {
                    if(puzzle.Distance<oldJoin) throw new InvalidOperationException(puzzle.name+" sits on the reshaped start.");
                    Undo.RecordObject(puzzle,"Reshape track start");
                    puzzle.Distance+=delta;
                    if(puzzle is SandboxDropBridge drop && drop.TargetDistance>=0) drop.TargetDistance+=delta;
                    EditorUtility.SetDirty(puzzle);
                }
                var factory=track.GetComponentInParent<SandboxFactoryLevel>(true);
                if(factory && factory.BindStart && factory.Start && factory.StartBinding.Track==track) {
                    var binding=factory.StartBinding;
                    var sample=SandboxPuzzleEditing.At(newPath,binding.Distance); var q=Quaternion.LookRotation(sample.Forward,sample.Up);
                    Undo.RecordObject(factory.Start,"Reshape track start");
                    factory.Start.SetPositionAndRotation(track.transform.TransformPoint(sample.Position+q*binding.Offset),track.transform.rotation*q*Quaternion.Euler(binding.Rotation));
                    EditorUtility.SetDirty(factory.Start);
                }
                EditorSceneManager.MarkSceneDirty(track.gameObject.scene);
                return FormattableString.Invariant($"join at point {join+1}: start length {oldJoin:F3} -> {newJoin:F3} (delta {delta:F3}); {report}");
            } finally {
                foreach(var o in owned) if(o && !AssetDatabase.Contains(o)) Object.DestroyImmediate(o);
            }
        }

        static float JoinDistance(List<SandboxTrackBaker.Sample> path,Vector3 point)
        {
            var nearest=path.OrderBy(p=>(p.Position-point).sqrMagnitude).First();
            if((nearest.Position-point).sqrMagnitude>1e-8f) throw new InvalidOperationException("The join control point is not on a sample boundary.");
            return nearest.Distance;
        }

        // Distance along the sampled centre line nearest to a track-local point.
        static float Along(Vector3[] samples,float[] cumulative,Vector3 p)
        {
            float best=float.PositiveInfinity,distance=0;
            for(int i=0;i<samples.Length-1;i++) {
                var d=samples[i+1]-samples[i];
                float u=Mathf.Clamp01(Vector3.Dot(p-samples[i],d)/d.sqrMagnitude);
                float error=(p-(samples[i]+u*d)).sqrMagnitude;
                if(error<best) { best=error; distance=Mathf.Lerp(cumulative[i],cumulative[i+1],u); }
            }
            return distance;
        }

        // Copies the triangles whose centre passes the test, with only the vertices they use.
        static int Append(Mesh mesh,Func<Vector3,bool> keep,List<Vector3> vertices,List<Vector3> normals,List<Vector4> tangents,List<Vector2> uv,List<int> triangles)
        {
            var v=mesh.vertices; var n=mesh.normals; var t=mesh.tangents; var u=mesh.uv; var tri=mesh.triangles;
            if(n.Length!=v.Length || t.Length!=v.Length || u.Length!=v.Length) throw new InvalidOperationException(mesh.name+": normals, tangents and UVs are required.");
            var map=new Dictionary<int,int>(); int count=0;
            for(int i=0;i<tri.Length;i+=3) {
                if(!keep((v[tri[i]]+v[tri[i+1]]+v[tri[i+2]])/3f)) continue;
                for(int j=0;j<3;j++) {
                    int index=tri[i+j];
                    if(!map.TryGetValue(index,out int mapped)) {
                        mapped=vertices.Count; map.Add(index,mapped);
                        vertices.Add(v[index]); normals.Add(n[index]); tangents.Add(t[index]); uv.Add(u[index]);
                    }
                    triangles.Add(mapped);
                }
                count++;
            }
            return count;
        }
    }
}
