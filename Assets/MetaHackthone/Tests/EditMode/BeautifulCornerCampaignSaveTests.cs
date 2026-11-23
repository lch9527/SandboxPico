using System;
using System.Linq;
using NUnit.Framework;
using PhysicalAssembly.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Tests
{
    public sealed class BeautifulCornerCampaignSaveTests
    {
        [Test] public void ReopenedSceneAndGameCopyKeepIndependentRoadAndSeparateWorkbench()
        {
            var scene=SceneManager.GetSceneByPath(BeautifulCornerDesignAuthoring.ScenePath);
            bool opened=!scene.isLoaded;
            if(opened) scene=EditorSceneManager.OpenScene(BeautifulCornerDesignAuthoring.ScenePath,OpenSceneMode.Additive);
            try {
                var source=SandboxFactoryAuthoring.Find(scene);
                var saved=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>(BeautifulCornerDesignAuthoring.PrefabPath);
                Assert.IsTrue(source.Validate(out var error),error); Assert.IsTrue(saved.Validate(out error),error);
                Assert.IsTrue(saved.UseStarterPalette); Assert.IsEmpty(saved.Puzzles);
                Assert.IsNotNull(saved.WorkbenchVisuals); Assert.IsFalse(saved.WorkbenchVisuals.IsChildOf(saved.TrackRoot));
                Assert.IsNotEmpty(saved.Scenery);
                Assert.AreEqual(9,saved.CampaignSpawns.Length);
                Assert.IsTrue(source.Tracks[0].ShowGeneratedVisuals);Assert.IsTrue(saved.Tracks[0].ShowGeneratedVisuals);
                Assert.AreEqual(SandboxDeckStyle.CreekDeck,saved.Tracks[0].DeckStyle);
                Assert.IsTrue(saved.Tracks[0].Generated.GetComponentsInChildren<Renderer>().Any(r=>r.enabled));
                var a=source.Tracks[0].Generated.GetComponentInChildren<MeshCollider>();
                var b=saved.Tracks[0].Generated.GetComponentInChildren<MeshCollider>();
                Assert.IsTrue(a.enabled && b.enabled); Assert.IsFalse(b.GetComponent<Renderer>().enabled);
                Assert.IsNotNull(a.sharedMesh);Assert.AreNotSame(a.sharedMesh,b.sharedMesh);
                CollectionAssert.AreEqual(a.sharedMesh.vertices,b.sharedMesh.vertices);
                Assert.IsNotEmpty(AssetDatabase.GetAssetPath(a.sharedMesh));
                Assert.Less(Vector3.Distance(source.transform.InverseTransformPoint(source.Start.position),saved.Start.position),.0001f);
                Assert.Less(Vector3.Distance(source.transform.InverseTransformPoint(source.Finish.transform.position),saved.Finish.transform.position),.0001f);
                Assert.IsEmpty(saved.Scenery.SelectMany(s=>s.GetComponentsInChildren<Collider>(true)).Where(c=>c!=saved.Finish),"Scenery must not introduce car-blocking collision.");
                Assert.AreSame(saved,AssetDatabase.LoadAssetAtPath<DownhillSettings>("Assets/MetaHackthone/Generated/GravityGarageSettings.asset").Level1);
            } finally { if(opened) EditorSceneManager.CloseScene(scene,true); }
        }

        [Test] public void RebuildAndExportRetainHiddenCollisionAndInvalidEditsKeepLastCopy()
        {
            var scene=SceneManager.GetSceneByPath(BeautifulCornerDesignAuthoring.ScenePath);
            bool opened=!scene.isLoaded;
            if(opened) scene=EditorSceneManager.OpenScene(BeautifulCornerDesignAuthoring.ScenePath,OpenSceneMode.Additive);
            var preview=EditorSceneManager.NewPreviewScene();
            string folder="Assets/BeautifulCornerSaveTest-"+Guid.NewGuid().ToString("N");
            var suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
            GameObject clone=null;
            try {
                clone=Object.Instantiate(SandboxFactoryAuthoring.Find(scene).gameObject);
                SceneManager.MoveGameObjectToScene(clone,preview);
                var factory=clone.GetComponent<SandboxFactoryLevel>();var track=factory.Tracks.Single();
                track.ShowGeneratedVisuals=false;
                Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true));
                Assert.IsTrue(track.Generated.GetComponentInChildren<MeshCollider>().enabled);
                Assert.IsFalse(track.Generated.GetComponentInChildren<Renderer>().enabled);
                string path=folder+"/Level1.prefab";
                var saved=SandboxFactoryCampaignSave.Export(factory,path,folder+"/Meshes","Beautiful Corner test");
                var mesh=saved.Tracks[0].Generated.GetComponentInChildren<MeshCollider>().sharedMesh;
                var vertices=mesh.vertices;
                var guid=AssetDatabase.AssetPathToGUID(path);
                Assert.IsNotNull(saved.WorkbenchVisuals);Assert.IsFalse(saved.WorkbenchVisuals.IsChildOf(saved.TrackRoot));
                track.DefaultWidth=-1;
                Assert.IsFalse(SandboxTrackBaker.Rebuild(track,true));
                Assert.Throws<InvalidOperationException>(()=>SandboxFactoryCampaignSave.Export(factory,path,folder+"/Meshes","Invalid"));
                Assert.AreEqual(guid,AssetDatabase.AssetPathToGUID(path));CollectionAssert.AreEqual(vertices,mesh.vertices);
                Assert.IsTrue(saved.Validate(out var error),error);
            } finally {
                if(clone) Object.DestroyImmediate(clone);
                EditorSceneManager.ClosePreviewScene(preview);SandboxTrackEditing.Suspended=suspended;
                if(opened) EditorSceneManager.CloseScene(scene,true);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
