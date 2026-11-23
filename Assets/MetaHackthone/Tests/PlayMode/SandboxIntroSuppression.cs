using NUnit.Framework;
using PhysicalAssembly;

// Applies to every play-mode test in this assembly: levels load straight into gameplay.
// The first-entry intro hides the kit and locks input, which no other fixture expects.
// The intro's own tests switch it back on for themselves.
[SetUpFixture]
public sealed class SandboxIntroSuppression
{
    [OneTimeSetUp] public void Suppress() => SandboxFirstEntryIntro.Suppressed=true;
    [OneTimeTearDown] public void Release() => SandboxFirstEntryIntro.Suppressed=false;
}
