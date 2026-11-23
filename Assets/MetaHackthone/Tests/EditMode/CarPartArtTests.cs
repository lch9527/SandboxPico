using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PhysicalAssembly.Tests
{
    // The Blender kit may change how designer parts look, never how they collide or connect.
    public sealed class CarPartArtTests
    {
        static readonly PartKind[] Kinds={PartKind.DownhillCore,PartKind.DownhillCube,PartKind.DownhillShortBlock,PartKind.DownhillLongBlock,
            PartKind.DownhillTriangle,PartKind.DownhillSmallWheel,PartKind.DownhillLargeWheel,PartKind.DownhillDrill};

        static AssemblyPart Make(GameObject root,PartKind kind,float scale)
            => DownhillParts.Create(root.GetComponent<AssemblyWorld>(),kind,new Pose(Vector3.zero,Quaternion.identity),scale,gridVersion:DownhillParts.DesignerGridVersion,buildingCore:kind==PartKind.DownhillCore);
        static Bounds Union<T>(T[] items,System.Func<T,Bounds> bounds)
        {
            var total=bounds(items[0]); foreach(var item in items) total.Encapsulate(bounds(item)); return total;
        }

        [Test] public void LibraryHoldsThreeReadableLodsWithStableMaterialSlots()
        {
            var library=CarPartArtLibrary.Load(); Assert.IsNotNull(library,"Run MetaHackthone > Car Parts Art > Import Blender kit");
            foreach(var kind in Kinds) Assert.IsTrue(library.Visuals.Any(v=>v.Kind==kind),kind+" has no art");
            foreach(var visual in library.Visuals) {
                Assert.AreEqual(3,visual.Lods.Length);
                for(int level=0;level<3;level++) {
                    Assert.IsTrue(visual.Lods[level].isReadable,visual.Lods[level].name+" must stay readable for pinch tests and outlines");
                    Assert.AreEqual(visual.Materials.Length,visual.Lods[level].subMeshCount,visual.Lods[level].name);
                    if(level>0) Assert.Less(visual.Lods[level].triangles.Length,visual.Lods[level-1].triangles.Length,visual.Lods[level].name);
                }
            }
            Assert.AreEqual(3,library.BumpMid.Length); Assert.AreEqual(3,library.BumpEnd.Length);
            Assert.Greater(library.Lod0Degrees,library.Lod1Degrees);
        }

        [TestCase(1f)] [TestCase(.4f)] public void ArtChangesTheLookAndNothingElse(float scale)
        {
            var library=CarPartArtLibrary.Load(); Assert.IsNotNull(library);
            var root=new GameObject("Car part art test"); root.AddComponent<AssemblyWorld>(); bool enabled=library.Enabled;
            AssemblyPart plain=null, art=null;
            try {
                foreach(var kind in Kinds) {
                    library.Enabled=false; plain=Make(root,kind,scale);
                    library.Enabled=true; art=Make(root,kind,scale);
                    Assert.AreEqual(plain.Sockets.Length,art.Sockets.Length,kind.ToString());
                    foreach(var socket in plain.Sockets) {
                        var other=art.FindSocket(socket.Id); Assert.IsNotNull(other,kind+" "+socket.Id);
                        Assert.Less(Vector3.Distance(socket.transform.position,other.transform.position),1e-6f);
                        Assert.Less(Vector3.Distance(socket.Normal,other.Normal),1e-6f);
                    }
                    var before=plain.GetComponentsInChildren<Collider>(); var after=art.GetComponentsInChildren<Collider>();
                    Assert.AreEqual(before.Length,after.Length,kind.ToString());
                    for(int i=0;i<before.Length;i++) {
                        Assert.AreEqual(before[i].GetType(),after[i].GetType());
                        Assert.Less(Vector3.Distance(before[i].bounds.size,after[i].bounds.size),1e-5f,kind+" collider "+i);
                    }
                    for(int i=0;i<plain.Bodies.Length;i++) Assert.AreEqual(plain.Bodies[i].Rigidbody.mass,art.Bodies[i].Rigidbody.mass);

                    Assert.IsNotEmpty(art.VisualRenderers);
                    foreach(var renderer in art.VisualRenderers) {
                        Assert.AreEqual(CarPartArtLibrary.VisualName,renderer.name,kind.ToString());
                        Assert.IsTrue(CarPartArtLibrary.IsShared(renderer.sharedMaterial));
                        Assert.IsNotNull(renderer.GetComponent<CarPartLod>());
                    }
                    Assert.AreEqual(art.VisualRenderers.Length,art.VisualMeshes.Length,"one mesh per renderer keeps outlines and ghosts single");
                    if(kind==PartKind.DownhillDrill) { Object.DestroyImmediate(plain.gameObject); Object.DestroyImmediate(art.gameObject); continue; }
                    // Every LOD fills the physical outline: never wider than the colliders, never visibly smaller.
                    var physical=Union(after,c=>c.bounds); float slack=.02f*BuildingGeometry.Unit*scale;
                    for(int level=0;level<3;level++) {
                        foreach(var lod in art.GetComponentsInChildren<CarPartLod>()) lod.Show(level);
                        var look=Union(art.VisualRenderers,r=>r.bounds);
                        for(int axis=0;axis<3;axis++) {
                            Assert.LessOrEqual(look.max[axis],physical.max[axis]+slack,$"{kind} LOD{level} axis {axis}");
                            Assert.GreaterOrEqual(look.min[axis],physical.min[axis]-slack,$"{kind} LOD{level} axis {axis}");
                            Assert.GreaterOrEqual(look.size[axis],physical.size[axis]*.93f,$"{kind} LOD{level} axis {axis}");
                        }
                    }
                    Object.DestroyImmediate(plain.gameObject); Object.DestroyImmediate(art.gameObject);
                }
            } finally {
                library.Enabled=enabled;
                if(plain) Object.DestroyImmediate(plain.gameObject); if(art) Object.DestroyImmediate(art.gameObject);
                Object.DestroyImmediate(root);
            }
        }

        [Test] public void LodFollowsViewingDistanceWithHysteresis()
        {
            var go=new GameObject("Lod test"); 
            try {
                var library=CarPartArtLibrary.Load(); Assert.IsNotNull(library);
                go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>();
                var lod=go.AddComponent<CarPartLod>(); lod.Meshes=library.Visuals[0].Lods; lod.UnitSize=BuildingGeometry.Unit;
                go.transform.localScale=Vector3.one*.4f;             // a workbench part: one unit is about 3.5 cm
                Assert.AreEqual(0,lod.Evaluate(Vector3.back*.5f),"arm's length");
                Assert.AreEqual(0,lod.Evaluate(Vector3.back*1.1f),"still inside the LOD0 band");
                lod.Show(lod.Evaluate(Vector3.back*2f)); Assert.AreEqual(1,lod.Level);
                Assert.AreEqual(1,lod.Evaluate(Vector3.back*1.2f),"hysteresis holds LOD1 near the threshold");
                Assert.AreEqual(0,lod.Evaluate(Vector3.back*.9f));
                lod.Show(lod.Evaluate(Vector3.back*6f)); Assert.AreEqual(2,lod.Level);
                Assert.AreSame(lod.Meshes[2],go.GetComponent<MeshFilter>().sharedMesh);
            } finally { Object.DestroyImmediate(go); }
        }

        [Test] public void SpeedBumpCoversTheRollerOnEveryLod()
        {
            var root=new GameObject("Speed bump test");
            try {
                const float width=2.4f, diameter=.35f, height=.1f;
                var bump=CarPartArtLibrary.AddSpeedBump(root.transform,SandboxRoller.BumpName,width,diameter,height); Assert.IsNotNull(bump);
                var group=bump.GetComponent<LODGroup>(); var lods=group.GetLODs(); Assert.AreEqual(3,lods.Length);
                Assert.Greater(lods[0].screenRelativeTransitionHeight,lods[1].screenRelativeTransitionHeight);
                Assert.Greater(lods[1].screenRelativeTransitionHeight,lods[2].screenRelativeTransitionHeight);
                foreach(var lod in lods) {
                    var look=Union(lod.renderers,r=>r.bounds);
                    Assert.AreEqual(width,look.size.x,.002f); Assert.AreEqual(0,look.center.x,.002f);
                    Assert.AreEqual(height,look.min.y,.002f); Assert.AreEqual(height+diameter,look.max.y,.002f);
                    Assert.GreaterOrEqual(look.size.z,diameter-.002f); Assert.LessOrEqual(look.size.z,diameter*1.2f);
                }
            } finally { Object.DestroyImmediate(root); }
        }
    }
}
