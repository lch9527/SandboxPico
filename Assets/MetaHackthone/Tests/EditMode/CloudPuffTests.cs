using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PhysicalAssembly.Tests
{
    public sealed class CloudPuffTests
    {
        const string BodyShader = "Creek Diorama/Cloud Puff", ShellShader = "Creek Diorama/Cloud Shell";

        [TestCase(BodyShader)] [TestCase(ShellShader)]
        public void CloudShaderIsSupported(string name)
        {
            var shader = Shader.Find(name);
            Assert.IsNotNull(shader);
            Assert.IsTrue(shader.isSupported);
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void CampaignLevelUsesWorldFixedCloudMeshes(int level)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab");
            Assert.IsNotNull(prefab);
            var all = prefab.GetComponentsInChildren<Transform>(true);
            var puffs = all.Where(t => t.name.StartsWith("Cloud puff ")).ToArray();
            Assert.AreEqual(10, puffs.Length);
            foreach (var puff in puffs)
            {
                Assert.IsTrue(puff.gameObject.activeSelf, puff.name);
                Assert.IsNull(puff.GetComponent<Collider>(), puff.name);
                var mesh = puff.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh, puff.name);
                Assert.AreEqual(2, mesh.subMeshCount, puff.name);
                var materials = puff.GetComponent<Renderer>().sharedMaterials;
                Assert.AreEqual(2, materials.Length, puff.name);
                Assert.AreEqual(BodyShader, materials[0].shader.name, puff.name);
                Assert.AreEqual(ShellShader, materials[1].shader.name, puff.name);
                foreach (var material in materials)
                    Assert.IsInstanceOf<Texture3D>(material.GetTexture("_NoiseTex"), puff.name);
            }
            // The ray-marched volumes are gone and the head-aligned cards stay only as an inactive fallback.
            Assert.IsFalse(all.Any(t => t.name.StartsWith("Cloud volume ")));
            Assert.IsFalse(all.Any(t => t.name.StartsWith("Cloud layer ") && t.gameObject.activeSelf));
        }
    }
}
