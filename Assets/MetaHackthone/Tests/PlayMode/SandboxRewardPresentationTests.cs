using NUnit.Framework;
using UnityEngine;

namespace PhysicalAssembly.Tests
{
    public sealed class SandboxRewardPresentationTests
    {
        [Test] public void FocusDimsOnlyCopiesAndRestoresOriginalMaterials()
        {
            var environment=GameObject.CreatePrimitive(PrimitiveType.Cube);var preview=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var original=environment.GetComponent<Renderer>().sharedMaterial;var rewardMaterial=preview.GetComponent<Renderer>().sharedMaterial;
            try {
                var focus=preview.AddComponent<SandboxRewardPresentation>();focus.Initialize(environment.transform,preview.transform);
                var copy=environment.GetComponent<Renderer>().sharedMaterial;
                Assert.AreNotSame(original,copy);focus.Focus=true;focus.Advance(1);
                Assert.AreEqual(1,focus.Amount);Assert.AreSame(rewardMaterial,preview.GetComponent<Renderer>().sharedMaterial);
                if(original.HasProperty("_Color"))Assert.That(copy.GetColor("_Color").r,Is.EqualTo(original.GetColor("_Color").r*.3f).Within(.001));
                focus.Focus=false;focus.Advance(1);Assert.AreEqual(0,focus.Amount);
                focus.Restore();Assert.AreSame(original,environment.GetComponent<Renderer>().sharedMaterial);Assert.IsEmpty(SandboxRewardPresentation.Draws);
            } finally {Object.DestroyImmediate(preview);Object.DestroyImmediate(environment);}
        }
    }
}
