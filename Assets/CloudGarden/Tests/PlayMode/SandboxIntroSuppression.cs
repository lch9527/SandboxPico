using NUnit.Framework;
using PhysicalAssembly;

// Applies to every play-mode test in this assembly: levels load straight into gameplay.
// The first-entry intro hides the kit and locks input, which no fixture here expects.
[SetUpFixture]
public sealed class CloudGardenIntroSuppression
{
    [OneTimeSetUp] public void Suppress() => SandboxFirstEntryIntro.Suppressed=true;
    [OneTimeTearDown] public void Release() => SandboxFirstEntryIntro.Suppressed=false;
}
