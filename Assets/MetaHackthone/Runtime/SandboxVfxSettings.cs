using System;
using UnityEngine;

namespace PhysicalAssembly
{
    [Serializable]
    public sealed class SandboxVfxSettings
    {
        public bool Enabled=true;
        public Color Gold=new(1,.75f,.08f), Wood=new(.66f,.44f,.22f), Rubber=new(.32f,.29f,.25f), Metal=new(.68f,.73f,.77f), Sandstone=new(.87f,.65f,.35f), Water=new(.55f,.9f,1);
        [Range(.05f,.5f)] public float GrabSeconds=.12f, AttachSeconds=.25f, RejectSeconds=.18f;
        [Range(.1f,1)] public float ImpactSeconds=.3f, SplashSeconds=.65f, RewardSeconds=.4f;
        [Range(0,1)] public float MinimumImpactIntensity=.23f;
        [Range(.05f,.8f)] public float RingRadiusInPartSizes=.28f;
        [Range(.005f,.15f)] public float ParticleSizeInPartSizes=.06f;
        [Tooltip("How far motes travel, as a multiple of the built-in distances.")]
        [Range(.5f,4)] public float Spread=2f;
        [Tooltip("Width of the soft ring band as a fraction of the ring radius.")]
        [Range(.05f,1)] public float RingWidthInRadii=.4f;
        [Range(1,8)] public int AttachMotes=8;
        [Range(0,8)] public int DetachMotes=4;
        [Range(1,8)] public int DrillMotes=4;
        [Range(1,32)] public int SplashDroplets=16;
        [Range(1,32)] public int RewardSparkles=12;
        [Range(1,48)] public int SuccessSparkles=24;
        [Range(1,64)] public int MaxParticles=64;
        [Range(1,8)] public int MaxRings=8;
    }
}
