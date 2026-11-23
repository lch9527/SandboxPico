using System;
using System.Linq;
using UnityEngine;

namespace PhysicalAssembly
{
    [DisallowMultipleComponent]
    public sealed class SandboxFactoryLevel : MonoBehaviour
    {
        [Tooltip("Keep scene objects exactly as edited. Track and puzzle generators cannot replace geometry; saving a campaign scene also saves it to the game.")]
        public bool AuthoredObjects;
        public SandboxLevelLayout Layout;
        public float WorldCourseScale => CourseScale*(Layout?Layout.SandboxScale:1f);
        public string Title = "New factory level";
        public Transform TrackRoot, Start;
        public BoxCollider Finish, TestBounds;
        public SandboxTrackGenerator[] Tracks = Array.Empty<SandboxTrackGenerator>();
        public SandboxTestCarAsset TestCar;
        public SandboxTestCarAsset[] Presets = Array.Empty<SandboxTestCarAsset>();
        [HideInInspector] public BuildingPartSpawn[] CampaignSpawns = Array.Empty<BuildingPartSpawn>();
        [Tooltip("Use the Gravity Garage starter parts and locking rules instead of a custom factory palette.")]
        public bool UseStarterPalette;
        [Tooltip("Optional workbench art, separate from the moving course. Its pose is relative to the layout root.")]
        public Transform WorkbenchVisuals;
        public Transform[] Scenery = Array.Empty<Transform>();
        [Tooltip("Use visual LODs for fine foliage and display-base seams. Does not change collisions or editable source meshes.")]
        public bool FilterSceneryDetails = true;
        void Awake()
        {
            if(FilterSceneryDetails && !GetComponent<SceneryDetailLod>())
                gameObject.AddComponent<SceneryDetailLod>().hideFlags=HideFlags.DontSave;
        }
        public bool BindStart;
        public SandboxTrackBinding StartBinding;
        public bool DebugPhysics;
        [Tooltip("Center wheel-supported vehicles and guide their heading. Pitch, roll and vertical motion remain physical.")]
        public bool CenterPathAssist = true;
        [Tooltip("Disable heading and centering throughout obstacle sections, including the complete vehicle footprint.")]
        public bool LimitedPathAssist = true;
        [Tooltip("Additional approach/exit clearance around puzzle sections, in course units.")]
        [Min(0)] public float AssistPuzzleClearance = 1;
        public SandboxPuzzle[] Puzzles => TrackRoot ? TrackRoot.GetComponentsInChildren<SandboxPuzzle>(true) : Array.Empty<SandboxPuzzle>();
        public void ResetPuzzles(bool running)
        {
            foreach(var p in Puzzles) if(p.isActiveAndEnabled) p.ResetPuzzle(running);
        }
        [Min(.001f)] public float CourseScale = .08f;
        [Min(1)] public float Timeout = 60;
        public static event Action<SandboxFactoryLevel, AssemblySaveV1> SaveCarRequested;
        public void RequestSaveCar(DownhillController controller)
        {
            try {
                if (!controller.IsBuilding || !controller.CanSwitchMode || controller.Rotating)
                    throw new InvalidOperationException("Return to Build and release parts before saving.");
                var save = controller.Workshop.Saves.Capture(controller.Core);
                AssemblySaveService.Validate(save);
                if (SaveCarRequested == null) throw new InvalidOperationException("Save Test Car is available in the Unity Editor.");
                SaveCarRequested(this, save);
            } catch (Exception e) { controller.Workshop.Session.Notify(e.Message); }
        }
        public bool Validate(out string error)
        {
            error = "";
            if(Layout && !Layout.Validate(out error)) return false;
            if (!Start || !Finish || !TestBounds || !TrackRoot) error = "Assign start, finish, track root, and test bounds.";
            else if (!float.IsFinite(CourseScale) || CourseScale <= 0 || !float.IsFinite(Timeout) || Timeout < 1) error = "Course scale and timeout must be positive.";
            else if (Tracks == null || Tracks.Length == 0 || Tracks.Any(t => !t || !t.Generated || t.Samples.Length < 2 || !string.IsNullOrEmpty(t.ValidationError))) error = "Add and bake valid tracks before testing.";
            else if(Puzzles.Any(p=>p.isActiveAndEnabled && (!p.Track || !string.IsNullOrEmpty(p.ValidationError)))) error="Fix puzzle placement errors before testing.";
            return error.Length == 0;
        }
        public static bool Contains(BoxCollider box, Vector3 world) => box &&
            new Bounds(box.center, box.size).Contains(box.transform.InverseTransformPoint(world));
        void OnDrawGizmos()
        {
            void Box(BoxCollider box, Color color) {
                if (!box) return;
                Gizmos.matrix = box.transform.localToWorldMatrix; Gizmos.color = color; Gizmos.DrawWireCube(box.center, box.size);
            }
            var matrix = Gizmos.matrix; var old = Gizmos.color;
            // The editor draws a pickable goal gizmo on Finish itself.
            Box(TestBounds, new Color(.3f,.7f,1));
            Gizmos.matrix = Matrix4x4.identity;
            if (Start) { Gizmos.color = Color.yellow; Gizmos.DrawRay(Start.position, Start.forward * CourseScale * 3); Gizmos.DrawWireSphere(Start.position, CourseScale * .3f); }
            Gizmos.matrix = matrix; Gizmos.color = old;
        }
    }
}
