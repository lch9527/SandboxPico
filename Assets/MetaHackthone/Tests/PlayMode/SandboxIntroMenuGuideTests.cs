using NUnit.Framework;
using UnityEngine;
namespace PhysicalAssembly.Tests {
 public sealed class SandboxIntroMenuGuideTests {
  [Test] public void RetiredIntroGuideNeverAllocatesParticles() {
   var go=new GameObject("Retired guide");
   try { int before=SandboxFragments.ActiveCount; var guide=go.AddComponent<SandboxIntroMenuGuide>(); guide.Tick(10); Assert.IsFalse(guide.Visible); Assert.AreEqual(before,SandboxFragments.ActiveCount); }
   finally { Object.DestroyImmediate(go); }
  }
 }
}
