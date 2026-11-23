using NUnit.Framework;
using UnityEngine;
using System.Reflection;

namespace PhysicalAssembly.Tests
{
    public sealed class SandboxMeshContactTests
    {
        [TestCase(-.4f)] [TestCase(0)] [TestCase(.4f)]
        public void ClosedPinchInsideMeshWorksAcrossItsFullLength(float along)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try {
                var part=Bind(go);
                go.transform.SetPositionAndRotation(new Vector3(2,1,3),Quaternion.Euler(25,40,10));
                go.transform.localScale=new Vector3(.3f,.1f,.1f);
                var center=go.transform.TransformPoint(new Vector3(along,0,0));
                var query=new SandboxMeshContact();
                Assert.AreSame(part,query.Find(new[]{part},center-go.transform.up*.006f,center+go.transform.up*.006f,.005f));
            } finally {Object.DestroyImmediate(go);}
        }
        [Test] public void WheelRimVolumeIsGrabbableButHoleAndEmptyBoundsAreNot()
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh=DownhillParts.Ring(.12f,.05f,.04f);
            try {
                go.GetComponent<MeshFilter>().sharedMesh=mesh;
                var part=Bind(go);var query=new SandboxMeshContact();var parts=new[]{part};
                for(int i=0;i<8;i++) {
                    float angle=i*Mathf.PI/4;
                    var center=new Vector3(0,Mathf.Cos(angle)*.09f,Mathf.Sin(angle)*.09f);
                    Assert.AreSame(part,query.Find(parts,center-Vector3.right*.006f,center+Vector3.right*.006f,.005f),"Rim position "+i);
                }
                Assert.IsNull(query.Find(parts,Vector3.left*.006f,Vector3.right*.006f,.005f),"The axle hole must stay empty.");
                var corner=new Vector3(0,.11f,.11f);
                Assert.IsNull(query.Find(parts,corner,corner,.005f),"Wheel bounds must not become a box-shaped grab zone.");
            } finally {Object.DestroyImmediate(go);Object.DestroyImmediate(mesh);}
        }
        static AssemblyPart Bind(GameObject go)
        {
            var part=go.AddComponent<AssemblyPart>();part.InstanceId="test";
            typeof(AssemblyPart).GetField("<VisualMeshes>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(part,new[]{go.GetComponent<MeshFilter>()});
            return part;
        }
        [TestCase(PartKind.DownhillLongBlock)] [TestCase(PartKind.DownhillLargeWheel)]
        [TestCase(PartKind.DownhillSmallWheel)]
        [TestCase(PartKind.DownhillShortBlock)] [TestCase(PartKind.DownhillCube)]
        [TestCase(PartKind.DownhillTriangle)] [TestCase(PartKind.DownhillDrill)]
        public void CampaignPartsCanBeGrabbedAwayFromBodyCenter(PartKind kind)
        {
            var root=new GameObject("Full mesh grab test");AssemblyPart part=null;
            try {
                var world=root.AddComponent<AssemblyWorld>();world.FloatingBuild=true;
                part=DownhillParts.Create(world,kind,new Pose(new Vector3(1,2,3),Quaternion.Euler(20,55,15)),gridVersion:DownhillParts.DesignerGridVersion);
                var query=new SandboxMeshContact();var driver=new HandGrabDriver(world,0);
                float u=BuildingGeometry.Unit;
                Vector3 local=kind switch {
                    PartKind.DownhillLongBlock=>Vector3.forward*u*1.25f,
                    PartKind.DownhillShortBlock or PartKind.DownhillCube=>Vector3.forward*u*.75f,
                    PartKind.DownhillTriangle=>Vector3.back*u*.5f,
                    PartKind.DownhillDrill=>Vector3.Lerp(SandboxDrill.BaseCenter(u,1),SandboxDrill.Tip(u,1),.35f),
                    _=>Vector3.up*(kind==PartKind.DownhillLargeWheel?.44f:.30f)/.38f*u*.75f
                };
                var point=part.transform.TransformPoint(local);var pose=new Pose(point,part.transform.rotation);
                driver.Sample(true,.06f,false,pose,null,true,0);
                var candidate=query.Find(world.Parts.Values,point-part.transform.right*.006f,point+part.transform.right*.006f,.005f);
                Assert.AreSame(part,candidate,"Pinching inside the far end/rim must select the visible mesh.");
                driver.Sample(true,.012f,false,pose,candidate,true,.1f);
                Assert.AreSame(part,world.HeldPart(0),"The full grab path must accept that off-center contact.");
                driver.Cancel();
                if(kind==PartKind.DownhillTriangle) {
                    var empty=part.transform.TransformPoint(new Vector3(0,u*.35f,u*.75f));
                    Assert.IsNull(query.Find(world.Parts.Values,empty,empty,.005f),"The empty corner above the slope is outside the mesh.");
                }
            } finally {if(part)Object.DestroyImmediate(part.gameObject);Object.DestroyImmediate(root);}
        }
        [TestCase(.004f,true)] [TestCase(.006f,false)] [TestCase(.01f,false)] [TestCase(.075f,false)]
        public void FaceContactUsesFiveMillimetres(float gap,bool expected)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try {
                var part=go.AddComponent<AssemblyPart>();part.InstanceId="test";
                typeof(AssemblyPart).GetField("<VisualMeshes>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(part,new[]{go.GetComponent<MeshFilter>()});
                go.transform.SetPositionAndRotation(new Vector3(2,1,3),Quaternion.Euler(25,40,10));go.transform.localScale=new Vector3(.2f,.3f,.1f);
                var center=go.transform.TransformPoint(new Vector3(0,.5f,0))+go.transform.up*gap;
                var query=new SandboxMeshContact();var parts=new[]{part};
                Assert.AreEqual(expected,query.Find(parts,center-go.transform.right*.005f,center+go.transform.right*.005f,.005f)==part);
                go.GetComponent<Renderer>().enabled=false;Assert.IsNull(query.Find(parts,center,center,.005f));
                query.Clear();
            } finally {Object.DestroyImmediate(go);}
        }
        [Test] public void TriangleBoundsDoNotFillEmptyCorners()
        {
            var a=Vector3.zero;var b=Vector3.right;var c=Vector3.up;
            var point=new Vector3(.8f,.8f,0);
            Assert.Greater(SandboxMeshContact.DistanceSquared(point,point,a,b,c),.005f*.005f);
            Assert.AreEqual(0,SandboxMeshContact.DistanceSquared(new Vector3(.2f,.2f,-.1f),new Vector3(.2f,.2f,.1f),a,b,c));
        }
    }
}
