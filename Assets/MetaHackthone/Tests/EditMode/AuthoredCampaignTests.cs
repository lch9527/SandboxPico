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
    public sealed class AuthoredCampaignTests
    {
        Scene scene, previous;
        string folder;
        bool suspended;

        [SetUp] public void SetUp()
        {
            previous=SceneManager.GetActiveScene();
            suspended=SandboxTrackEditing.Suspended;
            SandboxTrackEditing.Suspended=true;
            folder="Assets/AuthoredCampaignTest-"+Guid.NewGuid().ToString("N");
            SandboxTrackBaker.EnsureFolder(folder);
            // The EditMode runner starts with an untitled scratch scene; Unity
            // refuses additive scene IO until that scene has a path.
            if(string.IsNullOrEmpty(previous.path)) EditorSceneManager.SaveScene(previous,folder+"/Runner.unity");
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        }

        [TearDown] public void TearDown()
        {
            if(scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
            if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            AssetDatabase.DeleteAsset(folder);
            SandboxTrackEditing.Suspended=suspended;
        }

        [TestCase(1),TestCase(2),TestCase(3),TestCase(4),TestCase(5)]
        public void ManualEditsSurviveForcedBakeSceneReloadExportAndInstantiation(int number)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>("Assets/MetaHackthone/Generated/Campaign/Level"+number+".prefab");
            Assert.IsTrue(prefab.AuthoredObjects);
            var factory=Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(factory.gameObject,scene);
            var owner=factory.gameObject.AddComponent<BuildingSceneLayout>();
            factory.Layout.Bind(owner);
            owner.Spawns=factory.CampaignSpawns;
            string path=folder+"/Authored.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(scene,path));
            var track=factory.Tracks[0];
            var root=track.Generated;
            var rail=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.IndexOf("rail",StringComparison.OrdinalIgnoreCase)>=0)
                ?? root.GetComponentsInChildren<MeshFilter>(true).Last().transform;
            string removed=AnimationUtility.CalculateTransformPath(rail,root.transform);
            Object.DestroyImmediate(rail.gameObject);
            var road=root.GetComponentInChildren<MeshFilter>(true).transform;
            road.localPosition+=new Vector3(.13f,.04f,.08f);
            road.gameObject.SetActive(false);
            string roadPath=AnimationUtility.CalculateTransformPath(road,root.transform);
            var roadPosition=road.localPosition;
            var scenery=new GameObject("Hand placed scenery");
            scenery.transform.SetParent(factory.transform,false);
            scenery.transform.localPosition=new Vector3(3,2,1);
            var building=factory.Layout.BuildingRoot;
            building.localPosition+=new Vector3(.1f,0,.1f);
            var buildingPosition=building.localPosition;
            var start=factory.Start.position;
            factory.BindStart=true;
            factory.StartBinding.Offset+=Vector3.one;
            track.DefaultWidth+=1; // A stale signature used to trigger destructive save-time rebuilding.
            Assert.IsTrue(SandboxTrackBaker.RebuildFromInspector(track));
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true));
            SandboxPuzzleEditing.UpdateTrack(track);
            Assert.AreSame(root,track.Generated);
            SandboxTrackEditing.Suspended=false;
            Assert.IsTrue(SandboxTrackEditing.Flush(scene,true));
            Assert.AreEqual(start,factory.Start.position);
            Assert.IsTrue(EditorSceneManager.SaveScene(scene,path));
            EditorSceneManager.CloseScene(scene,true);
            scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            factory=SandboxFactoryAuthoring.Find(scene);
            var exported=SandboxFactoryCampaignSave.Export(factory,folder+"/Game.prefab",folder+"/Meshes","Authored test");
            var game=Object.Instantiate(exported);
            SceneManager.MoveGameObjectToScene(game.gameObject,scene);
            root=game.Tracks[0].Generated;
            Assert.IsNull(root.transform.Find(removed),"Deleted rail must remain deleted in gameplay.");
            var savedRoad=root.transform.Find(roadPath);
            Assert.AreEqual(roadPosition,savedRoad.localPosition);
            Assert.IsFalse(savedRoad.gameObject.activeSelf);
            Assert.AreEqual(buildingPosition,game.Layout.BuildingRoot.localPosition);
            Assert.AreEqual(new Vector3(3,2,1),game.transform.Find("Hand placed scenery").localPosition);
            Assert.IsTrue(game.Validate(out var error),error);
        }

        [Test] public void WorkbenchDoesNotResetTransformsOrRecreateDeletedArtwork()
        {
            var go=new GameObject("Authored building");
            SceneManager.MoveGameObjectToScene(go,scene);
            go.AddComponent<SandboxFactoryLevel>().AuthoredObjects=true;
            var volume=go.AddComponent<SandboxBuildVolume>();
            volume.Artwork=new GameObject("Edited artwork").transform;
            volume.Artwork.SetParent(go.transform,false);
            volume.Artwork.localPosition=Vector3.one;
            volume.Artwork.localRotation=Quaternion.Euler(0,17,0);
            volume.Artwork.localScale=Vector3.one*2;
            volume.FitArtwork(); volume.Rebuild();
            Assert.AreEqual(Vector3.one,volume.Artwork.localPosition);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0,17,0),volume.Artwork.localRotation),.001f);
            Assert.AreEqual(Vector3.one*2,volume.Artwork.localScale);
            Object.DestroyImmediate(volume.Artwork.gameObject);
            volume.Rebuild();
            Assert.IsTrue(volume.Artwork==null);
            Assert.AreEqual(0,go.transform.childCount);
        }

        [Test] public void BridgeResetsToAuthoredPoseAndAllowsDeletedChain()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>("Assets/MetaHackthone/Generated/Campaign/Level4.prefab");
            var factory=Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(factory.gameObject,scene);
            var bridge=factory.GetComponentInChildren<SandboxDropBridge>(true);
            Assert.IsNotNull(bridge);
            bridge.Deck.transform.position+=Vector3.up*.03f;
            bridge.Deck.transform.rotation*=Quaternion.Euler(4,0,0);
            var position=bridge.Deck.transform.position;
            var rotation=bridge.Deck.transform.rotation;
            if(bridge.Chain) Object.DestroyImmediate(bridge.Chain.gameObject);
            bridge.ResetPuzzle(false);
            bridge.Deck.transform.position+=Vector3.one;
            bridge.ResetPuzzle(false);
            Assert.Less(Vector3.Distance(position,bridge.Deck.transform.position),.0001f);
            Assert.Less(Quaternion.Angle(rotation,bridge.Deck.transform.rotation),.001f);
            Assert.IsTrue(bridge.Chain==null);
        }
    }
}
