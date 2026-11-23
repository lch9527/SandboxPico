using System.Linq;
using NUnit.Framework;
using PhysicalAssembly.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace PhysicalAssembly.Tests
{
    public class FactoryPuzzleTests
    {
        Scene scene; SandboxTrackGenerator track; SimulationMode simulationMode;
        [SetUp] public void SetUp()
        {
            SandboxTrackEditing.Suspended=true; scene=SceneManager.GetActiveScene();
            simulationMode=Physics.simulationMode; Physics.simulationMode=SimulationMode.Script;
            var go=new GameObject("Test track"); track=SandboxFactoryAuthoring.InitializeTrack(go);
            var spline=new Spline(); spline.Add(new BezierKnot(new float3(0,0,0))); spline.Add(new BezierKnot(new float3(0,0,20)));
            track.Curve.Spline=spline; track.Supports=false; track.Guardrails=false; track.DefaultWidth=4;
        }
        [TearDown] public void TearDown()
        {
            if(track) { foreach(var p in SandboxPuzzleEditing.Puzzles(track)) Object.DestroyImmediate(p.gameObject); Object.DestroyImmediate(track.gameObject); }
            Physics.simulationMode=simulationMode; SandboxTrackEditing.Suspended=false;
        }
        T Puzzle<T>(float distance) where T:SandboxPuzzle
        {
            var go=new GameObject(typeof(T).Name); var p=go.AddComponent<T>(); p.Track=track; p.Distance=distance; return p;
        }
        bool Ground(float x,float z) => scene.GetPhysicsScene().Raycast(new Vector3(x,2,z),Vector3.down,out _,3);
        [Test] public void NewPalettePuzzlesUseRedRollersAndHalfUnitRedTargets()
        {
            var red=AssetDatabase.LoadAssetAtPath<Material>("Assets/CreekDiorama/Materials/Goal vermilion.mat");
            Assert.IsNotNull(red);
            var roller=(SandboxRoller)SandboxPuzzleEditing.Add(track,typeof(SandboxRoller),5);
            var bridge=(SandboxDropBridge)SandboxPuzzleEditing.Add(track,typeof(SandboxDropBridge),12);
            Assert.AreSame(red,roller.Material);Assert.AreSame(red,bridge.TargetMaterial);Assert.AreEqual(.5f,bridge.TargetSize);
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            Assert.AreSame(red,roller.Generated.GetComponentInChildren<Renderer>().sharedMaterial);
            Assert.AreSame(red,bridge.TargetCollider.GetComponent<Renderer>().sharedMaterial);
            Assert.AreEqual(Vector3.one*.5f,bridge.TargetCollider.transform.localScale);
            Assert.AreSame(track.TimberMaterial,bridge.Deck.GetComponentInChildren<Renderer>().sharedMaterial);
        }
        [TestCase(1f)] [TestCase(.2f)]
        public void RailSectionsMeasureThirtyCentimetersAndKeepIndependentOverrides(float scale)
        {
            track.transform.localScale=Vector3.one*scale;track.Guardrails=true;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            var road=track.Generated.transform.Find("Road").GetComponent<MeshFilter>().sharedMesh.vertices;
            track.RailSectionLength=.3f;
            track.LeftRailSections.Add(new SandboxRailSectionSettings{Index=1,Enabled=false});
            track.RightRailSections.Add(new SandboxRailSectionSettings{Index=2,Height=1.2f,Thickness=.2f});
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track),track.ValidationError);
            CollectionAssert.AreEqual(road,track.Generated.transform.Find("Road").GetComponent<MeshFilter>().sharedMesh.vertices);
            int count=Mathf.CeilToInt(20*scale/.3f-.00001f);
            var left=track.Generated.transform.Find("Left Rail");var right=track.Generated.transform.Find("Right Rail");
            Assert.AreEqual(count-1,left.childCount);Assert.AreEqual(count,right.childCount);
            Assert.IsNull(left.Find("Section 002"));Assert.IsNotNull(right.Find("Section 002"));
            for(int i=0;i<count;i++) {
                var piece=right.Find($"Section {i+1:000}");var mesh=piece.GetComponent<MeshFilter>().sharedMesh;
                float start=i*.3f,end=Mathf.Min(20*scale,(i+1)*.3f);
                Assert.That(mesh.bounds.min.z*scale,Is.EqualTo(start).Within(.0001f));
                Assert.That(mesh.bounds.max.z*scale,Is.EqualTo(end).Within(.0001f));
                Assert.AreSame(mesh,piece.GetComponent<MeshCollider>().sharedMesh);
            }
            Assert.That(right.Find("Section 003").GetComponent<MeshFilter>().sharedMesh.bounds.size.y,Is.EqualTo(1.2f).Within(.0001f));
            string before=track.BakedSignature;track.LeftRailSections[0].Enabled=true;
            Assert.AreNotEqual(before,SandboxTrackBaker.Signature(track));
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track),track.ValidationError);
            Assert.IsNotNull(track.Generated.transform.Find("Left Rail/Section 002"));
        }
        [Test] public void RailSectionsKeepDropBridgeOpeningsClear()
        {
            track.transform.localScale=Vector3.one*.2f;track.Guardrails=true;track.RailSectionLength=.3f;
            Puzzle<SandboxDropBridge>(10);
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);Physics.SyncTransforms();
            foreach(string name in new[]{"Left Rail","Right Rail"}) {
                var rails=track.Generated.transform.Find(name);
                float x=name=="Left Rail"?-2.05f:2.05f;
                foreach(var collider in rails.GetComponentsInChildren<Collider>())
                    Assert.IsFalse(collider.Raycast(new Ray(track.transform.TransformPoint(new Vector3(x,2,10)),Vector3.down),out _,1),"Rail section fills the bridge opening.");
                Assert.IsFalse(rails.Find("Section 007"),"A section wholly inside the opening must be omitted.");
            }
        }
        [TestCase(true,true)] [TestCase(true,false)] [TestCase(false,true)] [TestCase(false,false)]
        public void RailSidesHaveIndependentGeometryAndCollision(bool left,bool right)
        {
            track.Guardrails=true;track.LeftRailEnabled=left;track.RightRailEnabled=right;
            track.LeftRailHeight=.4f;track.LeftRailThickness=.13f;
            track.RightRailHeight=.9f;track.RightRailThickness=.22f;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            foreach(int side in new[]{-1,1}) {
                var rail=track.Generated.transform.Find(side<0?"Left Rail":"Right Rail");
                Assert.AreEqual(side<0?left:right,rail!=null);
                if(!rail) continue;
                var mesh=rail.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.bounds.size.y,Is.EqualTo(side<0?.4f:.9f).Within(.0001f));
                Assert.That(mesh.bounds.size.x,Is.EqualTo(side<0?.13f:.22f).Within(.0001f));
                Assert.That(side<0?mesh.bounds.max.x:mesh.bounds.min.x,Is.EqualTo(side*2).Within(.0001f));
                Assert.AreSame(mesh,rail.GetComponent<MeshCollider>().sharedMesh);
                Assert.AreSame(track.RailPhysics,rail.GetComponent<MeshCollider>().sharedMaterial);
            }
        }
        [Test] public void RailEditsInvalidateBakeAndPreserveLegacyDimensionsAndRoad()
        {
            track.Guardrails=true;track.RailHeight=1.1f;track.RailThickness=.16f;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            var road=track.Generated.transform.Find("Road").GetComponent<MeshFilter>().sharedMesh.vertices;
            var right=track.Generated.transform.Find("Right Rail").GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.That(track.Generated.transform.Find("Left Rail").GetComponent<MeshFilter>().sharedMesh.bounds.size.y,Is.EqualTo(1.1f).Within(.0001f));
            string before=track.BakedSignature;
            track.LeftRailHeight=.5f;
            Assert.AreNotEqual(before,SandboxTrackBaker.Signature(track));
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track),track.ValidationError);
            CollectionAssert.AreEqual(road,track.Generated.transform.Find("Road").GetComponent<MeshFilter>().sharedMesh.vertices);
            CollectionAssert.AreEqual(right,track.Generated.transform.Find("Right Rail").GetComponent<MeshFilter>().sharedMesh.vertices);
            var valid=track.Generated;track.LeftRailThickness=0;
            Assert.IsFalse(SandboxTrackBaker.Rebuild(track));Assert.AreSame(valid,track.Generated);
            StringAssert.Contains("Left rail",track.ValidationError);
            track.Guardrails=false;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track),track.ValidationError);
            Assert.IsNull(track.Generated.transform.Find("Left Rail"));Assert.IsNull(track.Generated.transform.Find("Right Rail"));
        }
        [Test] public void CrabHasContinuousOpenCenterAndSolidSideLanes()
        {
            var crab=Puzzle<SandboxCrabBridge>(10); crab.Length=6; crab.HoleWidth=2; crab.LeftLaneWidth=crab.RightLaneWidth=1;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError); Physics.SyncTransforms();
            foreach(float z in new[]{7.01f,8f,10f,12f,12.99f}) {
                Assert.IsFalse(Ground(0,z),"Invisible center collision at "+z);
                Assert.IsTrue(Ground(-1.5f,z)); Assert.IsTrue(Ground(1.5f,z));
            }
            Assert.IsTrue(Ground(0,6.99f)); Assert.IsTrue(Ground(0,13.01f));
            Assert.IsTrue(track.Generated.GetComponentsInChildren<MeshCollider>().All(c=>!c.convex));
        }
        [Test] public void DeletingPuzzleRestoresOriginalRoad()
        {
            var crab=Puzzle<SandboxCrabBridge>(10); Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true));
            Object.DestroyImmediate(crab.gameObject); Assert.IsTrue(SandboxTrackBaker.Rebuild(track)); Physics.SyncTransforms(); Assert.IsTrue(Ground(0,10));
        }
        [Test] public void DisablingPuzzleRestoresRoadAndInvalidMovesKeepLastMesh()
        {
            var crab=Puzzle<SandboxCrabBridge>(10);
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true)); Physics.SyncTransforms(); Assert.IsFalse(Ground(0,10));
            crab.enabled=false;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true)); Physics.SyncTransforms(); Assert.IsTrue(Ground(0,10));
            Assert.IsFalse(crab.Generated.activeSelf);
            crab.enabled=true; crab.Distance=30;
            var previous=track.Generated;
            Assert.IsFalse(SandboxTrackBaker.Rebuild(track,true)); Assert.AreSame(previous,track.Generated);
        }
        [Test] public void TargetDistanceIsIndependentAndLayersMatchBuildGrid()
        {
            var bridge=Puzzle<SandboxDropBridge>(12); bridge.TargetDistance=4; bridge.TargetLayer=2;
            bridge.TargetSize=.5f;
            bridge.TargetMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CreekDiorama/Materials/Goal vermilion.mat");
            Assert.IsNotNull(bridge.TargetMaterial);
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            Vector3 first=bridge.TargetCollider.transform.position;
            bridge.Distance=15;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            Assert.That(Vector3.Distance(first,bridge.TargetCollider.transform.position),Is.LessThan(.0001f));
            bridge.TargetLayer=3;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            Assert.That(bridge.TargetCollider.transform.position.y-first.y,Is.EqualTo(BuildingGeometry.Unit/GravityGarage25DCourse.UnitScale).Within(.0001f));
            Assert.AreEqual(.5f,bridge.TargetSize,"Layer alignment must preserve the authored target size.");
            Assert.AreEqual(Vector3.one*.5f,bridge.TargetCollider.transform.localScale);
            Assert.AreSame(bridge.TargetMaterial,bridge.TargetCollider.GetComponent<Renderer>().sharedMaterial);
            Assert.AreSame(track.TimberMaterial,bridge.Deck.GetComponentInChildren<Renderer>().sharedMaterial,"Target color must not recolor the bridge.");
        }
        [TestCase(false)] [TestCase(true)]
        public void RebuildRemovesStalePuzzleCopiesIncludingDisabledPuzzles(bool disabled)
        {
            var roller=Puzzle<SandboxRoller>(10);
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true));
            var stale=Object.Instantiate(roller.Generated,roller.transform);
            stale.name=roller.Generated.name;
            var decoration=new GameObject("Designer decoration"); decoration.transform.SetParent(roller.transform);
            roller.enabled=!disabled;
            try {
                Assert.IsTrue(SandboxTrackBaker.Rebuild(track),track.ValidationError);
                Assert.IsFalse(stale,"Old generated colliders and renderers must not survive a rebuild.");
                Assert.IsTrue(decoration,"Preserve designer-authored children.");
                Assert.AreEqual(!disabled,roller.Generated.activeSelf);
                Assert.AreEqual(1,roller.transform.Cast<Transform>().Count(t=>t.name=="Generated puzzle (edit parent)"));
            } finally { Object.DestroyImmediate(roller.gameObject); }
        }
        [Test] public void FactoryCameraFramesRoadWithoutIncludingStrayPuzzleGeometry()
        {
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true));
            var stray=GameObject.CreatePrimitive(PrimitiveType.Cube);
            stray.name="Stale bridge deck"; stray.transform.SetParent(track.transform);
            stray.transform.position=Vector3.one*1000000;
            var root=new GameObject("Factory camera fixture"); root.SetActive(false);
            try {
                var factory=root.AddComponent<SandboxFactoryLevel>(); factory.TrackRoot=track.transform;
                factory.Start=track.transform; factory.Tracks=new[]{track};
                var workshop=root.AddComponent<WorkshopBootstrap>(); workshop.Layout=root.AddComponent<BuildingSceneLayout>(); workshop.Arena=root.transform;
                var level=root.AddComponent<DownhillController>();
                var course=root.AddComponent<DownhillCourse>(); course.SandboxFrame=track.transform;
                typeof(DownhillController).GetProperty("Course").SetValue(level,course);
                typeof(DownhillController).GetProperty("Workshop").SetValue(level,workshop);
                typeof(WorkshopBootstrap).GetProperty("Downhill").SetValue(workshop,level);
                var desktop=root.AddComponent<DesktopInputAdapter>(); desktop.Workshop=workshop;
                var camera=new GameObject("Test camera"); camera.transform.SetParent(root.transform);
                desktop.View=camera.AddComponent<Camera>(); desktop.View.aspect=16f/9;
                desktop.FrameGarage();
                Assert.Less(desktop.View.transform.position.magnitude,100,"A stray puzzle must not move the camera away from the road.");
                foreach(var point in track.Samples) {
                    var viewport=desktop.View.WorldToViewportPoint(track.transform.TransformPoint(point));
                    Assert.Greater(viewport.z,0); Assert.That(viewport.x,Is.InRange(0f,1f)); Assert.That(viewport.y,Is.InRange(0f,1f));
                }
            } finally { Object.DestroyImmediate(root); }
        }
        [Test] public void RollerFitsRoadAndRestsOnItsSurface()
        {
            var roller=Puzzle<SandboxRoller>(10); roller.Width=100; roller.Height=3; roller.Offset=new Vector3(5,2,0);
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            Assert.AreEqual(0,roller.Height); Assert.AreEqual(Vector3.zero,roller.Offset);
            Assert.That(roller.Width,Is.EqualTo(SandboxPuzzleEditing.At(SandboxTrackBaker.SamplePath(track),10).Width));
        }
        [Test] public void OverlappingCutoutsAndOutOfRangePlacementsAreRejected()
        {
            var first=Puzzle<SandboxCrabBridge>(10); first.name="First crossing";
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true)); var previous=track.Generated;
            var second=Puzzle<SandboxCrabBridge>(11); second.name="Second crossing";
            Assert.IsFalse(SandboxTrackBaker.Rebuild(track,true)); StringAssert.Contains("overlap",track.ValidationError);
            StringAssert.Contains(first.name,second.ValidationError); StringAssert.Contains(second.name,first.ValidationError);
            Assert.AreSame(previous,track.Generated,"Invalid placement must retain the last valid road.");
            second.Distance=30; Assert.IsFalse(SandboxTrackBaker.Rebuild(track,true)); StringAssert.Contains("past the track",track.ValidationError);
        }
        [Test] public void SplineChangesKeepDistanceAndMovePuzzle()
        {
            var roller=Puzzle<SandboxRoller>(10); Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true));
            var before=roller.transform.position; var knot=track.Curve.Spline[1]; knot.Position=new float3(10,0,20); track.Curve.Spline[1]=knot;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track)); Assert.AreEqual(10,roller.Distance); Assert.Greater(Vector3.Distance(before,roller.transform.position),1);
        }
        [TestCase(1f)] [TestCase(.21f)] public void DropBridgeActuallyFallsAfterItsConstraintIsReleasedAndResets(float scale)
        {
            track.transform.localScale=Vector3.one*scale;
            var bridge=Puzzle<SandboxDropBridge>(10); Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true),track.ValidationError);
            // The existing workbench presentation moves the whole course immediately before reset.
            var offset=new Vector3(2,3,-4); bridge.transform.position+=offset;
            var physics=scene.GetPhysicsScene(); bridge.ResetPuzzle(true); Physics.SyncTransforms();
            Assert.IsTrue(bridge.Deck.isKinematic,"An intact target must latch the deck, not rely on tether slack.");
            float raised=bridge.Deck.transform.TransformPoint(Vector3.forward*bridge.Length*scale).y;
            for(int i=0;i<120;i++) physics.Simulate(.01f);
            Assert.That(bridge.Deck.transform.TransformPoint(Vector3.forward*bridge.Length*scale).y,Is.EqualTo(raised).Within(.0001f*scale),"Deck must not sag before the drill hits.");
            bridge.DrillHit(bridge.TargetCollider.bounds.center);
            Assert.IsFalse(bridge.Deck.isKinematic); Assert.IsTrue(bridge.Deck.useGravity);
            for(int i=0;i<400;i++) physics.Simulate(.01f);
            float lowered=bridge.Deck.transform.TransformPoint(Vector3.forward*bridge.Length*scale).y;
            Assert.Less(lowered,raised-scale,"Deck must fall through real physics"); Assert.Greater(lowered,offset.y-.25f*scale,"Landing must stop the deck");
            for(int cycle=0;cycle<10;cycle++) {
                bridge.ResetPuzzle(true); Assert.IsFalse(bridge.Released); Assert.IsTrue(bridge.TargetCollider.gameObject.activeSelf);
                for(int i=0;i<60;i++) physics.Simulate(.01f);
                Assert.That(bridge.Deck.transform.TransformPoint(Vector3.forward*bridge.Length*scale).y,Is.EqualTo(raised).Within(.01f*scale));
                Assert.AreEqual(2,bridge.Deck.GetComponents<Joint>().Length);
                bridge.DrillHit(bridge.TargetCollider.bounds.center); for(int i=0;i<150;i++) physics.Simulate(.01f);
            }
        }
        [Test] public void DesktopDepthAndRotationPreserveHorizontalDragHeight()
        {
            var drag=new SandboxPlaneDrag(); drag.Begin(new Vector3(0,1,0),Vector3.up,Quaternion.identity);
            var ray=new Ray(new Vector3(0,5,-5),new Vector3(0,-4,5));
            var pose=drag.DesktopSample(ray,Vector3.forward,.5f,Quaternion.Euler(0,90,0));
            Assert.That(pose.position.y,Is.EqualTo(1).Within(.001f)); Assert.That(pose.position.z,Is.EqualTo(.5f).Within(.001f));
            Assert.Less(Quaternion.Angle(pose.rotation,Quaternion.Euler(0,90,0)),.01f);
            var next=drag.DesktopSample(ray,Vector3.forward,0,Quaternion.identity);
            Assert.Less(Vector3.Distance(pose.position,next.position),.00001f,"Depth must persist without another scroll event");
        }
        [Test] public void LowDrillSweepsAndNonDrillContactsCannotReleaseTheBridge()
        {
            var bridge=Puzzle<SandboxDropBridge>(10); bridge.transform.SetParent(track.transform,true);
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true)); bridge.ResetPuzzle(true); Physics.SyncTransforms();
            var root=new GameObject("Drill interaction fixture"); root.SetActive(false);
            try {
                var factory=root.AddComponent<SandboxFactoryLevel>(); factory.TrackRoot=track.transform;
                var workshop=root.AddComponent<WorkshopBootstrap>(); workshop.Layout=root.AddComponent<BuildingSceneLayout>();
                var level=root.AddComponent<DownhillController>();
                var course=root.AddComponent<DownhillCourse>(); course.SandboxFrame=track.transform;
                typeof(DownhillController).GetProperty("Course").SetValue(level,course);
                typeof(DownhillController).GetProperty("Workshop").SetValue(level,workshop);
                typeof(DownhillController).GetProperty("State").SetValue(level,DownhillState.Running);
                var run=root.AddComponent<SandboxPuzzleRun>(); run.Level=level;
                var part=root.AddComponent<AssemblyPart>(); part.Kind=PartKind.DownhillDrill;
                var low=bridge.TargetBody.position-Vector3.up*(bridge.TargetHeight-.2f);
                for(int i=0;i<5;i++) run.SweepDrill(part,low-Vector3.forward,low+Vector3.forward,.05f);
                Assert.IsFalse(bridge.Released,"A low drill must pass below the target");
                part.Kind=PartKind.DownhillLongBlock;
                run.SweepDrill(part,bridge.TargetBody.position-Vector3.forward,bridge.TargetBody.position+Vector3.forward,.1f);
                Assert.IsFalse(bridge.Released,"Body contact must not count as a drill tip");
            } finally {Object.DestroyImmediate(root);}
        }
        [TestCase("Wide crab",true)] [TestCase("Narrow car",false)] [TestCase("Asymmetric crab",false)]
        public void ActualSavedAssembliesAreSupportedOnlyWhenWheelsReachBothLanes(string preset,bool supported)
        {
            track.transform.localScale=Vector3.one*.21f;
            var crab=Puzzle<SandboxCrabBridge>(10); crab.Length=8; crab.HoleWidth=1.3f; crab.LeftLaneWidth=crab.RightLaneWidth=.7f;
            Assert.IsTrue(SandboxTrackBaker.Rebuild(track,true));
            for(int attempt=0;attempt<5;attempt++) {
                var root=new GameObject("Vehicle physics fixture");
                try {
                    var world=root.AddComponent<AssemblyWorld>(); var factory=root.AddComponent<PartFactory>(); factory.World=world;
                    var service=root.AddComponent<AssemblySaveService>(); service.World=world; service.Factory=factory;
                    var asset=AssetDatabase.LoadAssetAtPath<SandboxTestCarAsset>(SandboxFactorySamples.PresetFolder+"/"+preset+".asset");
                    var data=JsonUtility.FromJson<AssemblySaveV1>(JsonUtility.ToJson(asset.Assembly));
                    float factor=.21f/GravityGarage25DCourse.UnitScale/data.parts[0].geometryScale;
                    foreach(var p in data.parts) { p.geometryScale*=factor; foreach(var b in p.bodies)b.position*=factor; }
                    foreach(var e in data.connections){e.anchorA*=factor;e.anchorB*=factor;}
                    var parts=service.Restore(data,new Pose(new Vector3(0,.2f,2.1f),Quaternion.identity));
                    foreach(var p in parts) p.transform.SetParent(root.transform,true);
                    var bodies=parts.SelectMany(p=>p.Bodies).ToArray();
                    for(int i=0;i<bodies.Length;i++) for(int j=i+1;j<bodies.Length;j++) foreach(var a in bodies[i].Colliders) foreach(var b in bodies[j].Colliders) Physics.IgnoreCollision(a,b);
                    world.EditingEnabled=false;
                    foreach(var b in bodies) {b.Anchored=false;b.Rigidbody.isKinematic=false;b.Rigidbody.useGravity=true;}
                    Physics.SyncTransforms(); for(int i=0;i<600;i++) scene.GetPhysicsScene().Simulate(.005f);
                    var core=parts.Single(p=>p.BuildingCore).MainBody.Rigidbody;
                    if(supported) {Assert.Greater(core.position.y,0,"Wide car fell, attempt "+attempt);Assert.Greater(Vector3.Dot(core.rotation*Vector3.up,Vector3.up),.8f);}
                    else {
                        int supportedWheels=0;
                        foreach(var p in parts.Where(p=>DownhillParts.IsWheel(p.Kind))) {
                            var tire=p.Bodies.Single(b=>b.Id=="tire"); var ray=new Ray(tire.Rigidbody.position,Vector3.down);
                            if(track.Generated.GetComponentsInChildren<Collider>().Any(c=>c.Raycast(ray,out _,.15f))) supportedWheels++;
                        }
                        // An asymmetric beam can catch a lane and scrape along it; it need not fall completely.
                        Assert.Less(supportedWheels,4,"An invalid build acquired wheel support across the opening, attempt "+attempt);
                    }
                } finally {Object.DestroyImmediate(root);}
            }
        }
    }
}
