using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    public sealed partial class DownhillTests
    {
        [UnityTest] public IEnumerator FixedMenuStaysVisibleAndStartHoverOwnsPreview()
        {
            level.Course.SandboxPolish=true;
            var ui=level.Presentation.Interface;
            Assert.IsTrue(ui.LeftPanel.gameObject.activeInHierarchy);
            var position=ui.LeftPanel.position;
            AddWheel(workshop,level.Kit[1],1,0);
            yield return null;
            Assert.IsFalse(level.SoulStreamActive);
            level.Presentation.ReadyButton.SetInteractionState(true,false);
            yield return null;
            Assert.IsTrue(level.SoulStreamActive);
            level.Presentation.ReadyButton.SetInteractionState(false,false);
            yield return null;
            Assert.IsFalse(level.SoulStreamActive);
            Assert.AreEqual(position,ui.LeftPanel.position);
            Assert.IsTrue(level.TryStart());
            yield return null;
            Assert.IsTrue(ui.LeftPanel.gameObject.activeInHierarchy);
            Assert.IsFalse(level.SoulStreamActive);
            level.RestoreBuildImmediately();
            Assert.IsTrue(ui.LeftPanel.gameObject.activeInHierarchy);
        }
        [UnityTest] public IEnumerator SandboxBoundaryReturnsOnlyReleasedLooseParts()
        {
            level.Course.SandboxPolish=true; yield return null;
            var part=level.Kit[5]; var spawn=part.MainBody.Rigidbody.position;
            var outside=level.Course.BuildArea.transform.TransformPoint(level.Course.BuildArea.Size*2);
            Assert.IsTrue(workshop.World.TryGrab(0,part,Grip(part)));
            part.MainBody.Rigidbody.position=outside;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.AreEqual(0,level.BoundaryReturnCount);
            workshop.World.Release(0,false); part.MainBody.Rigidbody.position=outside; part.MainBody.transform.position=outside;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.IsTrue(level.IsReturning(part));
            Assert.IsFalse(workshop.World.TryGrab(0,part,Grip(part)));
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.AreEqual(0,level.BoundaryReturnCount);
            Assert.That(Vector3.Distance(spawn,part.MainBody.Rigidbody.position),Is.LessThan(.001f));
            foreach(var body in part.Bodies) foreach(var collider in body.Colliders) Assert.IsTrue(collider.enabled);
        }
        [UnityTest] public IEnumerator SandboxBoundaryResetClearsEffectsAndPreservesCoreAttachments()
        {
            level.Course.SandboxPolish=true; yield return null;
            AddWheel(workshop,level.Kit[1],1,0);
            var attached=level.Kit[1]; var loose=level.Kit[5];
            var outside=level.Course.BuildArea.transform.TransformPoint(level.Course.BuildArea.Size*2);
            attached.MainBody.Rigidbody.position=outside; loose.MainBody.Rigidbody.position=outside;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.IsFalse(level.IsReturning(attached)); Assert.IsTrue(level.IsReturning(loose));
            Assert.IsTrue(level.TryResetBuild()); Assert.AreEqual(0,level.BoundaryReturnCount);
            yield return null;
            foreach(var part in level.Kit) foreach(var renderer in part.VisualRenderers) Assert.IsTrue(renderer.enabled);
        }
        [UnityTest] public IEnumerator SandboxLooseHighlightsFollowAttachmentGraph()
        {
            level.Course.SandboxPolish=true;
            var highlights=root.AddComponent<SandboxPartHighlights>(); highlights.Level=level;
            yield return null; int loose=SandboxPartHighlights.Draws.Count; Assert.Greater(loose,0);
            AddWheel(workshop,level.Kit[1],1,0); yield return null;
            Assert.Less(SandboxPartHighlights.Draws.Count,loose);
            Assert.IsTrue(workshop.World.TryDetachPart(level.Kit[1])); yield return null;
            Assert.AreEqual(loose,SandboxPartHighlights.Draws.Count);
        }
        [Test] public void BuildingVolumeTestsAllFacesInItsOwnFrame()
        {
            var go=new GameObject("Rotated building cube"); go.transform.SetParent(root.transform);
            go.transform.SetPositionAndRotation(new Vector3(3,2,1),Quaternion.Euler(0,73,0));
            go.transform.localScale=Vector3.one*.4f; var area=go.AddComponent<BuildingArea>(); area.Size=Vector3.one;
            for(int axis=0;axis<3;axis++) for(int sign=-1;sign<=1;sign+=2) {
                var point=Vector3.zero; point[axis]=sign*.49f; Assert.IsTrue(area.Contains(go.transform.TransformPoint(point)));
                point[axis]=sign*.51f; Assert.IsFalse(area.Contains(go.transform.TransformPoint(point)));
            }
        }
    }
}
