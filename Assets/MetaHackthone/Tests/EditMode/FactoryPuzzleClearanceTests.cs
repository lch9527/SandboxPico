using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PhysicalAssembly.Tests
{
    public sealed class FactoryPuzzleClearanceTests
    {
        GameObject root;
        SandboxTrackGenerator track;
        SandboxCrabBridge puzzle;
        AssemblyBody front,core;
        object section;
        MethodInfo intersects;

        [SetUp] public void Setup()
        {
            root=new GameObject("Clearance fixture");
            track=root.AddComponent<SandboxTrackGenerator>();
            track.Samples=new Vector3[101];
            for(int i=0;i<track.Samples.Length;i++) track.Samples[i]=Vector3.forward*i;
            var obstacle=new GameObject("Puzzle"); obstacle.transform.SetParent(root.transform);
            puzzle=obstacle.AddComponent<SandboxCrabBridge>(); puzzle.Track=track; puzzle.Distance=50; puzzle.Length=4;
            core=Body("Core",new Vector3(0,0,43)); front=Body("Front wheel",new Vector3(0,0,48));
            var type=typeof(SandboxFactoryPathAssist).GetNestedType("PuzzleSection",BindingFlags.NonPublic);
            section=Activator.CreateInstance(type,puzzle,1f); intersects=type.GetMethod("Intersects");
            Physics.SyncTransforms();
        }
        AssemblyBody Body(string name,Vector3 position)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(root.transform);
            go.transform.position=position; go.transform.localScale=Vector3.one*.2f;
            var body=go.AddComponent<AssemblyBody>(); body.Initialize(); body.Rigidbody.isKinematic=true; return body;
        }
        bool Contains(params AssemblyBody[] bodies)=>(bool)intersects.Invoke(section,new object[]{bodies});
        [TearDown] public void Cleanup() { UnityEngine.Object.DestroyImmediate(root); }

        [Test] public void FrontWheelDisablesAssistanceBeforeCoreEntersPuzzle()
        {
            Assert.IsFalse(Contains(core)); Assert.IsTrue(Contains(core,front));
            front.transform.position=Vector3.forward*60; Physics.SyncTransforms();
            Assert.IsFalse(Contains(core,front));
        }
        [Test] public void CachedClearanceFollowsTrackRepositionAndScale()
        {
            Assert.IsTrue(Contains(front));
            // Vehicle stays in world space when the track is repositioned.
            front.transform.SetParent(null,true); core.transform.SetParent(null,true);
            try {
                root.transform.position=Vector3.right*10; root.transform.localScale=Vector3.one*2;
                Physics.SyncTransforms(); Assert.IsFalse(Contains(front));
                front.transform.position=root.transform.TransformPoint(Vector3.forward*50);
                Physics.SyncTransforms(); Assert.IsTrue(Contains(front));
            } finally { UnityEngine.Object.DestroyImmediate(front.gameObject); UnityEngine.Object.DestroyImmediate(core.gameObject); }
        }
        [Test] public void DisabledCollidersCannotKeepAssistanceSuppressed()
        {
            Assert.IsTrue(Contains(front)); front.Colliders[0].enabled=false;
            Assert.IsFalse(Contains(front));
        }
    }
}
