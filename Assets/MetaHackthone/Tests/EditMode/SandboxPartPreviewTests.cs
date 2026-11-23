using System.Linq;
using CloudGarden.Integration;
using NUnit.Framework;
using PhysicalAssembly.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Tests
{
    public sealed class SandboxPartPreviewTests
    {
        static readonly PartKind[] Kinds={PartKind.DownhillLongBlock,PartKind.DownhillCube,PartKind.DownhillShortBlock,
            PartKind.DownhillCore,PartKind.DownhillTriangle,PartKind.DownhillSmallWheel,PartKind.DownhillLargeWheel,PartKind.DownhillDrill};

        [TestCase(.4f,1f)] [TestCase(.65f,1.7f)]
        public void EveryPreviewMatchesRuntimeMeshesMaterialsAndWorldGeometry(float size,float buildingScale)
        {
            var scene=EditorSceneManager.NewPreviewScene();
            var settings=ScriptableObject.CreateInstance<DownhillSettings>(); settings.DesignerBuilder=true;
            var root=new GameObject("Preview parity"); root.SetActive(false); SceneManager.MoveGameObjectToScene(root,scene);
            var entry=root.AddComponent<DownhillEntry>(); entry.Settings=settings;
            var layout=root.AddComponent<BuildingSceneLayout>(); entry.Layout=layout;
            var level=root.AddComponent<SandboxLevelLayout>();
            var building=new GameObject("Building Area").transform; building.SetParent(root.transform,false);
            building.localScale=Vector3.one*buildingScale; level.BuildingRoot=building;
            layout.Spawns=Kinds.Select((kind,i)=> {
                var marker=new GameObject(kind.ToString()).AddComponent<BuildingPartSpawn>(); marker.Kind=kind;
                marker.transform.SetParent(building,false); marker.transform.localPosition=new Vector3(i*.3f,.7f,.2f);
                marker.transform.localRotation=Quaternion.Euler(0,27+i*15,0); marker.transform.localScale=Vector3.one*size;
                marker.Preview=GameObject.CreatePrimitive(PrimitiveType.Cube); marker.Preview.transform.SetParent(marker.transform,false);
                return marker;
            }).ToArray();
            var world=new GameObject("Runtime parity world").AddComponent<AssemblyWorld>(); SceneManager.MoveGameObjectToScene(world.gameObject,scene);
            try {
                Assert.AreEqual(Kinds.Length,SandboxPartPreviewSync.Refresh(scene));
                Assert.AreEqual(0,SandboxPartPreviewSync.Refresh(scene),"An unchanged palette must not rebuild or dirty the scene again.");
                foreach(var spawn in layout.Spawns) {
                    var part=DownhillParts.Create(world,spawn.Kind,spawn.Pose,spawn.SizeMultiplier,settings,DownhillParts.DesignerGridVersion,spawn.Kind==PartKind.DownhillCore);
                    try {
                        var preview=spawn.Preview.GetComponentsInChildren<MeshFilter>(true);
                        Assert.AreEqual(part.VisualMeshes.Length,preview.Length,spawn.Kind.ToString());
                        Assert.IsEmpty(spawn.Preview.GetComponentsInChildren<Collider>(true));
                        Assert.IsEmpty(spawn.Preview.GetComponentsInChildren<Rigidbody>(true));
                        Assert.IsEmpty(spawn.Preview.GetComponentsInChildren<AssemblySocket>(true));
                        foreach(var runtime in part.VisualMeshes) {
                            var mesh=preview.Single(f=>f.sharedMesh==runtime.sharedMesh);
                            Assert.IsTrue(EditorUtility.IsPersistent(mesh.sharedMesh));
                            CollectionAssert.AreEqual(runtime.GetComponent<Renderer>().sharedMaterials,mesh.GetComponent<Renderer>().sharedMaterials);
                            // The mesh asset plus its transform completely determine the visible geometry.
                            foreach(var point in new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.forward})
                                Assert.Less(Vector3.Distance(runtime.transform.TransformPoint(point),mesh.transform.TransformPoint(point)),.00001f,spawn.Kind.ToString());
                        }
                    } finally { Object.DestroyImmediate(part.gameObject); }
                }
                // Changing a slot's kind must also update its art without touching its pose/size.
                var changed=layout.Spawns[0]; var pose=changed.Pose; var scale=changed.transform.localScale;
                changed.Kind=PartKind.DownhillSmallWheel;
                Assert.AreEqual(1,SandboxPartPreviewSync.Refresh(scene));
                Assert.AreEqual(pose.position,changed.Pose.position); Assert.AreEqual(pose.rotation,changed.Pose.rotation);
                Assert.AreEqual(scale,changed.transform.localScale);
            } finally { Object.DestroyImmediate(settings); EditorSceneManager.ClosePreviewScene(scene); }
        }

        [TestCase("Level1")] [TestCase("Level2")] [TestCase("Level3")]
        [TestCase("Level4")] [TestCase("Level5")] [TestCase("FactoryFinalRun")] [TestCase("DownhillSandbox")]
        public void ExistingSandboxScenesRefreshToSharedArtWithoutChangingPartMarkers(string name)
        {
            var scene=EditorSceneManager.OpenPreviewScene($"Assets/MetaHackthone/Scenes/{name}.unity");
            try {
                var markers=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<BuildingPartSpawn>(true)).ToArray();
                var before=markers.Select(m=>(m.Kind,m.Pose.position,m.Pose.rotation,m.transform.localScale)).ToArray();
                SandboxPartPreviewSync.Refresh(scene);
                Assert.AreEqual(0,SandboxPartPreviewSync.Refresh(scene));
                CollectionAssert.AreEqual(before,markers.Select(m=>(m.Kind,m.Pose.position,m.Pose.rotation,m.transform.localScale)).ToArray());
                var entry=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DownhillEntry>(true)).Single();
                foreach(var spawn in entry.Layout.Spawns.Where(s=>s)) {
                    var expected=CarPartArtLibrary.Load().Visuals.Where(v=>v.Kind==spawn.Kind).Select(v=>v.Lods[0]).ToArray();
                    CollectionAssert.AreEquivalent(expected,spawn.Preview.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray(),spawn.name);
                }
            } finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
