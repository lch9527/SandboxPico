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
    public sealed class FactoryAuthoringTests
    {
        Scene scene; string path; SandboxFactoryLevel factory;
        [SetUp] public void OpenCopy()
        {
            SandboxTrackEditing.Suspended=true;
            path="Assets/FactoryValidation-"+Guid.NewGuid().ToString("N")+".unity";
            Assert.IsTrue(AssetDatabase.CopyAsset(SandboxFactorySamples.SamplePath,path));
            scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive); factory=SandboxFactoryAuthoring.Find(scene);
        }
        [TearDown] public void Cleanup()
        {
            if(scene.IsValid() && scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            if(!string.IsNullOrEmpty(path)) {AssetDatabase.DeleteAsset(path);AssetDatabase.DeleteAsset(System.IO.Path.ChangeExtension(path,null)+"_Factory");}
            SandboxTrackEditing.Suspended=false;
        }
        [Test] public void DuplicateDeleteAndUndoRedoRebuildTheTrack()
        {
            var track=factory.Tracks[0]; var source=factory.Puzzles.OfType<SandboxCrabBridge>().Single();
            var duplicate=Object.Instantiate(source,source.transform.parent); duplicate.Distance=9; duplicate.Length=2;
            Undo.RegisterCreatedObjectUndo(duplicate.gameObject,"Duplicate puzzle");
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            Assert.AreEqual(4,factory.Puzzles.Length);
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(duplicate,"Change hole"); duplicate.HoleWidth=.9f; Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); Assert.AreEqual(source.HoleWidth,duplicate.HoleWidth);
            Undo.PerformRedo(); Assert.AreEqual(.9f,duplicate.HoleWidth);
            Undo.IncrementCurrentGroup(); Undo.DestroyObjectImmediate(duplicate.gameObject);
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true)); Assert.AreEqual(3,factory.Puzzles.Length);
            Undo.PerformUndo(); Assert.AreEqual(4,factory.Puzzles.Length);
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            Undo.ClearAll();
        }
        [Test] public void SavingAndReopeningRetainsCutoutCollidersAndMechanismReferences()
        {
            var authored=factory.Tracks[0];authored.Guardrails=true;
            authored.LeftRailEnabled=false;authored.RightRailHeight=.83f;authored.RightRailThickness=.17f;
            authored.RailSectionLength=.3f;
            authored.RightRailSections.Add(new SandboxRailSectionSettings{Index=1,Height=.93f});
            SandboxTrackEditing.Suspended=false;
            Assert.IsTrue(SandboxTrackEditing.Flush(scene,true)); Assert.IsTrue(EditorSceneManager.SaveScene(scene));
            SandboxTrackEditing.Suspended=true;
            EditorSceneManager.CloseScene(scene,true); scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive); factory=SandboxFactoryAuthoring.Find(scene);
            Assert.AreEqual(3,factory.Puzzles.Length);
            var crab=factory.Puzzles.OfType<SandboxCrabBridge>().Single(); var track=crab.Track;
            Assert.IsFalse(track.LeftRailEnabled);Assert.AreEqual(.83f,track.RightRailHeight);Assert.AreEqual(.17f,track.RightRailThickness);
            Assert.IsNull(track.Generated.transform.Find("Left Rail"));Assert.IsNotNull(track.Generated.transform.Find("Right Rail"));
            Assert.AreEqual(.3f,track.RailSectionLength);Assert.AreEqual(.93f,track.RailSectionHeight(1,1));
            Assert.IsNotNull(track.Generated.transform.Find("Right Rail/Section 002"));
            var ray=new Ray(crab.transform.position+crab.transform.up, -crab.transform.up);
            foreach(var collider in track.Generated.GetComponentsInChildren<Collider>()) Assert.IsFalse(collider.Raycast(ray,out _,2),"Persisted collider fills hole");
            foreach(var mesh in track.Generated.GetComponentsInChildren<MeshFilter>()) Assert.IsTrue(AssetDatabase.Contains(mesh.sharedMesh));
            var bridge=factory.Puzzles.OfType<SandboxDropBridge>().Single();
            Assert.NotNull(bridge.Deck); Assert.NotNull(bridge.Hinge.connectedBody); Assert.NotNull(bridge.Restraint.connectedBody); Assert.NotNull(bridge.TargetCollider);
            Assert.That(bridge.Deck.transform.lossyScale.x,Is.EqualTo(1).Within(.001f));
            Assert.AreEqual(9,factory.Presets.Length);
            foreach(var car in factory.Presets)Assert.DoesNotThrow(()=>AssemblySaveService.Validate(car.Assembly));
        }
        [Test] public void BoundStartFollowsEditedSplineWithoutChangingItsDistance()
        {
            var track=factory.StartBinding.Track; var before=factory.Start.position;
            var knot=track.Curve.Spline[0]; knot.Position.x+=1; track.Curve.Spline[0]=knot;
            float distance=factory.StartBinding.Distance;
            Assert.IsTrue(SandboxTrackEditing.UpdateStart(factory));
            Assert.Greater(Vector3.Distance(before,factory.Start.position),.01f); Assert.AreEqual(distance,factory.StartBinding.Distance);
        }
        [Test] public void SavingEditedTrackUpdatesExistingRenderBufferAndCollider()
        {
            var track=factory.Tracks[0];
            SandboxTrackBaker.Persist(track);
            var original=track.Generated.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Road").sharedMesh;
            var originalPath=AssetDatabase.GetAssetPath(original);
            // Force allocation of the old render buffer before changing the spline.
            var before=ReadRenderedPosition(original);
            var knot=track.Curve.Spline[0]; knot.Position.y+=2; track.Curve.Spline[0]=knot;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            var expected=track.Generated.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Road").sharedMesh.vertices[0];
            Assert.Greater(Vector3.Distance(before,expected),.1f);
            SandboxTrackBaker.Persist(track);
            var road=track.Generated.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Road");
            Assert.AreSame(original,road.sharedMesh,"Keep existing scene/asset references valid.");
            Assert.AreEqual(originalPath,AssetDatabase.GetAssetPath(road.sharedMesh));
            Assert.AreSame(road.sharedMesh,road.GetComponent<MeshCollider>().sharedMesh);
            Assert.That(Vector3.Distance(expected,ReadRenderedPosition(road.sharedMesh)),Is.LessThan(.0001f),"GPU geometry must match the edited collider geometry.");
            Assert.IsTrue(EditorSceneManager.SaveScene(scene));
            EditorSceneManager.CloseScene(scene,true);
            scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive); factory=SandboxFactoryAuthoring.Find(scene);
            road=factory.Tracks[0].Generated.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Road");
            Assert.AreSame(road.sharedMesh,road.GetComponent<MeshCollider>().sharedMesh);
            Assert.That(Vector3.Distance(expected,road.sharedMesh.vertices[0]),Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(expected,ReadRenderedPosition(road.sharedMesh)),Is.LessThan(.0001f));
        }
        static Vector3 ReadRenderedPosition(Mesh mesh)
        {
            int stream=mesh.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.Position);
            int offset=mesh.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Position);
            using(var buffer=mesh.GetVertexBuffer(stream)) {
                var bytes=new byte[buffer.count*buffer.stride]; buffer.GetData(bytes);
                return new Vector3(BitConverter.ToSingle(bytes,offset),BitConverter.ToSingle(bytes,offset+4),BitConverter.ToSingle(bytes,offset+8));
            }
        }
        [Test] public void BlankTemplateCreatesAnIndependentSavedTrackWithPresetsAndBoundStart()
        {
            EditorSceneManager.CloseScene(scene,true); AssetDatabase.DeleteAsset(path);
            Assert.IsTrue(AssetDatabase.CopyAsset(SandboxFactoryAuthoring.TemplatePath,path));
            scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive); factory=SandboxFactoryAuthoring.Find(scene);
            Assert.IsEmpty(factory.Tracks); Assert.AreEqual(9,factory.Presets.Length);
            Assert.AreEqual(10,factory.GetComponent<BuildingSceneLayout>().Spawns.Length);
            SandboxFactoryAuthoring.MakeIndependent(scene,path);
            var track=SandboxFactoryAuthoring.AddTrack(factory,false);
            Assert.AreSame(track,factory.StartBinding.Track); Assert.IsTrue(factory.BindStart);
            Assert.IsTrue(SandboxTrackEditing.Flush(scene,true)); Assert.IsTrue(EditorSceneManager.SaveScene(scene));
            EditorSceneManager.CloseScene(scene,true); scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            factory=SandboxFactoryAuthoring.Find(scene);
            Assert.AreEqual(1,factory.Tracks.Length); Assert.IsTrue(factory.Validate(out var error),error);
            Assert.IsTrue(AssetDatabase.GetAssetPath(factory.TestCar).StartsWith(System.IO.Path.ChangeExtension(path,null)+"_Factory/"));
        }
    }
}
