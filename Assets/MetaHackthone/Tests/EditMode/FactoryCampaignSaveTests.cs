using System.Linq;
using NUnit.Framework;
using PhysicalAssembly.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAssembly.Tests
{
    public sealed class FactoryCampaignSaveTests
    {
        [Test] public void PublishedLevelPreservesAuthoredRoadGoalAndPuzzleReferences()
        {
            var scene=SceneManager.GetSceneByPath(SandboxFactoryCampaignSave.ScenePath);
            bool opened=!scene.isLoaded;
            if(opened) scene=EditorSceneManager.OpenScene(SandboxFactoryCampaignSave.ScenePath,OpenSceneMode.Additive);
            try {
                var source=SandboxFactoryAuthoring.Find(scene);
                var saved=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>(SandboxFactoryCampaignSave.PrefabPath);
                Assert.IsNotNull(saved);
                Assert.IsTrue(saved.Validate(out var error),error);
                Assert.AreEqual(source.CenterPathAssist,saved.CenterPathAssist);
                Assert.AreEqual(source.CourseScale,saved.CourseScale);
                Assert.AreEqual(source.Finish.size,saved.Finish.size);
                Assert.AreEqual(source.Finish.center,saved.Finish.center);
                Assert.That(Vector3.Distance(source.transform.InverseTransformPoint(source.Finish.transform.position),saved.Finish.transform.position),Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(source.transform.InverseTransformPoint(source.Start.position),saved.Start.position),Is.LessThan(.0001f));
                Assert.AreEqual(source.Tracks.Length,saved.Tracks.Length);
                for(int i=0;i<source.Tracks.Length;i++) {
                    var a=source.Tracks[i].Generated.GetComponentsInChildren<MeshFilter>();
                    var b=saved.Tracks[i].Generated.GetComponentsInChildren<MeshFilter>();
                    Assert.AreEqual(a.Length,b.Length);
                    for(int j=0;j<a.Length;j++) {
                        Assert.AreNotSame(a[j].sharedMesh,b[j].sharedMesh,"Published meshes must be independent of authoring edits.");
                        CollectionAssert.AreEqual(a[j].sharedMesh.vertices,b[j].sharedMesh.vertices);
                        CollectionAssert.AreEqual(a[j].sharedMesh.triangles,b[j].sharedMesh.triangles);
                    }
                }
                Assert.AreEqual(source.Puzzles.Length,saved.Puzzles.Length);
                foreach(var puzzle in saved.Puzzles) Assert.IsTrue(puzzle.Track.transform.IsChildOf(saved.transform));
                // No exported scene-object reference may still point into the authoring scene.
                foreach(var component in saved.GetComponentsInChildren<Component>(true).Where(c=>c)) {
                    var serialized=new SerializedObject(component); var property=serialized.GetIterator();
                    while(property.Next(true)) if(property.propertyType==SerializedPropertyType.ObjectReference) {
                        var referenced=property.objectReferenceValue as Component;
                        if(referenced) Assert.IsFalse(referenced.gameObject.scene.IsValid(),component.name+"."+property.propertyPath);
                    }
                }
                CollectionAssert.AreEqual(source.GetComponent<BuildingSceneLayout>().Spawns.Select(s=>s.Kind),saved.CampaignSpawns.Select(s=>s.Kind));
            } finally { if(opened) EditorSceneManager.CloseScene(scene,true); }
        }

        [Test] public void CampaignOrderAndPartUnlocksFollowTheCourse()
        {
            CollectionAssert.AreEqual(new[]{SandboxCourseId.BeautifulCorner,SandboxCourseId.TeleportPipes,SandboxCourseId.GravityGarage,SandboxCourseId.DrillDropBridge,SandboxCourseId.FactoryLevel4},SandboxCourseCatalog.Courses);
            Assert.AreEqual(2,SandboxCourseCatalog.Number(SandboxCourseId.TeleportPipes));
            Assert.AreEqual(3,SandboxCourseCatalog.Number(SandboxCourseId.GravityGarage));
            Assert.IsFalse(SandboxCourseCatalog.PartAvailable(SandboxCourseId.BeautifulCorner,PartKind.DownhillLargeWheel));
            Assert.IsFalse(SandboxCourseCatalog.PartAvailable(SandboxCourseId.TeleportPipes,PartKind.DownhillLargeWheel),"Big wheels are the reward for finishing Level 2.");
            Assert.IsTrue(SandboxCourseCatalog.PartAvailable(SandboxCourseId.GravityGarage,PartKind.DownhillLargeWheel));
            Assert.IsTrue(SandboxCourseCatalog.Reached(SandboxCourseId.TeleportPipes,SandboxCourseCatalog.LongBlockCourse));
            Assert.IsFalse(SandboxCourseCatalog.Reached(SandboxCourseId.BeautifulCorner,SandboxCourseCatalog.LongBlockCourse));
            Assert.IsFalse(SandboxCourseCatalog.PartAvailable(SandboxCourseId.GravityGarage,PartKind.DownhillDrill));
            Assert.IsTrue(SandboxCourseCatalog.PartAvailable(SandboxCourseId.DrillDropBridge,PartKind.DownhillDrill));
        }
        [Test] public void SessionRequiresEachCourseAndResetsAllProgress()
        {
            SandboxCampaignSession.Reset();
            SandboxCampaignSession.Advance(true);
            Assert.AreEqual(SandboxCourseId.BeautifulCorner,SandboxCampaignSession.Current);
            SandboxCampaignSession.Complete(SandboxCourseId.FactoryLevel4,true);
            Assert.IsFalse(SandboxCampaignSession.AllComplete);
            SandboxCampaignSession.Complete(SandboxCourseId.BeautifulCorner,true);
            SandboxCampaignSession.Advance(true);
            // Level 2 is Crab Bridge, Level 3 is Wheel Size Challenge.
            Assert.AreEqual(SandboxCourseId.TeleportPipes,SandboxCampaignSession.Current);
            SandboxCampaignSession.Complete(SandboxCourseId.TeleportPipes,true);
            SandboxCampaignSession.Advance(true);
            Assert.AreEqual(SandboxCourseId.GravityGarage,SandboxCampaignSession.Current);
            Assert.IsFalse(SandboxCampaignSession.DrillClaimed,"The drill is granted on entering the drill course.");
            SandboxCampaignSession.Advance(true);
            Assert.AreEqual(SandboxCourseId.GravityGarage,SandboxCampaignSession.Current);
            SandboxCampaignSession.Complete(SandboxCourseId.GravityGarage,true);
            Assert.IsFalse(SandboxCampaignSession.AllComplete);
            SandboxCampaignSession.Advance(true);
            Assert.AreEqual(SandboxCourseId.DrillDropBridge,SandboxCampaignSession.Current);
            Assert.IsTrue(SandboxCampaignSession.DrillClaimed);
            Assert.IsFalse(SandboxCampaignSession.AllComplete);
            SandboxCampaignSession.Complete(SandboxCampaignSession.Current,true);
            SandboxCampaignSession.Advance(true);
            Assert.AreEqual(SandboxCourseId.FactoryLevel4,SandboxCampaignSession.Current);
            SandboxCampaignSession.Complete(SandboxCampaignSession.Current,true);
            Assert.IsTrue(SandboxCampaignSession.AllComplete);
            SandboxCampaignSession.Reset();
            Assert.IsFalse(SandboxCampaignSession.SecondComplete);
            Assert.IsFalse(SandboxCampaignSession.DrillClaimed);
            Assert.IsFalse(SandboxCampaignSession.AllComplete);
        }
    }
}
