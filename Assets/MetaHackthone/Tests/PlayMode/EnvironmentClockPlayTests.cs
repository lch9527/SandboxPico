using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    public sealed class EnvironmentClockPlayTests
    {
        [UnityTest] public IEnumerator LandscapeKeepsMovingWhenPhysicsClockIsPaused()
        {
            float scale=Time.timeScale;
            try {
                yield return null;yield return null;
                Assert.IsNotNull(Object.FindFirstObjectByType<EnvironmentShaderClock>());
                Time.timeScale=0;
                float start=Shader.GetGlobalVector("_CreekClock").x;
                yield return new WaitForSecondsRealtime(.25f);yield return null;
                var clock=Shader.GetGlobalVector("_CreekClock");
                Assert.AreEqual(1,clock.y);Assert.Greater(clock.x-start,.20f);
            }
            finally {Time.timeScale=scale;}
        }
    }
}
