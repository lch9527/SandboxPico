using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PhysicalAssembly.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace PhysicalAssembly.Tests
{
    public class FactoryTrackSmoothingTests
    {
        Scene scene;
        SandboxTrackGenerator track;
        [SetUp] public void Setup()
        {
            scene=EditorSceneManager.NewPreviewScene();
            var go=new GameObject("Smoothing test"); SceneManager.MoveGameObjectToScene(go,scene);
            track=SandboxFactoryAuthoring.InitializeTrack(go); track.Supports=false;
            track.Curve.Spline=new Spline();
            foreach(var p in new[]{new float3(0,0,0),new float3(0,1,5),new float3(4,1.5f,9),new float3(9,1.5f,9)})
                track.Curve.Spline.Add(new BezierKnot(p),TangentMode.Linear);
        }
        [TearDown] public void Cleanup()
        {
            if(track && track.Generated) foreach(var f in track.Generated.GetComponentsInChildren<MeshFilter>())
                if(f.sharedMesh && !AssetDatabase.Contains(f.sharedMesh)) Object.DestroyImmediate(f.sharedMesh);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        [Test] public void SmoothingPreservesPointsAndWidthsAndIsIdempotent()
        {
            var positions=track.Curve.Spline.Select(k=>k.Position).ToArray();
            var widths=Enumerable.Range(0,positions.Length).Select(track.KnotWidth).ToArray();
            SandboxTrackBaker.SmoothTangents(track);
            var first=track.Curve.Spline.ToArray();
            SandboxTrackBaker.SmoothTangents(track);
            for(int i=0;i<positions.Length;i++) {
                Assert.AreEqual(positions[i],track.Curve.Spline[i].Position);
                Assert.AreEqual(widths[i],track.KnotWidth(i));
                Assert.That(math.distance(first[i].TangentIn,track.Curve.Spline[i].TangentIn),Is.LessThan(.00001f));
                Assert.That(math.distance(first[i].TangentOut,track.Curve.Spline[i].TangentOut),Is.LessThan(.00001f));
                Assert.That(Quaternion.Angle(first[i].Rotation,track.Curve.Spline[i].Rotation),Is.LessThan(.01f));
            }
            var path=SandboxTrackBaker.SamplePath(track);
            for(int i=1;i<path.Count;i++) Assert.LessOrEqual(Vector3.Angle(path[i-1].Forward,path[i].Forward),2.01f);
        }
        [Test] public void SurfaceNormalsJoinAcrossSegmentsButKeepThicknessCornersSharp()
        {
            SandboxTrackBaker.SmoothTangents(track);
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            var road=track.Generated.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Road");
            Assert.AreSame(road.sharedMesh,road.GetComponent<MeshCollider>().sharedMesh);
            var vertices=road.sharedMesh.vertices; var normals=road.sharedMesh.normals;
            var top=new Dictionary<Vector3,Vector3>(); int joined=0,sharp=0;
            for(int i=0;i<vertices.Length;i++) if(normals[i].y>.7f) {
                if(top.TryGetValue(vertices[i],out var other)) { Assert.Greater(Vector3.Dot(other,normals[i]),.99999f); joined++; }
                else top.Add(vertices[i],normals[i]);
            }
            for(int i=0;i<vertices.Length;i++) if(Mathf.Abs(normals[i].y)<.1f && top.TryGetValue(vertices[i],out var up))
                if(Mathf.Abs(Vector3.Dot(normals[i],up))<.2f) sharp++;
            Assert.Greater(joined,10,"There must be shared smooth segment seams.");
            Assert.Greater(sharp,10,"Road thickness must keep distinct side/top normals.");
        }
        [Test] public void DisablingAutoSmoothRetainsHandAuthoredTangents()
        {
            track.AutoSmoothOnRebuild=false;
            var before=track.Curve.Spline.ToArray();
            Assert.IsTrue(SandboxTrackBaker.RebuildFromInspector(track),track.ValidationError);
            for(int i=0;i<before.Length;i++) Assert.AreEqual(before[i],track.Curve.Spline[i]);
        }
    }
}
