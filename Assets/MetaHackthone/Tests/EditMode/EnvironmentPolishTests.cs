using System.Linq;
using NUnit.Framework;
using PhysicalAssembly.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Tests
{
    public sealed class EnvironmentPolishTests
    {
        static SandboxFactoryLevel Level(int n) => AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>($"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab");
        [TestCase(1),TestCase(2),TestCase(3),TestCase(4),TestCase(5)]
        public void HorizonVeilIsOneSoftTransparentDrawBehindScenery(int n)
        {
            var f=Level(n);var veil=f.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Sky haze");
            Assert.AreEqual("Creek Diorama/Soft Horizon Veil",veil.sharedMaterial.shader.name);
            Assert.AreEqual(2900,veil.sharedMaterial.renderQueue);
            Assert.Less(veil.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3,120);
            Assert.IsEmpty(veil.GetComponentsInChildren<Collider>());
        }
        [Test] public void FifthCourseHasSupportedFlowerBordersOutsideRoad()
        {
            var f=Level(5);var group=f.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Freight wildflower borders");
            var source=group.Find("Editable planting");Assert.GreaterOrEqual(source.childCount,24);Assert.IsFalse(source.gameObject.activeSelf);
            var ground=new ContactPolishAuthoring.Surface(f.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.name=="Campaign island ground turf"&&m.GetComponent<Renderer>().enabled&&m.GetComponentsInParent<Transform>(true).All(t=>t.gameObject.activeSelf)));
            // The start and finish terraces overlap in XZ; test support below each plant, not the highest terrace overhead.
            foreach(Transform plant in source){Assert.IsTrue(ground.Height(plant.position,out float height,plant.position.y+.001f));Assert.That(plant.position.y-height,Is.InRange(0,.08f));}
            var lanes=CarSceneryClearance.Lanes(f);foreach(var r in group.Find("Flower and grass batches").GetComponentsInChildren<Renderer>())Assert.IsFalse(CarSceneryClearance.Intrudes(r,lanes));
        }
        [TestCase(1),TestCase(2),TestCase(3),TestCase(4),TestCase(5)]
        public void WaterIsBelowRimWithClosedInnerWalls(int n)
        {
            var f=Level(n);var filters=f.GetComponentsInChildren<MeshFilter>(true);
            var river=filters.Single(m=>m.name=="Freight creek"||m.name=="Living river surface");
            var steel=filters.Single(m=>m.name=="Blue steel edge"&&m.transform.parent.name=="01 Display base");
            var lining=filters.Single(m=>m.name=="Inner tray wall");
            var floor=filters.Single(m=>m.name=="Deep shadow"&&m.transform.parent.name=="01 Display base");
            float height=river.transform.TransformPoint(river.sharedMesh.vertices[0]).y;
            float rim=steel.sharedMesh.vertices.Where(p=>p.y>.16f).Min(p=>steel.transform.TransformPoint(p).y);
            Assert.Less(height,rim,"Water must remain below the inner rim lip");
            Assert.Greater(height,floor.sharedMesh.vertices.Max(p=>floor.transform.TransformPoint(p).y));
            Assert.AreEqual(32,lining.sharedMesh.vertexCount);
            var wall=lining.sharedMesh.vertices.Select(lining.transform.TransformPoint).ToArray();
            foreach(var p in river.sharedMesh.vertices.Select(river.transform.TransformPoint)) {
                Assert.That(Mathf.Abs(p.y-height),Is.LessThan(.00001f));
                Assert.IsTrue(wall.Any(w=>new Vector2(w.x-p.x,w.z-p.z).sqrMagnitude<.000001f&&w.y>height),"Water edge must meet an opaque wall above it");
                Assert.IsTrue(wall.Any(w=>new Vector2(w.x-p.x,w.z-p.z).sqrMagnitude<.000001f&&w.y<height),"Wall must extend below water");
            }
            foreach(var r in f.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Player view (editor only)")) {
                Assert.AreEqual("EditorOnly",r.tag);Assert.That(r.sharedMaterial.shader.name,Does.StartWith("Universal Render Pipeline/"));
            }
        }
        [TestCase(1),TestCase(2),TestCase(3),TestCase(4)]
        public void RiverFacesShareOneHeightAndHaveNoDegenerateTriangles(int n)
        {
            var rivers=Level(n).GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="Living river surface").ToArray();
            Assert.IsNotEmpty(rivers);
            foreach(var f in rivers) {
                var v=f.sharedMesh.vertices;var ix=f.sharedMesh.triangles;
                Assert.Less(v.Max(p=>p.y)-v.Min(p=>p.y),.00001f);
                for(int i=0;i<ix.Length;i+=3)Assert.Greater(Vector3.Cross(v[ix[i+1]]-v[ix[i]],v[ix[i+2]]-v[ix[i]]).y,0);
            }
        }
        [Test] public void LoweredFirstTerraceTreesRestOnVisibleTerrain()
        {
            var level=Level(1);
            var moss=level.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="Botanical moss"&&f.transform.parent.name=="Stratified rock and soil");
            var surface=new ContactPolishAuthoring.Surface(new[]{moss});int checkedTrees=0;
            foreach(var tree in level.GetComponentsInChildren<Transform>(true).Where(t=>t.parent&&t.parent.name=="Layered canopy"&&(t.name.StartsWith("Pine")||t.name.StartsWith("Tree_")))) {
                if(!surface.Height(tree.position,out float y))continue;
                Assert.That(tree.position.y-y,Is.InRange(-.01f,.035f),tree.name+" at "+tree.position);checkedTrees++;
            }
            Assert.GreaterOrEqual(checkedTrees,12);
        }
        [Test] public void BarrelBatchIncludesWoodAndMetal()
        {
            var group = Level(2).GetComponentsInChildren<Transform>(true).Single(t => t.name == "Story props" && t.parent.name == "Render batches");
            foreach (string material in new[] { "Botanical oak", "Botanical bronze" })
            {
                var t = group.Find(material); Assert.IsNotNull(t, "Missing material batch: " + material);
                Assert.IsTrue(t.gameObject.activeSelf); Assert.IsTrue(t.GetComponent<Renderer>().enabled);
                Assert.Greater(t.GetComponent<MeshFilter>().sharedMesh.triangles.Length, 0);
            }
        }
        [TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
        public void SupportsArePersistedAndDoNotEnterDrivingClearance(int n)
        {
            var factory = Level(n); Assert.IsTrue(factory.Validate(out string error), error);
            var lanes = CarSceneryClearance.Lanes(factory);
            var batches = factory.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Trestle render batches").ToArray();
            Assert.IsNotEmpty(batches);
            foreach (var batch in batches)
            {
                Assert.IsEmpty(batch.GetComponentsInChildren<Collider>(true));
                foreach (var filter in batch.GetComponentsInChildren<MeshFilter>())
                {
                    Assert.IsTrue(AssetDatabase.Contains(filter.sharedMesh));
                    Assert.IsFalse(CarSceneryClearance.MeshOverlaps(filter, lanes), n + " " + filter.name + " intrudes into driving space");
                }
            }
        }
        [TestCase(4), TestCase(5)]
        public void AuthoredBridgeFallsWithRopesAndResetsWithoutChangingCollision(int n)
        {
            var scene = SceneManager.GetActiveScene();
            var oldSimulation = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
            var factory = Object.Instantiate(Level(n)); factory.transform.position += Vector3.right * 100;
            var beforeSuspended = SandboxTrackEditing.Suspended; SandboxTrackEditing.Suspended = true;
            try
            {
                var bridge = factory.GetComponentInChildren<SandboxDropBridge>(true); Assert.IsNotNull(bridge);
                var rig = bridge.GetComponent<SandboxBridgeRig>(); Assert.IsNotNull(rig);
                bridge.ResetPuzzle(true); Physics.SyncTransforms();
                var pose = bridge.Deck.rotation; int count = bridge.Deck.GetComponentsInChildren<Collider>(true).Length;
                Assert.AreEqual(1, count, "Visual planks must not add seams to physics");
                var tip = bridge.Deck.transform.TransformPoint(Vector3.forward * bridge.Length * bridge.transform.lossyScale.x);
                bridge.DrillHit(bridge.TargetCollider.bounds.center);
                Assert.IsTrue(bridge.Released); Assert.IsFalse(bridge.Deck.isKinematic);
                for (int i = 0; i < 240; i++) scene.GetPhysicsScene().Simulate(.01f);
                var lowered = bridge.Deck.transform.TransformPoint(Vector3.forward * bridge.Length * bridge.transform.lossyScale.x);
                Assert.Less(lowered.y, tip.y - bridge.transform.lossyScale.x * .5f);
                rig.UpdateRig(1);
                foreach (var cable in rig.Cables) Assert.IsTrue(cable.enabled && cable.gameObject.activeInHierarchy);
                for (int i = 0; i < rig.Cables.Length; i++) Assert.Less(Vector3.Distance(rig.Cables[i].GetPosition(0), rig.DeckEyes[i].position), .0001f);
                bridge.ResetPuzzle(true); Assert.Less(Quaternion.Angle(pose, bridge.Deck.rotation), .001f); Assert.IsFalse(bridge.Released);
                Assert.IsTrue(bridge.TargetBody.gameObject.activeSelf);
            }
            finally { Object.DestroyImmediate(factory.gameObject); SandboxTrackEditing.Suspended = beforeSuspended; Physics.simulationMode = oldSimulation; }
        }
        [Test] public void CloudBodyAndShellUseMatchingSlowMotion()
        {
            var materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets/CreekDiorama/Materials" }).Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(m => m.shader.name == "Creek Diorama/Cloud Puff" || m.shader.name == "Creek Diorama/Cloud Shell").ToArray();
            Assert.IsNotEmpty(materials);
            foreach (var m in materials) { Assert.That(m.GetFloat("_RollSpeed"), Is.InRange(.01f,.015f)); Assert.That(m.GetFloat("_Drift"),Is.InRange(.04f,.07f)); Assert.LessOrEqual(m.GetFloat("_Swell"), .5f); }
        }
    }
}
