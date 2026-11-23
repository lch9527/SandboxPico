using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using PhysicalAssembly.Editor;

namespace PhysicalAssembly.Tests
{
    public sealed class SceneryLodRepairTests
    {
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void CurrentBakedMeadowAndCanopyHaveReducedLeafAndFlowerMeshes(int level)
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab");
            var catalog=Resources.Load<SceneryDetailCatalog>("SceneryDetailCatalog");
            var entries=catalog.Foliage.ToDictionary(e=>e.Source);
            long before=0,after=0;int matched=0;
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool active=renderer.enabled;
                for(var t=renderer.transform;t;t=t.parent)active &= t.gameObject.activeSelf;
                if(!active || !SceneryAAAuthoring.IsDetailMaterial(renderer.sharedMaterial))continue;
                // The separate sculpted canopy volumes are already coarse geometry;
                // the repaired catalog targets the fine leaves and flower batches.
                if(renderer.sharedMaterial.name.Contains(" sculpted"))continue;
                string path=AnimationUtility.CalculateTransformPath(renderer.transform,root.transform);
                if(!path.Contains("/Render batches/Moss ivy ferns and meadow/") && !path.Contains("/Render batches/Layered canopy/"))continue;
                var source=renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsTrue(entries.TryGetValue(source,out var entry),path);
                Assert.Less(entry.Filtered.GetIndexCount(0),source.GetIndexCount(0),path);
                Assert.Greater(entry.Filtered.vertexCount,0,path);
                for(int channel=3;channel<=4;channel++)
                {
                    var originalUv=new System.Collections.Generic.List<Vector4>();source.GetUVs(channel,originalUv);
                    if(originalUv.Count==0)continue;
                    var filteredUv=new System.Collections.Generic.List<Vector4>();entry.Filtered.GetUVs(channel,filteredUv);
                    Assert.AreEqual(entry.Filtered.vertexCount,filteredUv.Count,"Wind channel missing: "+path);
                    var values=new System.Collections.Generic.HashSet<Vector4>(originalUv);
                    Assert.IsTrue(filteredUv.All(v=>values.Contains(v)),"Wind anchors changed: "+path);
                }
                before+=source.GetIndexCount(0)/3;after+=entry.Filtered.GetIndexCount(0)/3;matched++;
            }
            Assert.GreaterOrEqual(matched,9,"Current leaf AND flower batches must be covered.");
            Assert.Less(after,before*.7,"The distant foliage should remove at least 30% of geometry.");
        }

        [Test] public void DaisyCentresKeepAllElevenFlowersAtTwelveTrianglesEach()
        {
            var source=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/CreekDiorama/Meshes/Botanical/Flowers_Daisies_2.asset");
            var vertices=source.vertices;var indices=source.triangles;
            var filtered=SceneryAAAuthoring.FilterFlowerCentres(source);
            try
            {
                Assert.AreEqual(11*12,filtered.triangles.Length/3);
                Assert.Less(Vector3.Distance(source.bounds.center,filtered.bounds.center),.00001f);
                Assert.Less(Vector3.Distance(source.bounds.size,filtered.bounds.size),.00001f);
                CollectionAssert.AreEqual(vertices,source.vertices);
                CollectionAssert.AreEqual(indices,source.triangles);
            }
            finally { Object.DestroyImmediate(filtered); }
        }

        [Test] public void PetalFansKeepTheirOutlineAndLoseInternalTriangles()
        {
            var source=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/CreekDiorama/Meshes/Botanical/Flowers_Daisies_0.asset");
            var filtered=SceneryAAAuthoring.SimplifyLeafFans(source);
            try
            {
                Assert.AreEqual(11*7*4,filtered.triangles.Length/3);
                Assert.Less(Vector3.Distance(source.bounds.center,filtered.bounds.center),.001f);
                Assert.Less(Vector3.Distance(source.bounds.size,filtered.bounds.size),.001f);
                Assert.AreEqual(filtered.vertexCount,filtered.normals.Length);
            }
            finally { Object.DestroyImmediate(filtered); }
        }

        [Test] public void SavedInputsMatchCatalogAndAllVariantsStillExist()
        {
            var catalog=Resources.Load<SceneryDetailCatalog>("SceneryDetailCatalog");
            Assert.AreEqual(SceneryAAAuthoring.Fingerprint(),catalog.AuthoringFingerprint);
            Assert.IsTrue(catalog.Foliage.All(e=>e.Source && e.Filtered));
            Assert.AreEqual(catalog.Foliage.Length,catalog.Foliage.Select(e=>e.Source).Distinct().Count());
        }
    }
}
