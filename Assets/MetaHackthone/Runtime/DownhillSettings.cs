using UnityEngine;

namespace PhysicalAssembly
{
    [CreateAssetMenu(menuName="MetaHackthone/Downhill sandbox")]
    public sealed class DownhillSettings : ScriptableObject
    {
        [Tooltip("Optional override; defaults to Resources/SandboxSoundBank.")]
        public SandboxSoundBank SoundBank;
        public SandboxVfxSettings Vfx = new();
        public bool SandboxDrillCourse;
        public bool UseGravityGarage;
        public bool Campaign;
        public bool DesignerBuilder;
        [Range(1,8)] public float RaySocketAngle=3f;
        public GravityGarageLevelAsset Level25D;
        public GravityGarage3DLevelAsset Level3D;
        [Tooltip("Published final factory course played after Waterfall Valley.")]
        public SandboxFactoryLevel Level4; // Legacy serialized final-course reference, now displayed as Level 5.
        public SandboxFactoryLevel WheelSizeCourse, CrabBridgeCourse, DrillBridgeCourse;
        public SandboxFactoryLevel FactoryCourse(SandboxCourseId id) => id switch {
            SandboxCourseId.BeautifulCorner=>Level1, SandboxCourseId.GravityGarage=>WheelSizeCourse,
            SandboxCourseId.TeleportPipes=>CrabBridgeCourse, SandboxCourseId.DrillDropBridge=>DrillBridgeCourse,
            SandboxCourseId.FactoryLevel4=>Level4, _=>null };
        [UnityEngine.Serialization.FormerlySerializedAs("DesignBeautifulCorner")]
        [Tooltip("Published Beautiful Corner course for campaign and Design Mode.")]
        public SandboxFactoryLevel Level1;
        public GravityGarageDesign Garage=GravityGarageDesign.UnityDefaults();
        public const float VehicleScale = 2f / 3f;
        [Range(.1f,1f)] public float Level2VehicleScale=.6f;
        [Header("Assembly and presentation")]
        [Range(.25f,1f)] public float WorkbenchScale=.5f;
        [Range(1f,1.5f)] public float PresentationScale=1.2f;
        [Range(.05f,.15f)] public float SnapRadius=.09f;
        public BuildSpringSettings BuildSpring=new();
        [Header("Floating Start control")]
        public Vector3 StartBuildOffset=new(-.70f,.30f,-.10f);
        public Vector2 StartButtonSize=new(.22f,.24f);
        [Header("Test flow")]
        [Min(0)] public float TransitionDuration=1.2f;
        public AnimationCurve TransitionEasing=AnimationCurve.EaseInOut(0,0,1,1);
        public float WorkbenchApproach=.45f;
        public Vector3 WorkbenchLevelOffset=Vector3.zero;
        [Range(.25f,1f)] public float PhysicsSpeed=.8f;
        [Min(.01f)] public float CountdownStep=1f;
        [Min(.01f)] public float GoDuration=.3f;
        [Tooltip("Campaign launches skip the 3-2-1 countdown; the car rests this long at the start before physics begins.")]
        [Min(0)] public float LaunchPause=.15f;
        [Tooltip("After reaching the finish the car brakes to a stop over this many car lengths.")]
        [Min(0)] public float FinishBrakeLengths=1.5f;
        public float ReturnDelay = 10, AttemptDuration = 30, StallDuration = 3;
        public float SmallWheelResistance = .005f, LargeWheelResistance = .5f;
        public float BarrierHeight = .018f, GapWidth = .040f;
        public float SurfaceHeight = .70f, PlayerDistance = .65f, SandboxGap = .08f;
        public Vector3 WorkbenchSize = new(1.4f,.06f,1.0f);
        public Vector2 GridSize = new(.49f,.47f);
        public Vector3 CoreSize = new(.18f,.08f,.18f);
        public Vector3 CorePosition = new(0,.90f,0);
        public Vector2 LargeTrayOffset = new(-.49f,.15f), SmallTrayOffset = new(.49f,.15f);
        public float FrontTrayZ = -.36f, FrontTraySpacing = .21f;
        public Vector3 KitPosition(int index) => index switch
        {
            0 => CorePosition,
            1 or 2 => new Vector3(LargeTrayOffset.x,SurfaceHeight+.141f,(index==1?-1:1)*LargeTrayOffset.y),
            3 or 4 => new Vector3(SmallTrayOffset.x,SurfaceHeight+.086f,(index==3?-1:1)*SmallTrayOffset.y),
            _ => new Vector3((index-6)*FrontTraySpacing,SurfaceHeight+(index==6?.081f:.051f),FrontTrayZ)
        };
        // Preserve all authored course coordinates, translating its support top from .71 to SurfaceHeight.
        public Vector3 SandboxOffset => new(0,SurfaceHeight-.71f,WorkbenchSize.z*.5f+SandboxGap+.39f-.65f);
        public Vector3 StartPosition = new(-1.16f,1.19f,.65f);
        public Quaternion StartRotation => Quaternion.LookRotation(new Vector3(1,-.35f,0));
        public Bounds FinishBounds => new(new Vector3(1.18f,.89f,.65f),new Vector3(.30f,.32f,.64f));
    }
}



































