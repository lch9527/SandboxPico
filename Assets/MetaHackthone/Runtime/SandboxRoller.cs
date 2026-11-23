using UnityEngine;

namespace PhysicalAssembly
{
    public sealed class SandboxRoller : SandboxPuzzle
    {
        [Min(.02f)] public float Width = 2.4f, Diameter = .35f;
        public float Height;
        public override string ValidatePuzzle() => Width > 0 && Diameter > 0 && float.IsFinite(Width + Diameter + Height) ? null : "Roller dimensions must be positive and finite.";
        public override void BuildGeometry(Transform root)
        {
            var go = Shape(root, RollerName, PrimitiveType.Cylinder, new Vector3(0, Height + Diameter * .5f, 0),
                new Vector3(Diameter, Width * .5f, Diameter), Quaternion.Euler(0, 0, 90));
            // A capsule's rounded ends do not match a cylinder. Use the actual static cylinder mesh.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            EnsureVisual(root);
        }
        public const string RollerName = "Physical roller", GravelName = "Gravel", BumpName = "Speed bump";
        // The cylinder stays as the physical bump; a striped speed bump is what the player sees.
        // The earlier gravel heap remains the fallback when the car part art kit is missing.
        void EnsureVisual(Transform root)
        {
            var roller = root.Find(RollerName); if (!roller) return;
            var bump = root.Find(BumpName);
            if (!bump) bump = CarPartArtLibrary.AddSpeedBump(root, BumpName, Width, Diameter, Height);
            var gravel = root.Find(GravelName);
            if (bump && gravel) { if (Application.isPlaying) Destroy(gravel.gameObject); else DestroyImmediate(gravel.gameObject); gravel = null; }
            if (!bump && !gravel) gravel = SandboxStoneLibrary.AddGravel(root, GravelName, Width, Diameter, Height, Mathf.RoundToInt(Distance * 100) + 1);
            if ((bump || gravel) && roller.TryGetComponent<Renderer>(out var renderer)) renderer.enabled = false;
        }
        // Levels baked with the bare roller or the gravel look are upgraded when they load.
        void Start() { if (Application.isPlaying && Generated) EnsureVisual(Generated.transform); }
        // Editor entry point: swaps a roller already baked into a scene to the current look without rebaking the track.
        // True when the generated children changed.
        public bool RefreshVisual()
        {
            if (!Generated) return false;
            bool before = Generated.transform.Find(BumpName);
            EnsureVisual(Generated.transform);
            return before != (bool)Generated.transform.Find(BumpName) || !Generated.transform.Find(GravelName);
        }
    }
}
