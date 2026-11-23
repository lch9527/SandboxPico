using System;
using UnityEngine;
using UnityEngine.Audio;

namespace PhysicalAssembly
{
    public enum SandboxSoundCue
    {
        Grab, Attach, Detach, Reject, Button, WheelTick, Countdown, Go,
        Gather, Travel, Arrive, Success, Failure, Reveal, Claim,
        WoodImpact, RubberImpact, MetalImpact, Splash, Rolling, Rattle, Drill, Water,
        // Appended: the bank stores cues by number, so new cues always go at the end.
        GrabWheel, GrabDrill, DrillBreak
    }
    public enum SandboxAudioBus { Interactions, Vehicle, Ambience, Rewards }
    public enum SandboxSurfaceMaterial { Wood, Rubber, Metal }

    [Serializable]
    public sealed class SandboxSoundEntry
    {
        public SandboxSoundCue Cue;
        public AudioClip[] Clips = Array.Empty<AudioClip>();
        public SandboxAudioBus Bus;
        [Range(0, 1)] public float Volume = .25f;
        [Range(0, .15f)] public float PitchVariation = .035f;
        [Min(0)] public float Cooldown = .08f;
        [Range(1, 12)] public int MaxVoices = 3;
        [Range(0, 1)] public float SpatialBlend = 1;
        [Min(.01f)] public float MinDistance = .65f;
        [Min(.1f)] public float MaxDistance = 8;
    }

    [CreateAssetMenu(menuName = "MetaHackthone/Sandbox sound bank")]
    public sealed class SandboxSoundBank : ScriptableObject
    {
        public SandboxSoundEntry[] Sounds = Array.Empty<SandboxSoundEntry>();
        public AudioMixerGroup Interactions, Vehicle, Ambience, Rewards;
        [Range(1, 16)] public int OneShotVoices = 12;
        [Min(0)] public float MinimumImpactSpeed = .12f;
        [Min(.01f)] public float FullImpactSpeed = 1.5f;
        [Min(.01f)] public float FullRollingSpeed = 1.2f;
        [Range(0, 1)] public float RewardAmbienceMultiplier = .3f;
        public SandboxSoundEntry Find(SandboxSoundCue cue) => Array.Find(Sounds, sound => sound != null && sound.Cue == cue);
        public AudioMixerGroup Group(SandboxAudioBus bus) => bus switch
        {
            SandboxAudioBus.Vehicle => Vehicle, SandboxAudioBus.Ambience => Ambience,
            SandboxAudioBus.Rewards => Rewards, _ => Interactions
        };
    }
}
