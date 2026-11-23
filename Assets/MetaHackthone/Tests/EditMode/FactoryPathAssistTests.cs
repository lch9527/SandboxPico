using NUnit.Framework;
using UnityEngine;

namespace PhysicalAssembly.Tests
{
    public class FactoryPathAssistTests
    {
        GameObject root;
        AssemblyBody Body(Vector3 position)
        {
            var go=new GameObject("Test body"); go.transform.SetParent(root.transform);
            go.transform.position=position;
            var body=go.AddComponent<AssemblyBody>(); body.Initialize(); return body;
        }
        [SetUp] public void Setup() { root=new GameObject("Path assist test"); }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(root); }
        [Test]
        public void LimitedGuidePreservesPitchRollAndVerticalMotion()
        {
            var core=Body(new Vector3(1,2,3));
            var rotation=Quaternion.Euler(12,0,15);
            core.Rigidbody.rotation=rotation;
            core.Rigidbody.linearVelocity=new Vector3(0,-3,4);
            core.Rigidbody.angularVelocity=new Vector3(2,0,3);
            SandboxFactoryPathAssist.ApplyLimitedGuide(new[]{core},core.Rigidbody,Vector3.zero,Vector3.forward);
            Assert.That(core.Rigidbody.position.y,Is.EqualTo(2));
            Assert.That(core.Rigidbody.linearVelocity.y,Is.EqualTo(-3));
            Assert.That(Vector3.Dot(core.Rigidbody.rotation*Vector3.up,Vector3.up),
                Is.EqualTo(Vector3.Dot(rotation*Vector3.up,Vector3.up)).Within(.0001f));
            Assert.That(core.Rigidbody.angularVelocity.magnitude,Is.EqualTo(Mathf.Sqrt(13)).Within(.0001f));
        }
        [Test]
        public void LimitedGuideCannotRecoverTippedCar()
        {
            var core=Body(new Vector3(1,2,3));
            var rotation=Quaternion.Euler(0,30,80);
            core.Rigidbody.rotation=rotation;
            var position=core.Rigidbody.position;
            SandboxFactoryPathAssist.ApplyLimitedGuide(new[]{core},core.Rigidbody,Vector3.zero,Vector3.forward);
            Assert.That(core.Rigidbody.position,Is.EqualTo(position));
            Assert.That(Quaternion.Angle(core.Rigidbody.rotation,rotation),Is.LessThan(.001f));
        }
        [Test] public void CenteringKeepsHeightForwardMotionAndWheelSpin()
        {
            var core=Body(new Vector3(1,2,3)); var wheel=Body(new Vector3(.5f,1.5f,3));
            wheel.Part=wheel.gameObject.AddComponent<AssemblyPart>(); wheel.Part.Kind=PartKind.DownhillSmallWheel;
            var bodies=new[]{core,wheel};
            foreach(var b in bodies) { b.Rigidbody.linearVelocity=new Vector3(2,-3,4); b.Rigidbody.angularVelocity=new Vector3(7,5,0); }
            SandboxFactoryPathAssist.ApplyHorizontalGuide(bodies,core.Rigidbody,new Vector3(0,0,3),Vector3.forward);
            Assert.That(Vector3.Distance(core.Rigidbody.position,new Vector3(0,2,3)),Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(wheel.Rigidbody.position,new Vector3(-.5f,1.5f,3)),Is.LessThan(.0001f));
            foreach(var b in bodies) {
                Assert.That(Vector3.Distance(b.Rigidbody.linearVelocity,new Vector3(0,-3,4)),Is.LessThan(.0001f),"Do not propel or support a falling car.");
            }
            Assert.That(Vector3.Distance(wheel.Rigidbody.angularVelocity,new Vector3(7,5,0)),Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(core.Rigidbody.angularVelocity,new Vector3(7,5,0)),Is.LessThan(.0001f),"Contacts must resolve pitch/roll, not an orientation reset.");
        }
        [Test] public void TurningMovesWholeAssemblyWithoutStretchingJointsOrAddingSpeed()
        {
            var core=Body(new Vector3(0,2,3)); var wheel=Body(new Vector3(1,1.5f,3));
            var bodies=new[]{core,wheel}; float separation=Vector3.Distance(core.Rigidbody.position,wheel.Rigidbody.position);
            foreach(var b in bodies) b.Rigidbody.linearVelocity=Vector3.forward*4;
            var tangent=Quaternion.Euler(0,30,0)*Vector3.forward;
            SandboxFactoryPathAssist.ApplyHorizontalGuide(bodies,core.Rigidbody,new Vector3(0,0,3),tangent);
            Assert.That(Vector3.Angle(core.Rigidbody.rotation*Vector3.forward,tangent),Is.LessThan(.01f));
            Assert.That(Vector3.Distance(core.Rigidbody.position,wheel.Rigidbody.position),Is.EqualTo(separation).Within(.0001f));
            Assert.That(core.Rigidbody.position.y,Is.EqualTo(2).Within(.0001f));
            Assert.That(wheel.Rigidbody.position.y,Is.EqualTo(1.5f).Within(.0001f));
            Assert.That(core.Rigidbody.linearVelocity.magnitude,Is.EqualTo(4).Within(.0001f));
        }
        [Test] public void UnsupportedCarKeepsItsPositionAndFallingVelocity()
        {
            var core=Body(new Vector3(1,2,3)); var rotation=Quaternion.Euler(20,30,40); core.Rigidbody.rotation=rotation;
            var wheel=Body(new Vector3(.5f,1.5f,3)); var wheelPosition=wheel.Rigidbody.position;
            core.Rigidbody.angularVelocity=new Vector3(2,3,4);
            var velocity=new Vector3(2,-5,4); core.Rigidbody.linearVelocity=velocity;
            SandboxFactoryPathAssist.ApplyHorizontalGuide(new[]{core,wheel},core.Rigidbody,Vector3.zero,Vector3.forward,false);
            Assert.That(Vector3.Distance(core.Rigidbody.position,new Vector3(1,2,3)),Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(core.Rigidbody.linearVelocity,velocity),Is.LessThan(.0001f),"No support, braking, or propulsion in a gap.");
            Assert.That(Quaternion.Angle(core.Rigidbody.rotation,rotation),Is.LessThan(.01f));
            Assert.That(wheel.Rigidbody.position,Is.EqualTo(wheelPosition),"An airborne wheel must not orbit a corrected core.");
            Assert.That(core.Rigidbody.angularVelocity,Is.EqualTo(new Vector3(2,3,4)));
        }

        [Test]
        public void MixedWheelRadiiKeepBothAxlesOnTheRoadThroughGuidance()
        {
            // A short wheelbase exaggerates the observed bug: unequal radii require
            // pitch relative to a flat road. Flattening the core penetrates one axle.
            const float large=.12f,small=.08f,wheelbase=.22f;
            float pitch=-Mathf.Asin((large-small)/wheelbase)*Mathf.Rad2Deg;
            var rotation=Quaternion.Euler(pitch,0,0);
            var core=Body(new Vector3(0,.1f,0)); core.Rigidbody.rotation=rotation;
            var front=Body(core.Rigidbody.position+rotation*Vector3.forward*(wheelbase*.5f));
            var rear=Body(core.Rigidbody.position-rotation*Vector3.forward*(wheelbase*.5f));
            var bodies=new[]{core,front,rear};
            for(int tick=0;tick<100;tick++) SandboxFactoryPathAssist.ApplyHorizontalGuide(bodies,core.Rigidbody,Vector3.zero,Vector3.forward);
            Assert.That(front.Rigidbody.position.y-large,Is.EqualTo(0).Within(.00001f),"Large front wheel must not be pushed into the road.");
            Assert.That(rear.Rigidbody.position.y-small,Is.EqualTo(0).Within(.00001f),"Small rear wheel must remain supported.");
            Assert.That(Quaternion.Angle(core.Rigidbody.rotation,rotation),Is.LessThan(.01f));
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void PublishedPuzzleCoursesUseLimitedAssistance(int level)
        {
            var factory=UnityEditor.AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab");
            Assert.IsNotNull(factory);
            Assert.IsTrue(factory.CenterPathAssist);
            Assert.IsTrue(factory.LimitedPathAssist,"Puzzle contacts must not fight orientation assistance.");
        }

        // The final run keeps full assistance through its puzzles by design (2026-11-17): the balance
        // stabilizer and the drill car were tuned with it, and the designer chose to keep it.
        [Test]
        public void FinalRunKeepsUnlimitedAssistance()
        {
            var factory=UnityEditor.AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>("Assets/MetaHackthone/Generated/Campaign/Level5.prefab");
            Assert.IsNotNull(factory);
            Assert.IsTrue(factory.CenterPathAssist);
            Assert.IsFalse(factory.LimitedPathAssist);
        }
    }
}
