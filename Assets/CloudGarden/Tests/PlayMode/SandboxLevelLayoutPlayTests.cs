using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    public sealed class SandboxLevelLayoutPlayTests
    {
        DownhillController level;
        [UnitySetUp] public IEnumerator Setup()
        {
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("DownhillSandbox"); yield return null;
            level=Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
            level.Settings.TransitionDuration=0; level.Settings.CountdownStep=.01f; level.Settings.GoDuration=.01f;
            level.LaunchDecomposeSeconds=level.LaunchTravelSeconds=level.LaunchReconstructSeconds=.01f;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(level) level.RestoreBuildImmediately(); yield return null;
            DesignModeSession.SetEnabled(false); SandboxCampaignSession.Reset();
            SceneManager.LoadScene("MainMenu"); yield return null;
        }
        [UnityTest] public IEnumerator FourCoursesMoveTurnAndScaleFromHalfToDouble() => Exercise(.5f,2f);
        [UnityTest] public IEnumerator FourCoursesMoveTurnAndScaleFromDoubleToHalf() => Exercise(2f,.5f);

        IEnumerator Exercise(float first,float second)
        {
            var layouts=level.Workshop.Layout.LevelLayouts;
            foreach(var binding in layouts) {
                Configure(binding,binding.CourseId==SandboxCourseId.GravityGarage?second:first);
                Assert.IsFalse(binding.gameObject.activeInHierarchy);
            }
            for(int number=1;number<=4;number++) {
                float scale=number%2==1?first:second;
                var binding=level.ActiveLayout; Assert.IsNotNull(binding);
                if(level.FactoryLevel) {
                    Configure(binding,scale);
                    Assert.IsTrue(level.TryBuildFactoryCar());
                }
                Assert.IsTrue(binding.Validate(out var error),error);
                var markers=level.Workshop.Layout.Spawns;
                Assert.That(level.Core.GeometryScale,Is.EqualTo(markers.First(s=>s && s.Kind==PartKind.DownhillCore).SizeMultiplier).Within(.0001f));
                Assert.That(level.Workshop.World.SocketDistance,Is.EqualTo(level.Settings.SnapRadius*scale).Within(.0001f));
                if(number==4) {
                    Assert.IsTrue(level.TryLoadFactoryCar(level.FactoryLevel.TestCar));
                    float size=level.Core.GeometryScale;
                    Assert.IsTrue(level.TryLoadFactoryCar(level.FactoryLevel.TestCar));
                    Assert.AreEqual(size,level.Core.GeometryScale);
                } else if(number==1) Assert.IsTrue(level.TryLoadDesignerExample());
                Assert.IsNotEmpty(level.Workshop.World.Connections);
                Assert.IsTrue(level.Kit.SelectMany(p=>p.Bodies).All(b=>Vector3.Distance(b.transform.lossyScale,Vector3.one)<.0001f));
                // Whole-arena placement must apply once to authored roots and unscaled bodies.
                var arena=level.Workshop.Arena;
                var localCore=arena.InverseTransformPoint(level.Core.MainBody.Rigidbody.position);
                var localBuilding=binding.BuildingRoot.localToWorldMatrix;
                var localRoad=binding.SandboxRoot.localToWorldMatrix;
                var beforeArena=arena.localToWorldMatrix;
                var pose=new Pose(arena.position+new Vector3(.2f,0,-.3f),Quaternion.Euler(0,25,0)*arena.rotation);
                level.Workshop.ApplyPlayArea(pose);
                Assert.Less(Vector3.Distance(arena.TransformPoint(localCore),level.Core.MainBody.Rigidbody.position),.0001f);
                var delta=arena.localToWorldMatrix*beforeArena.inverse;
                AssertMatrix(delta*localBuilding,binding.BuildingRoot.localToWorldMatrix);
                AssertMatrix(delta*localRoad,binding.SandboxRoot.localToWorldMatrix);
                var desktop=level.Workshop.GetComponent<DesktopInputAdapter>();
                if(desktop && desktop.IsDesktop) {
                    desktop.FramePlatform();
                    Assert.That(Vector3.Distance(desktop.View.transform.position,level.Presentation.AssemblyAnchor.position),Is.EqualTo(2.3f*scale).Within(.001f));
                }
                var groupPose=binding.SandboxRoot.localToWorldMatrix;
                var buildPose=binding.BuildingRoot.localToWorldMatrix;
                var ids=level.Kit.Select(p=>p.InstanceId).ToArray();
                int connections=level.Workshop.World.Connections.Count;
                for(int attempt=0;attempt<2;attempt++) {
                    Assert.IsTrue(level.TryStart(),level.Workshop.Session.Status);
                    float until=Time.realtimeSinceStartup+5;
                    while(level.State!=DownhillState.Running && Time.realtimeSinceStartup<until) yield return null;
                    Assert.AreEqual(DownhillState.Running,level.State,level.Workshop.Session.Status);
                    Assert.That(level.SimulationCore.GeometryScale,Is.EqualTo(level.TestScale).Within(.0001f));
                    Assert.IsTrue(level.TryRetry()); yield return null;
                    level.RestoreBuildImmediately(); yield return null;
                    Assert.AreEqual(groupPose,binding.SandboxRoot.localToWorldMatrix);
                    Assert.AreEqual(buildPose,binding.BuildingRoot.localToWorldMatrix);
                    CollectionAssert.AreEqual(ids,level.Kit.Select(p=>p.InstanceId));
                }
                if(number<4) {
                    SandboxCampaignSession.Complete(level.CurrentCourse,true);
                    Assert.IsTrue(level.TryAdvanceLevel()); yield return null;
                    if(number<3) {
                        CollectionAssert.AreEqual(ids,level.Kit.Select(p=>p.InstanceId));
                        Assert.AreEqual(connections,level.Workshop.World.Connections.Count);
                    }
                }
            }
        }
        static void AssertMatrix(Matrix4x4 expected,Matrix4x4 actual)
        {
            for(int i=0;i<16;i++) Assert.That(actual[i],Is.EqualTo(expected[i]).Within(.0001f));
        }
        static void Configure(SandboxLevelLayout layout,float scale)
        {
            var roadBefore=layout.SandboxRoot.localToWorldMatrix;
            layout.BuildingRoot.localPosition+=new Vector3(1,.25f,-.5f);
            layout.BuildingRoot.localRotation=Quaternion.Euler(0,90,0);
            layout.BuildingRoot.localScale=Vector3.one*scale;
            Assert.AreEqual(roadBefore,layout.SandboxRoot.localToWorldMatrix);
            var buildingBefore=layout.BuildingRoot.localToWorldMatrix;
            layout.SandboxRoot.localPosition+=new Vector3(-1,.3f,2);
            layout.SandboxRoot.localRotation=Quaternion.Euler(0,-90,0);
            layout.SandboxRoot.localScale=Vector3.one*(scale==.5f?2:.5f);
            Assert.AreEqual(buildingBefore,layout.BuildingRoot.localToWorldMatrix);
        }
    }
}
