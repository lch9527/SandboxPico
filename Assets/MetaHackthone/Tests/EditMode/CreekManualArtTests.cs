using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using PhysicalAssembly.Editor;

namespace PhysicalAssembly.Tests {
 public sealed class CreekManualArtTests {
  [Test] public void EveryPartHasPlacementPivotAndNoPhysicsOrMissingMaterials() {
   var paths=AssetDatabase.FindAssets("t:Prefab",new[]{CarSceneryPartsAuthoring.Parts}).Select(AssetDatabase.GUIDToAssetPath).ToArray();Assert.AreEqual(37,paths.Length);
   foreach(var path in paths) {
    var part=AssetDatabase.LoadAssetAtPath<GameObject>(path);Assert.AreEqual(Vector3.zero,part.transform.position);Assert.AreEqual(Vector3.one,part.transform.localScale);
    Assert.IsEmpty(part.GetComponentsInChildren<Collider>(true));Assert.IsEmpty(part.GetComponentsInChildren<Rigidbody>(true));
    var renderers=part.GetComponentsInChildren<Renderer>(true);Assert.IsNotEmpty(renderers);
    foreach(var r in renderers)foreach(var mat in r.sharedMaterials){Assert.IsNotNull(mat,path);Assert.IsNotNull(mat.shader,path);}
    var bounds=CarSceneryPartsAuthoring.Bounds(renderers);
    if(!part.GetComponent<CarSceneryBreeze>()) {Assert.AreEqual(0,bounds.min.y,.0001f,path);Assert.AreEqual(0,bounds.center.x,.0001f,path);Assert.AreEqual(0,bounds.center.z,.0001f,path);}
   }
  }
  [TestCase("Trail_Rest_Stop")] [TestCase("Riverside_Mooring")] [TestCase("Wheelwright_Bench")]
  [TestCase("Quarry_Winch")] [TestCase("Freight_Windmill")] [TestCase("Creek_Wind_Pennant")]
  public void EditableAssembliesAreMadeOfNestedParts(string name) {
   var asset=AssetDatabase.LoadAssetAtPath<GameObject>(CarSceneryPartsAuthoring.Editable+"/"+name+"_Editable.prefab");Assert.IsNotNull(asset);
   var contents=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(asset));
   try {
    foreach(Transform child in contents.transform) {
     Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject),child.name);
     var source=PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);Assert.That(AssetDatabase.GetAssetPath(source),Does.StartWith(CarSceneryPartsAuthoring.Parts));
    }
   }finally{PrefabUtility.UnloadPrefabContents(contents);}
  }
  [Test] public void LandscapeExposesIndividualBotanicalPlacements() {
   var asset=AssetDatabase.LoadAssetAtPath<GameObject>(CarSceneryPartsAuthoring.Editable+"/Creek_Environment_Editable.prefab");Assert.IsNotNull(asset);
   var placements=asset.transform.Find("10 Botanical dressing/Editable trees rocks plants and props");Assert.IsNotNull(placements);Assert.IsTrue(placements.gameObject.activeSelf);
   Assert.IsNull(asset.transform.Find("10 Botanical dressing/Render batches"));Assert.Greater(placements.GetComponentsInChildren<MeshFilter>().Length,50);Assert.IsEmpty(asset.GetComponentsInChildren<Collider>(true));
  }
  [Test] public void CloudNoiseHasMipsAndTextureFilteringIsEnabled() {
   var noise=AssetDatabase.LoadAssetAtPath<Texture3D>("Assets/CreekDiorama/Textures/Clouds/CloudVolumeNoise.asset");Assert.AreEqual(7,noise.mipmapCount);Assert.AreEqual(FilterMode.Trilinear,noise.filterMode);
   Assert.AreNotEqual(AnisotropicFiltering.Disable,QualitySettings.anisotropicFiltering);
   for(int mip=0;mip<noise.mipmapCount;mip++)Assert.Greater(noise.GetPixelData<byte>(mip).Length,0);
  }
  [TestCase("Creek Diorama/Pastel Sky")] [TestCase("Creek Diorama/Horizon Palette")]
  [TestCase("Creek Diorama/Living Water")] [TestCase("Creek Diorama/Silk Waterfall")]
  [TestCase("Creek Diorama/Waterfall Impact")] [TestCase("Creek Diorama/Portal")]
  [TestCase("Creek Diorama/Cloud Puff")] [TestCase("Creek Diorama/Cloud Shell")]
  [TestCase("Creek Diorama/Assembly Bounds")] [TestCase("Creek Diorama/Waterfall Spray")]
  [TestCase("MetaHackthone/Steering Emblem")]
  public void EditedShadersCompileWithoutErrors(string name) {
   var shader=Shader.Find(name);Assert.IsNotNull(shader);Assert.IsTrue(shader.isSupported);
   Assert.IsEmpty(ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").Select(m=>m.message).ToArray());
  }
 }
}
