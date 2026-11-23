using System.Linq;
using NUnit.Framework;
using PhysicalAssembly.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PhysicalAssembly.Tests
{
    public sealed class SceneryClearanceTests
    {
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void AllNewSceneryAndItsMotionStayOutsideDrivingClearance(int level)
        {
            string path=$"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try {
                var factory=root.GetComponent<SandboxFactoryLevel>();var lanes=CarSceneryClearance.Lanes(factory);Assert.IsNotEmpty(lanes);
                foreach(var group in CarSceneryClearance.MoveGroups(factory))foreach(var renderer in group.GetComponentsInChildren<Renderer>())
                    Assert.IsFalse(CarSceneryClearance.Intrudes(renderer,lanes),$"Level {level}: {group.name}/{renderer.name}");
                var area=factory.Layout.Course.BuildArea;var volume=area.GetComponent<SandboxBuildVolume>();Assert.IsTrue(volume.ShowWalls);
                CheckFrame(volume.Artwork);
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        [Test] public void SharedRangeHasOnlyThinOpaqueGeometry()
        {
            CheckFrame(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MetaHackthone/Resources/SandboxBuildingBoundary.prefab").transform);
            var shader=Shader.Find(LightweightBoundaryAuthoring.ShaderName);Assert.IsNotNull(shader);Assert.IsTrue(shader.isSupported);
            Assert.IsEmpty(ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray());
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void ExistingTerrainStaysOutsideDrivingSpace(int level)
        {
            var root=PrefabUtility.LoadPrefabContents($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab");
            try {
                var f=root.GetComponent<SandboxFactoryLevel>();var lanes=CarSceneryClearance.Lanes(f);var env=f.Layout.Course.SandboxFrame.Find("Environment");var art=env.Find(CarSceneryAuthoring.GroupName);
                foreach(var filter in env.GetComponentsInChildren<MeshFilter>()) {
                    var r=filter.GetComponent<Renderer>();if(!r || !r.enabled || filter.transform.IsChildOf(art))continue;
                    Assert.IsFalse(CarSceneryClearance.MeshOverlaps(filter,lanes),$"Level {level}: {filter.name}");
                }
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        [Test] public void QuarryRoadEndsHaveSolidGroundImmediatelyBelowThem()
        {
            var root=PrefabUtility.LoadPrefabContents("Assets/MetaHackthone/Generated/Campaign/Level4.prefab");
            try {
                var f=root.GetComponent<SandboxFactoryLevel>();var track=f.Tracks[0];var art=f.Layout.Course.SandboxFrame.Find("Environment/"+CarSceneryAuthoring.GroupName);
                for(int i=0;i<2;i++) {
                    var island=art.Find(QuarryIslandAuthoring.IslandPrefix+(i==0?" Start":" Finish"));Assert.IsNotNull(island);
                    var endpoint=track.transform.TransformPoint(i==0?track.Samples[0]:track.Samples[^1]);
                    Assert.Less(Vector3.Distance(island.position,endpoint),.001f);
                    var turf=island.Find("Quarry island ground turf");var collider=turf.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=turf.GetComponent<MeshFilter>().sharedMesh;
                    try {
                        Assert.IsTrue(collider.Raycast(new Ray(endpoint+Vector3.up,Vector3.down),out var hit,2),"Road endpoint must land on island turf");
                        Assert.That(endpoint.y-hit.point.y,Is.InRange(.01f,.15f),"Island should support the road without a large gap");
                    }finally{Object.DestroyImmediate(collider);}
                    Assert.IsEmpty(island.GetComponentsInChildren<Collider>());
                }
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void BothRoadEndsAreSupportedAcrossWidthInSceneAndRuntime(int level)
        {
            var root=PrefabUtility.LoadPrefabContents($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab");
            try {Assert.IsEmpty(CampaignLandingAudit.Check(root.GetComponent<SandboxFactoryLevel>()),"Runtime prefab");}
            finally{PrefabUtility.UnloadPrefabContents(root);}
            var setup=EditorSceneManager.GetSceneManagerSetup();bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
            try {
                var scene=EditorSceneManager.OpenScene($"Assets/MetaHackthone/Scenes/Level{level}.unity",OpenSceneMode.Additive);
                Assert.IsEmpty(CampaignLandingAudit.Check(SandboxFactoryAuthoring.Find(scene)),"Authoring scene");EditorSceneManager.CloseScene(scene,true);
            }finally{SandboxTrackEditing.Suspended=suspended;if(setup.Any(s=>s.isLoaded))EditorSceneManager.RestoreSceneManagerSetup(setup);}
        }
        static void CheckFrame(Transform root)
        {
            Assert.IsNotNull(root);Assert.IsEmpty(root.GetComponentsInChildren<Collider>(true));
            var renderers=root.GetComponentsInChildren<Renderer>(true);Assert.AreEqual(2,renderers.Length);
            foreach(var renderer in renderers) {
                Assert.AreEqual(1,renderer.sharedMaterials.Length);
                Assert.AreEqual(LightweightBoundaryAuthoring.ShaderName,renderer.sharedMaterial.shader.name);
                Assert.Less(renderer.sharedMaterial.renderQueue,2500);
            }
            int triangles=root.GetComponentsInChildren<MeshFilter>(true).Sum(m=>m.sharedMesh.triangles.Length/3);Assert.AreEqual(432,triangles);
        }
        [Test] public void BoundaryVisibilityKeepsGameplayExtentAndRespondsToToggle()
        {
            var go=new GameObject("range test");
            try {
                var area=go.AddComponent<BuildingArea>();area.Size=new Vector3(.8f,.4f,.7f);
                var volume=go.AddComponent<SandboxBuildVolume>();volume.Rebuild();volume.ShowWalls=true;volume.SetVisible(true);
                Assert.IsTrue(volume.Artwork.GetComponent<Renderer>().enabled);Assert.IsTrue(volume.Tabletop.enabled);
                volume.SetVisible(false);Assert.IsFalse(volume.Artwork.GetComponent<Renderer>().enabled);Assert.IsTrue(volume.Tabletop.enabled);
                volume.SetVisible(true);Assert.IsTrue(volume.Artwork.GetComponent<Renderer>().enabled);
                volume.ShowWalls=false;volume.SetVisible(true);Assert.IsFalse(volume.Artwork.GetComponent<Renderer>().enabled);
                area.Size=new Vector3(1,.5f,.6f);volume.FitArtwork();Assert.AreEqual(area.Size,volume.Artwork.localScale);
                Assert.IsTrue(area.Contains(Vector3.zero));Assert.IsFalse(area.Contains(Vector3.right));
            } finally { Object.DestroyImmediate(go); }
        }
    }
}
