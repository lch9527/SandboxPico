using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace PhysicalAssembly.Tests
{
    public sealed partial class SandboxVictoryTests
    {
        [UnityTest] public IEnumerator AudioRewardRevealAndClaimEachPlayOnce()
        {
            var cues=new List<SandboxSoundCue>();level.Audio.CuePlayed+=cues.Add;
            SandboxCampaignSession.Complete(SandboxCourseId.GravityGarage);yield return null;
            Assert.IsNotNull(level.Reward.Preview);Assert.AreEqual(1,cues.Count(c=>c==SandboxSoundCue.Reveal));
            Assert.AreEqual(13,level.Vfx.ActiveParticles,"One reveal sparkle: twelve motes and a flash");
            level.Reward.Reveal();level.Reward.Reveal();
            Assert.AreEqual(1,cues.Count(c=>c==SandboxSoundCue.Reveal));
            level.Reward.Claim();level.Reward.Claim();
            Assert.AreEqual(1,cues.Count(c=>c==SandboxSoundCue.Claim));
            Assert.AreEqual(1,level.Vfx.ActiveRings,"One contracting claim pulse");
            Assert.AreEqual(13,level.Vfx.ActiveParticles,"Claim releases the reveal sparkle and plays one claim burst");
            Assert.IsNull(level.Reward.Preview);
            UnityEngine.Object.DestroyImmediate(level.Reward);
            Assert.AreEqual(1,level.Vfx.ActiveRings,"Claim pulse survives removal of the old coordinator on advance");
            yield return new UnityEngine.WaitForSecondsRealtime(level.Vfx.Settings.RewardSeconds+.05f);
            Assert.AreEqual(0,level.Vfx.ActiveRings);
        }
        [UnityTest] public IEnumerator AudioRewardCancellationNeverPlaysClaim()
        {
            var cues=new List<SandboxSoundCue>();level.Audio.CuePlayed+=cues.Add;
            SandboxCampaignSession.Complete(SandboxCourseId.GravityGarage);yield return null;
            Assert.IsNotNull(level.Reward.Preview);
            level.Reward.Cancel();level.Reward.Claim();yield return null;
            Assert.AreEqual(0,cues.Count(c=>c==SandboxSoundCue.Claim));
            Assert.AreEqual(0,level.Vfx.ActiveParticles);Assert.AreEqual(0,level.Vfx.ActiveRings);
        }
    }
}
