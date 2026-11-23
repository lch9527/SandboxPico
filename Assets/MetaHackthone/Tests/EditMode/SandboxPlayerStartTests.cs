using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using PhysicalAssembly.Editor;

namespace PhysicalAssembly.Tests
{
    public sealed class SandboxPlayerStartTests
    {
        GameObject root;
        SandboxLevelLayout layout;
        [SetUp] public void Setup()
        {
            root=new GameObject("Player start fixture");
            layout=root.AddComponent<SandboxLevelLayout>();
            layout.BuildingRoot=new GameObject("Building Area").transform; layout.BuildingRoot.SetParent(root.transform,false);
            layout.SandboxRoot=new GameObject("Sandbox Level").transform; layout.SandboxRoot.SetParent(root.transform,false);
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(root); }

        [Test] public void EnsureCreatesAnEditableMarkerWithAnEditorOnlyCamera()
        {
            Assert.IsTrue(SandboxPlayerStartAuthoring.Ensure(layout));
            Assert.IsNotNull(layout.PlayerStart); Assert.IsTrue(layout.PlayerStart.IsChildOf(layout.BuildingRoot));
            Assert.IsNotNull(layout.PlayerStart.GetComponent<SandboxPlayerStart>(),"Clicking the camera selects the Player Start itself");
            var camera=layout.PlayerStart.GetComponentInChildren<MeshRenderer>(true);
            Assert.IsNotNull(camera); Assert.AreEqual("EditorOnly",camera.tag);
            Assert.Less(Vector3.Distance(camera.transform.position,layout.PlayerStart.position),1e-5f,"The camera preview sits exactly at the Player Start");
            Assert.That(layout.PlayerStartHeight,Is.EqualTo(SandboxLevelLayout.DefaultEyeHeight).Within(1e-4f),"A new Player Start is at standing eye height");
            Assert.IsFalse(SandboxPlayerStartAuthoring.Ensure(layout),"A second pass leaves the designer's marker alone");
        }
        [Test] public void OldFeetMarkerMovesUpToItsCameraWithoutMovingTheCamera()
        {
            SandboxPlayerStartAuthoring.Ensure(layout);
            var camera=layout.PlayerStart.GetComponentInChildren<MeshRenderer>(true).transform;
            // Old layout: Player Start on the floor, camera preview 1.6 m above it.
            layout.PlayerStart.localPosition=new Vector3(.3f,-.2f,-.6f); camera.localPosition=Vector3.up*1.6f;
            var cameraWorld=camera.position;
            Assert.IsTrue(SandboxPlayerStartAuthoring.Ensure(layout));
            Assert.Less(Vector3.Distance(cameraWorld,layout.PlayerStart.position),1e-5f);
            Assert.Less(Vector3.Distance(cameraWorld,camera.position),1e-5f);
            Assert.IsFalse(SandboxPlayerStartAuthoring.Ensure(layout));
        }
        [Test] public void ArmReachCheckWantsCameraOutsideAndPlatformInsideTheBoundaryWithinReach()
        {
            SandboxPlayerStartAuthoring.Ensure(layout);
            var course=new GameObject("Course").AddComponent<DownhillCourse>(); course.transform.SetParent(layout.BuildingRoot,false);
            layout.Course=course;
            course.WorkbenchFrame=new GameObject("Centered workbench frame").transform; course.WorkbenchFrame.SetParent(course.transform,false);
            course.BuildArea=new GameObject("Sandbox building boundary").AddComponent<BuildingArea>(); course.BuildArea.transform.SetParent(course.WorkbenchFrame,false);
            course.BuildArea.Size=Vector3.one*.8f; course.BuildArea.transform.localPosition=new Vector3(0,1,0);
            var pad=GameObject.CreatePrimitive(PrimitiveType.Cube); pad.name="Centered grid pad"; pad.transform.SetParent(course.WorkbenchFrame,false);
            pad.transform.localPosition=new Vector3(0,.9f,0); pad.transform.localScale=new Vector3(.34f,.006f,.34f);
            // Camera just in front of the boundary, looking at it: everything passes.
            layout.PlayerStart.position=new Vector3(0,1.3f,-.45f);
            var good=SandboxPlayerStartAuthoring.Check(layout);
            Assert.IsTrue(good.CameraOutsideBoundary); Assert.IsTrue(good.PlatformInsideBoundary); Assert.IsTrue(good.PlatformInReach);
            Assert.Less(good.NearestBoundary,SandboxPlayerStartAuthoring.ArmReach); Assert.IsTrue(good.Good);
            // Camera inside the boundary fails.
            layout.PlayerStart.position=new Vector3(0,1.1f,0);
            Assert.IsFalse(SandboxPlayerStartAuthoring.Check(layout).CameraOutsideBoundary); Assert.IsFalse(SandboxPlayerStartAuthoring.Check(layout).Good);
            // Too far away: the platform is out of reach.
            layout.PlayerStart.position=new Vector3(0,1.5f,-1.6f);
            var far=SandboxPlayerStartAuthoring.Check(layout);
            Assert.IsTrue(far.CameraOutsideBoundary); Assert.IsFalse(far.PlatformInReach); Assert.IsFalse(far.Good);
        }
        [Test] public void LayoutWithoutMarkerDefaultsToEyeHeightInFrontOfTheBuildingArea()
        {
            layout.BuildingRoot.position=new Vector3(1,2,3);
            var pose=layout.PlayerStartPose(.65f);
            Assert.Less(Vector3.Distance(new Vector3(1,2+SandboxLevelLayout.DefaultEyeHeight,3-.65f),pose.position),1e-4f);
        }
        [Test] public void EditedStartFollowsTheBuildingAreaAndStaysUpright()
        {
            SandboxPlayerStartAuthoring.Ensure(layout);
            var start=layout.PlayerStart.GetComponent<SandboxPlayerStart>();
            // Designer drags it, raises it and tilts it by accident; runtime reads position and yaw.
            start.transform.localPosition=new Vector3(1.2f,.7f,-.4f); start.transform.localRotation=Quaternion.Euler(20,90,10);
            start.KeepLevel();
            Assert.That(start.transform.localPosition.y,Is.EqualTo(.7f).Within(1e-5f),"The authored height is kept");
            Assert.That(Quaternion.Angle(start.transform.localRotation,Quaternion.Euler(0,90,0)),Is.LessThan(.01f));
            // Moving and turning the Building Area carries the start with it.
            layout.BuildingRoot.SetPositionAndRotation(new Vector3(3,1,5),Quaternion.Euler(0,180,0));
            var pose=layout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance);
            Assert.Less(Vector3.Distance(new Vector3(3-1.2f,1.7f,5+.4f),pose.position),1e-4f);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0,270,0),pose.rotation),.01f);
            Assert.That(layout.PlayerStartHeight,Is.EqualTo(.7f).Within(1e-4f));
        }
    }
}
