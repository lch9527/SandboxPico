using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PhysicalAssembly.Tests
{
    public sealed class CarRenderingIntegrationTests
    {
        [Test] public void QuestClarityPresetSurvivesMsaaSetupAndCanReturnToNative()
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            float previous=pipeline.renderScale;
            try {
                PhysicalAssembly.Editor.ProjectBuilder.ApplyQuestClarity();
                PhysicalAssembly.Editor.ProjectBuilder.ApplyQuestMsaa();
                Assert.AreEqual(1.15f,pipeline.renderScale,.001f);
                Assert.AreEqual(4,pipeline.msaaSampleCount);
                Assert.IsFalse(pipeline.supportsCameraDepthTexture);Assert.IsFalse(pipeline.supportsCameraOpaqueTexture);
                PhysicalAssembly.Editor.ProjectBuilder.ApplyQuestNative();
                Assert.AreEqual(1,pipeline.renderScale);Assert.AreEqual(4,pipeline.msaaSampleCount);
            }finally{pipeline.renderScale=previous;EditorUtility.SetDirty(pipeline);AssetDatabase.SaveAssets();}
        }
        [Test] public void MobilePipelineUsesFourSamplesWithoutCameraTextureCost()
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            Assert.IsFalse(pipeline.supportsCameraDepthTexture);Assert.IsFalse(pipeline.supportsCameraOpaqueTexture);
            Assert.AreEqual(4,pipeline.msaaSampleCount);Assert.AreEqual(1,pipeline.renderScale);
            int previous=QualitySettings.GetQualityLevel();
            try {
                for(int i=0;i<QualitySettings.names.Length;i++) {
                    if(QualitySettings.GetRenderPipelineAssetAt(i)!=pipeline)continue;
                    QualitySettings.SetQualityLevel(i,false);
                    Assert.AreEqual(4,QualitySettings.antiAliasing,QualitySettings.names[i]);
                }
            }finally{QualitySettings.SetQualityLevel(previous,false);}
        }
        [Test] public void MobilePipelineKeepsProgramBranchMeasuredDefaults()
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            Assert.IsFalse(pipeline.supportsCameraDepthTexture);Assert.IsFalse(pipeline.supportsCameraOpaqueTexture);
            Assert.AreEqual(4,pipeline.msaaSampleCount);Assert.AreEqual(1,pipeline.renderScale);
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void EveryLevelRiverWorksWithoutCameraTextures(int level)
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab");
            var water=root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m && m.shader.name=="Creek Diorama/Living Water").Distinct().ToArray();
            Assert.IsNotEmpty(water);
            foreach(var material in water) {Assert.AreEqual(0,material.GetFloat("_UseDepth"),material.name);Assert.AreEqual(0,material.GetFloat("_UseOpaqueTexture"),material.name);}
        }
        [Test] public void NewWaterMaterialsDefaultToNoCameraTextures()
        {
            var material=new Material(Shader.Find("Creek Diorama/Living Water"));
            try { Assert.AreEqual(0,material.GetFloat("_UseDepth"));Assert.AreEqual(0,material.GetFloat("_UseOpaqueTexture")); }
            finally { Object.DestroyImmediate(material); }
        }
    }
}
