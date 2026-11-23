using System;
using System.Linq;
using NUnit.Framework;
using PhysicalAssembly.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Tests
{
    public sealed class FactoryCreekDeckTests
    {
        SandboxTrackGenerator track;
        string folder;
        [SetUp] public void SetUp()
        {
            SandboxTrackEditing.Suspended=true;
            track=SandboxFactoryAuthoring.InitializeTrack(new GameObject("Creek deck test"));
            var spline=new Spline();
            spline.Add(new BezierKnot(float3.zero));spline.Add(new BezierKnot(new float3(0,0,17.3f)));
            track.Curve.Spline=spline;track.Supports=false;track.Guardrails=false;
            folder="Assets/CreekDeckTest-"+Guid.NewGuid().ToString("N");
        }
        [TearDown] public void TearDown()
        {
            foreach(var puzzle in SandboxPuzzleEditing.Puzzles(track))Object.DestroyImmediate(puzzle.gameObject);
            foreach(var mesh in track.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).Distinct())
                if(mesh && !AssetDatabase.Contains(mesh))Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(track.gameObject);
            if(AssetDatabase.IsValidFolder(folder))AssetDatabase.DeleteAsset(folder);
            SandboxTrackEditing.Suspended=false;
        }
        MeshFilter[] Art()=>track.Generated.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("CreekDeck_")).ToArray();
        void Rebuild()=>Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
        bool HasCap(Vector3 normal,float z)
        {
            return Art().Any(f=>f.sharedMesh.vertices.Zip(f.sharedMesh.normals,(v,n)=>Mathf.Abs(v.z-z)<.001f && Vector3.Dot(n,normal)>.99f).Any(x=>x));
        }
        [Test] public void ArtKeepsSourceMaterialsAndSmoothCollisionAndClipsFinalModule()
        {
            track.DeckStyle=SandboxDeckStyle.Plain;Rebuild();
            var plain=track.Generated.GetComponentInChildren<MeshCollider>().sharedMesh.vertices;
            track.DeckStyle=SandboxDeckStyle.CreekDeck;Rebuild();
            CollectionAssert.AreEqual(plain,track.Generated.GetComponentInChildren<MeshCollider>().sharedMesh.vertices);
            Assert.IsFalse(track.Generated.transform.Find("Road").GetComponent<MeshRenderer>().enabled);
            Assert.AreEqual(6,Art().Length);
            Assert.IsTrue(Art().All(f=>!f.GetComponent<Collider>()));
            var materials=track.DeckModule.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Distinct();
            CollectionAssert.AreEquivalent(materials,Art().Select(f=>f.GetComponent<MeshRenderer>().sharedMaterial));
            Assert.That(Art().SelectMany(f=>f.sharedMesh.vertices).Max(v=>v.z),Is.EqualTo(17.3f).Within(.001f));
            Assert.IsTrue(HasCap(Vector3.forward,17.3f),"Trimmed final plank needs a closed end.");
        }
        [Test] public void PartialAndFullPuzzleCutsMoveAndRestoreArt()
        {
            var crab=new GameObject("Crab cut").AddComponent<SandboxCrabBridge>();crab.Track=track;crab.Distance=8;crab.Length=3;crab.HoleWidth=1.2f;crab.LeftLaneWidth=crab.RightLaneWidth=.6f;
            Rebuild();
            Assert.IsFalse(Art().SelectMany(f=>f.sharedMesh.vertices).Any(v=>v.z>6.501f && v.z<9.499f && Mathf.Abs(v.x)<.599f));
            foreach(int side in new[]{-1,1})
                Assert.IsTrue(Art().Any(f=>f.sharedMesh.vertices.Zip(f.sharedMesh.normals,(v,n)=>v.z>6.501f && v.z<9.499f && Mathf.Abs(v.x-side*.6f)<.001f && n.x*side<-.99f).Any(x=>x)),"Side cuts need caps facing the hole.");
            crab.enabled=false;Rebuild();
            Assert.IsTrue(Art().SelectMany(f=>f.sharedMesh.vertices).Any(v=>v.z>6.6f && v.z<9.4f && Mathf.Abs(v.x)<.59f));
            Object.DestroyImmediate(crab.gameObject);
            var bridge=new GameObject("Drop cut").AddComponent<SandboxDropBridge>();bridge.Track=track;bridge.Distance=10;bridge.Length=3;bridge.TargetDistance=2;
            Rebuild();
            Assert.IsFalse(Art().SelectMany(f=>f.sharedMesh.vertices).Any(v=>v.z>8.501f && v.z<11.499f));
            Assert.IsTrue(HasCap(Vector3.forward,8.5f));Assert.IsTrue(HasCap(Vector3.back,11.5f));
            bridge.Distance=14;Rebuild();
            Assert.IsTrue(Art().SelectMany(f=>f.sharedMesh.vertices).Any(v=>v.z>9 && v.z<11));
            Assert.IsFalse(Art().SelectMany(f=>f.sharedMesh.vertices).Any(v=>v.z>12.501f && v.z<15.499f));
        }
        [Test] public void RenderOnlyMeshAssetsKeepIdentityAcrossRepeatedSave()
        {
            Rebuild();SandboxTrackBaker.Persist(track,folder,"test");
            var saved=Art().Select(f=>f.sharedMesh).ToArray();
            var ids=saved.Select(m=>GlobalObjectId.GetGlobalObjectIdSlow(m).ToString()).ToArray();
            track.SetKnotWidth(1,3.2f);Rebuild();SandboxTrackBaker.Persist(track,folder,"test");
            CollectionAssert.AreEqual(saved,Art().Select(f=>f.sharedMesh));
            CollectionAssert.AreEqual(ids,Art().Select(f=>GlobalObjectId.GetGlobalObjectIdSlow(f.sharedMesh).ToString()));
            Assert.IsTrue(Art().All(f=>AssetDatabase.Contains(f.sharedMesh) && !f.GetComponent<Collider>()));
        }
        [Test] public void InvalidSourceKeepsLastValidGeometryAndPlainCanBeRestored()
        {
            Rebuild();var previous=track.Generated;
            track.DeckModule=null;Assert.IsFalse(SandboxTrackBaker.Rebuild(track,true));
            Assert.AreSame(previous,track.Generated);Assert.IsTrue(previous.activeSelf);Assert.IsNotEmpty(track.ValidationError);
            track.DeckStyle=SandboxDeckStyle.Plain;Rebuild();Assert.IsEmpty(Art());
            Assert.IsTrue(track.Generated.transform.Find("Road").GetComponent<MeshRenderer>().enabled);
        }
        [Test] public void CurvedSlopedVariableWidthRoadProducesFiniteArtWithinSampleBounds()
        {
            var knot=track.Curve.Spline[1];knot.Position=new float3(6,4,15);track.Curve.Spline[1]=knot;
            track.Curve.Spline.SetTangentMode(TangentMode.AutoSmooth);track.SetKnotWidth(0,2);track.SetKnotWidth(1,3.5f);
            Rebuild();var samples=SandboxTrackBaker.SamplePath(track);
            Assert.IsTrue(Art().SelectMany(f=>f.sharedMesh.vertices).All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)));
            Assert.IsTrue(Art().SelectMany(f=>f.sharedMesh.vertices).All(v=>samples.Any(s=>Vector3.Distance(v,s.Position)<s.Width*.6f+track.SampleSpacing)));
        }
    }
}
