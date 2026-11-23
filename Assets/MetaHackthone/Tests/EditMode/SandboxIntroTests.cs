using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace PhysicalAssembly.Tests {
    public class SandboxIntroTests {
        [Test] public void EntryGateDoesNotReplayOrEnterDuringDesignOrLaterLevels(){
            Assert.IsTrue(SandboxFirstEntryIntro.Eligible(true,false,SandboxCourseId.BeautifulCorner,false));
            Assert.IsFalse(SandboxFirstEntryIntro.Eligible(true,false,SandboxCourseId.BeautifulCorner,true));
            Assert.IsFalse(SandboxFirstEntryIntro.Eligible(true,true,SandboxCourseId.BeautifulCorner,false));
            Assert.IsFalse(SandboxFirstEntryIntro.Eligible(false,false,SandboxCourseId.BeautifulCorner,false));
            Assert.IsFalse(SandboxFirstEntryIntro.Eligible(true,false,SandboxCourseId.FactoryLevel4,false));
        }
        [Test] public void PlayersWhoSawTheOldIntroSeeTheNewOne(){
            Assert.AreNotEqual("Sandbox.Intro.Almost.v1",SandboxFirstEntryIntro.SeenKey);
            Assert.AreEqual(SandboxIntroCars.Count,SandboxFirstEntryIntro.RunSeconds.Length);
            // The core builds at 180 beats a minute, and every part is in the air before the beat it lands on.
            Assert.AreEqual(1/3f,SandboxFirstEntryIntro.BuildBeat,.0001f);Assert.Less(SandboxFirstEntryIntro.PartFlight,SandboxFirstEntryIntro.BuildBeat*4);
        }
        // The pull is drawn with real geometry and two shaders of its own; nothing is laid over the whole view.
        [Test] public void ThePullIsABlueprintAndTethers(){
            Assert.IsNotNull(Resources.Load<Shader>("SandboxHologram"),"Blueprint and held-part glow");
            Assert.IsNotNull(Resources.Load<Shader>("SandboxTether"),"The core's tether");
            Assert.IsNull(Resources.Load<Shader>("SandboxSpaceLens"),"The black-hole lens is gone");
            Assert.Greater(SandboxFirstEntryIntro.TetherReach,0);Assert.Less(SandboxFirstEntryIntro.TetherReach,SandboxFirstEntryIntro.BuildBeat*4-SandboxFirstEntryIntro.PartFlight+.001f,"The first part is caught before it has to fly");
        }
        // The core's two attempts, built by the game's own joining rules on either side.
        [TestCase(1f)] [TestCase(-1f)] public void CoreBuildsTwoCarsThatCannotWorkFromLevelOneParts(float side){
            var root=new GameObject("Intro car fixture");
            try{
                var world=root.AddComponent<AssemblyWorld>(); const float scale=.8f; float unit=BuildingGeometry.Unit*scale;
                // Car one is the nonsense stack: a mast, a bar, a wheel lying on the bar and a wheel for a nose.
                var first=SandboxIntroCars.Build(world,scale,new Pose(new Vector3(0,5,0),Quaternion.identity),0,side);
                Assert.IsTrue(first.Core.BuildingCore);Assert.AreEqual(5,first.Parts.Count);
                Assert.AreEqual(2,first.Count(PartKind.DownhillSmallWheel));Assert.AreEqual(1,first.Count(PartKind.DownhillLongBlock));Assert.AreEqual(1,first.Count(PartKind.DownhillShortBlock));
                Assert.AreEqual(5,world.GetConnectedParts(first.Core.InstanceId).Count,"Everything is joined to the core");
                // Car two is nearly the starter car: the core, the long block ahead of it and two wheels.
                var turned=Quaternion.Euler(0,35,0);
                var second=SandboxIntroCars.Build(world,scale,new Pose(new Vector3(0,9,0),turned),1,side);
                Assert.AreEqual(4,second.Parts.Count);
                Assert.AreEqual(2,second.Count(PartKind.DownhillSmallWheel));Assert.AreEqual(1,second.Count(PartKind.DownhillLongBlock));Assert.AreEqual(0,second.Count(PartKind.DownhillShortBlock));
                Assert.AreEqual(4,world.GetConnectedParts(second.Core.InstanceId).Count);
                foreach(var part in first.Parts.Concat(second.Parts)){
                    Assert.IsTrue(part.BuildingCore||SandboxCourseCatalog.PartAvailable(SandboxCourseId.BeautifulCorner,part.Kind),part.Kind+" is a Level 1 part");
                    foreach(var body in part.Bodies)Assert.IsTrue(body.Rigidbody.isKinematic,"Cars wait weightless until their run");
                }
                // Car one cannot stand: its weight hangs well to one side of the core it rests on, toward the viewer.
                float mass=first.Bodies.Sum(b=>b.Rigidbody.mass);
                var centre=first.Bodies.Aggregate(Vector3.zero,(sum,b)=>sum+b.Rigidbody.worldCenterOfMass*b.Rigidbody.mass)/mass;
                Assert.Greater((centre.x-first.Core.MainBody.Rigidbody.position.x)*side,.25f*unit);
                // Car two cannot roll straight: both wheels are on the same side of the long block, front and back.
                var wheels=second.Parts.Where(p=>p.Kind==PartKind.DownhillSmallWheel).Select(p=>Quaternion.Inverse(turned)*(p.MainBody.Rigidbody.position-second.Core.MainBody.Rigidbody.position)).ToArray();
                foreach(var wheel in wheels){Assert.Greater(wheel.x*side*SandboxIntroCars.WheelSide,.6f*unit,"Both wheels on the same side");Assert.AreEqual(0,wheel.y,.05f*unit,"on the block's axle line");}
                Assert.Greater(Mathf.Abs(wheels[0].z-wheels[1].z),1.5f*unit,"one at the front, one at the back");
                // Letting go leaves the core alone and every other part free of it.
                var dropped=SandboxIntroCars.DetachCore(world,first);
                Assert.AreEqual(4,dropped.Length);Assert.AreEqual(1,world.GetConnectedParts(first.Core.InstanceId).Count);
                Assert.AreEqual(3,SandboxIntroCars.DetachCore(world,second).Length);Assert.AreEqual(1,world.GetConnectedParts(second.Core.InstanceId).Count);
            }finally{Object.DestroyImmediate(root);}
        }
    }
}
