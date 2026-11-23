using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PhysicalAssembly.Tests
{
    public sealed class SandboxSoundBankTests
    {
        [Test] public void ShippingBankContainsEveryCueWithValidRoutingAndVariations()
        {
            var bank=Resources.Load<SandboxSoundBank>("SandboxSoundBank");Assert.IsNotNull(bank);
            Assert.AreEqual(Enum.GetValues(typeof(SandboxSoundCue)).Length,bank.Sounds.Length);
            foreach(var sound in bank.Sounds) {
                Assert.IsNotNull(bank.Group(sound.Bus),sound.Cue.ToString());
                Assert.IsTrue(sound.Clips.Length>0 && sound.Clips.All(c=>c),sound.Cue.ToString());
                Assert.That(sound.Volume,Is.InRange(0,1));Assert.Greater(sound.MaxDistance,sound.MinDistance);
                if(sound.Cue<=SandboxSoundCue.WheelTick || sound.Cue>=SandboxSoundCue.WoodImpact && sound.Cue<=SandboxSoundCue.MetalImpact
                    || sound.Cue==SandboxSoundCue.GrabWheel || sound.Cue==SandboxSoundCue.GrabDrill)
                    Assert.GreaterOrEqual(sound.Clips.Distinct().Count(),4,sound.Cue.ToString());
            }
        }
        [Test] public void ShippingClipsHaveHeadroomAndContinuousLoopSeams()
        {
            var bank=Resources.Load<SandboxSoundBank>("SandboxSoundBank");
            foreach(var sound in bank.Sounds) foreach(var clip in sound.Clips) {
                Assert.AreEqual(1,clip.channels,clip.name);Assert.AreEqual(48000,clip.frequency,clip.name);
                Assert.IsTrue(clip.LoadAudioData());var data=new float[clip.samples];Assert.IsTrue(clip.GetData(data,0));
                Assert.Less(data.Max(x=>Mathf.Abs(x)),.7f,clip.name);Assert.Greater(data.Max(x=>Mathf.Abs(x)),.05f,clip.name);
                if(sound.Cue>=SandboxSoundCue.Rolling && sound.Cue<=SandboxSoundCue.Water) Assert.Less(Mathf.Abs(data[0]-data[data.Length-1]),.04f,clip.name);
                else {Assert.Less(Mathf.Abs(data[0]),.001f,clip.name);Assert.Less(Mathf.Abs(data[data.Length-1]),.001f,clip.name);}
                var importer=(AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
                Assert.AreEqual(AudioClipLoadType.DecompressOnLoad,importer.defaultSampleSettings.loadType);
                Assert.IsTrue(importer.defaultSampleSettings.preloadAudioData,clip.name);
            }
        }
    }
}
