using NUnit.Framework;
using PhysicalAssembly.MetaXR;
using UnityEngine;

namespace PhysicalAssembly.Tests
{
    public sealed class RewardPassthroughTests
    {
        GameObject rig,bridgeObject,preview;
        OVRPassthroughLayer layer;
        MetaRewardPassthrough bridge;
        SandboxRewardPresentation focus;
        [SetUp] public void Setup()
        {
            // Inspect real SDK layer properties without starting a native passthrough session.
            rig=new GameObject("Passthrough fixture"); rig.SetActive(false);
            layer=rig.AddComponent<OVRPassthroughLayer>();
            bridgeObject=new GameObject("Reward bridge"); bridge=bridgeObject.AddComponent<MetaRewardPassthrough>(); bridge.Initialize(rig.transform);
            preview=new GameObject("Reward"); focus=preview.AddComponent<SandboxRewardPresentation>();
            focus.EnvironmentBrightness=.3f; focus.FadeSeconds=.45f;
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(preview); Object.DestroyImmediate(bridgeObject); Object.DestroyImmediate(rig);
        }
        [TestCase(false)] [TestCase(true)]
        public void MatchesCourseFadeAndRestoresExactLayerSettings(bool originalOverride)
        {
            var scale=new Vector4(.8f,.9f,1.2f,.7f); var offset=new Vector4(.1f,-.1f,.05f,.2f);
            layer.overridePerLayerColorScaleAndOffset=originalOverride;layer.colorScale=scale;layer.colorOffset=offset;layer.textureOpacity=.73f;
            layer.SetBrightnessContrastSaturation(.12f,-.05f,.2f);
            focus.Focus=true;focus.Advance(.225f);
            Assert.IsTrue(layer.overridePerLayerColorScaleAndOffset);
            var effectiveScale=originalOverride?scale:Vector4.one;var effectiveOffset=originalOverride?offset:Vector4.zero;
            Assert.That(layer.colorScale.x,Is.EqualTo(effectiveScale.x*.65f).Within(.00001));
            Assert.That(layer.colorOffset.y,Is.EqualTo(effectiveOffset.y*.65f).Within(.00001));
            Assert.AreEqual(effectiveScale.w,layer.colorScale.w); Assert.AreEqual(effectiveOffset.w,layer.colorOffset.w);
            focus.Advance(.225f);
            Assert.That(layer.colorScale.z,Is.EqualTo(effectiveScale.z*.3f).Within(.00001));
            Assert.AreEqual(.73f,layer.textureOpacity);
            Assert.AreEqual(OVRPassthroughLayer.ColorMapEditorType.ColorAdjustment,layer.colorMapEditorType);
            Assert.AreEqual(.12f,layer.colorMapEditorBrightness); Assert.AreEqual(.2f,layer.colorMapEditorSaturation);
            focus.Restore();
            Assert.AreEqual(originalOverride,layer.overridePerLayerColorScaleAndOffset); Assert.AreEqual(scale,layer.colorScale); Assert.AreEqual(offset,layer.colorOffset);
        }
        [Test] public void MenuOpenCloseReversesFadeWithoutCompounding()
        {
            for(int i=0;i<3;i++) {
                focus.Focus=true; focus.Advance(.45f); Assert.That(layer.colorScale.x,Is.EqualTo(.3f).Within(.00001));
                focus.Focus=false; focus.Advance(.225f); Assert.That(layer.colorScale.x,Is.EqualTo(.65f).Within(.00001));
                focus.Focus=true; focus.Advance(.225f); Assert.That(layer.colorScale.x,Is.EqualTo(.3f).Within(.00001));
                focus.Focus=false; focus.Advance(.45f); Assert.IsFalse(layer.overridePerLayerColorScaleAndOffset); Assert.AreEqual(Vector4.one,layer.colorScale);
            }
        }
        [Test] public void DisableAndDestroyRestoreAndResumeCanReapply()
        {
            focus.Focus=true; focus.Advance(1);bridge.enabled=false;
            Assert.IsFalse(layer.overridePerLayerColorScaleAndOffset);Assert.AreEqual(Vector4.one,layer.colorScale);
            bridge.enabled=true; focus.Advance(0);Assert.That(layer.colorScale.x,Is.EqualTo(.3f).Within(.00001));
            Object.DestroyImmediate(preview);preview=null;
            Assert.IsFalse(layer.overridePerLayerColorScaleAndOffset);Assert.AreEqual(Vector4.one,layer.colorScale);
        }
        // The first-entry intro darkens the room through the same bridge without being a reward.
        [Test] public void AnyOwnerMayDarkenTheRoomAndTheDarkestRequestWins()
        {
            var intro=new GameObject("Intro owner");
            try {
                SandboxRewardPresentation.RequestRoomBrightness(intro,.12f); Assert.That(layer.colorScale.x,Is.EqualTo(.12f).Within(.00001));
                focus.Focus=true; focus.Advance(1); Assert.That(layer.colorScale.x,Is.EqualTo(.12f).Within(.00001),"The darker request holds while a reward opens");
                SandboxRewardPresentation.RequestRoomBrightness(intro,1); Assert.That(layer.colorScale.x,Is.EqualTo(.3f).Within(.00001),"Clearing one request leaves the other");
                focus.Restore(); Assert.IsFalse(layer.overridePerLayerColorScaleAndOffset); Assert.AreEqual(Vector4.one,layer.colorScale);
                SandboxRewardPresentation.RequestRoomBrightness(null,.5f); Assert.AreEqual(Vector4.one,layer.colorScale,"A request without an owner is ignored");
            } finally { Object.DestroyImmediate(intro); }
        }
        [Test] public void LateRigBindingAndDuplicateRestorationKeepCurrentReward()
        {
            bridge.Initialize(null);focus.Focus=true;focus.Advance(1);
            bridge.Initialize(rig.transform);focus.Advance(0);Assert.That(layer.colorScale.x,Is.EqualTo(.3f).Within(.00001));
            var other=new GameObject("Replacement reward");
            try {
                var replacement=other.AddComponent<SandboxRewardPresentation>();replacement.EnvironmentBrightness=.4f;replacement.Focus=true;replacement.Advance(1);
                focus.Restore(); focus.Restore();
                Assert.That(layer.colorScale.x,Is.EqualTo(.4f).Within(.00001));
            } finally {Object.DestroyImmediate(other);}
            Assert.IsFalse(layer.overridePerLayerColorScaleAndOffset);Assert.AreEqual(Vector4.one,layer.colorScale);
        }
    }
}
