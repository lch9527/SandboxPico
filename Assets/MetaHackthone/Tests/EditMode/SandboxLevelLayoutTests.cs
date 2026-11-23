using System;
using System.Linq;
using NUnit.Framework;
using PhysicalAssembly.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Tests
{
    public sealed class SandboxLevelLayoutTests
    {
        [TestCase("Level1",1)] [TestCase("DownhillSandbox",2)] [TestCase("Level4",1)] [TestCase("LevelFactory",1)]
        public void SavedScenesHaveCompleteIndependentGroupsAndRepeatSafeMigration(string name,int count)
        {
            var path="Assets/MetaHackthone/Scenes/"+name+".unity";
            var scene=SceneManager.GetSceneByPath(path); bool opened=!scene.isLoaded;
            if(opened) scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try {
                var owner=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<BuildingSceneLayout>(true)).Single();
                Assert.AreEqual(count,owner.LevelLayouts.Length);
                var ids=owner.GetComponentsInChildren<Transform>(true).Select(t=>t.GetInstanceID()).ToArray();
                SandboxLevelLayoutAuthoring.Migrate(owner);
                CollectionAssert.AreEqual(ids,owner.GetComponentsInChildren<Transform>(true).Select(t=>t.GetInstanceID()));
                foreach(var layout in owner.LevelLayouts) {
                    Assert.IsTrue(layout.Validate(out var error),error);
                    Assert.AreEqual("Building Area",layout.BuildingRoot.name); Assert.AreEqual("Sandbox Level",layout.SandboxRoot.name);
                    Assert.IsFalse(layout.BuildingRoot.IsChildOf(layout.SandboxRoot));
                    Assert.IsTrue(layout.Course.WorkbenchFrame.IsChildOf(layout.BuildingRoot));
                    Assert.IsTrue(layout.Course.SandboxFrame.IsChildOf(layout.SandboxRoot));
                    Assert.IsTrue(layout.Presentation.Interface.Root.IsChildOf(layout.BuildingRoot));
                    Assert.IsTrue(layout.Presentation.AssemblyAnchor.IsChildOf(layout.BuildingRoot));
                    Assert.IsTrue(layout.Spawns.All(s=>!s || s.transform.IsChildOf(layout.BuildingRoot)));
                    Assert.IsTrue(layout.Course.BuildArea.transform.IsChildOf(layout.BuildingRoot));
                }
                if(count==2) Assert.IsFalse(owner.LevelLayouts[0].Spawns.Intersect(owner.LevelLayouts[1].Spawns).Any());
            } finally {if(opened) EditorSceneManager.CloseScene(scene,true);}
        }

        [Test] public void ExportPreservesBothTransformsAndRejectsInvalidScaleWithoutReplacingPlayableCopy()
        {
            var source=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>(BeautifulCornerDesignAuthoring.PrefabPath);
            var scene=EditorSceneManager.NewPreviewScene(); var clone=Object.Instantiate(source.gameObject);
            SceneManager.MoveGameObjectToScene(clone,scene);
            string folder="Assets/LayoutExportTest-"+Guid.NewGuid().ToString("N");
            try {
                var factory=clone.GetComponent<SandboxFactoryLevel>(); var binding=factory.Layout;
                var owner=clone.AddComponent<BuildingSceneLayout>(); binding.Bind(owner);
                binding.BuildingRoot.localPosition+=new Vector3(2,1,-3);
                binding.BuildingRoot.localRotation=Quaternion.Euler(0,90,0); binding.BuildingRoot.localScale=Vector3.one*.5f;
                binding.SandboxRoot.localPosition+=new Vector3(-2,.5f,3);
                binding.SandboxRoot.localRotation=Quaternion.Euler(0,-90,0); binding.SandboxRoot.localScale=Vector3.one*2;
                var saved=SandboxFactoryCampaignSave.Export(factory,folder+"/Level.prefab",folder+"/Meshes","Test layout");
                Assert.AreEqual(binding.BuildingRoot.localToWorldMatrix,saved.Layout.BuildingRoot.localToWorldMatrix);
                Assert.AreEqual(binding.SandboxRoot.localToWorldMatrix,saved.Layout.SandboxRoot.localToWorldMatrix);
                Assert.AreEqual(binding.Spawns[0].SizeMultiplier,saved.CampaignSpawns[0].SizeMultiplier);
                var guid=AssetDatabase.AssetPathToGUID(folder+"/Level.prefab");
                foreach(var invalid in new[]{new Vector3(1,2,1),Vector3.zero,-Vector3.one}) {
                    binding.BuildingRoot.localScale=invalid;
                    Assert.Throws<InvalidOperationException>(()=>SandboxFactoryCampaignSave.Export(factory,folder+"/Level.prefab",folder+"/Meshes","Invalid"));
                    Assert.AreEqual(guid,AssetDatabase.AssetPathToGUID(folder+"/Level.prefab"));
                }
                binding.BuildingRoot.localScale=Vector3.one; binding.BuildingRoot.localRotation=Quaternion.Euler(10,0,0);
                Assert.IsFalse(binding.Validate(out _));
                Assert.IsTrue(saved.Validate(out var error),error);
            } finally {Object.DestroyImmediate(clone); EditorSceneManager.ClosePreviewScene(scene); AssetDatabase.DeleteAsset(folder);}
        }
    }
}
