using UnityEngine;

namespace PhysicalAssembly
{
    public sealed class SandboxCrabBridge : SandboxPuzzle
    {
        [Min(.1f)] public float Length = 5;
        [Min(.02f)] public float HoleWidth = 1.2f, LeftLaneWidth = .6f, RightLaneWidth = .6f;
        public override float Span => Length;
        public override bool ReplacesRoad => true;
        public override Vector2[] RoadBands(float width) => new[] {
            new Vector2(-width*.5f, Offset.x - HoleWidth * .5f),
            new Vector2(Offset.x + HoleWidth * .5f, width*.5f) };
        public override string ValidatePuzzle() => Length > 0 && HoleWidth > 0 && LeftLaneWidth > 0 && RightLaneWidth > 0 &&
            float.IsFinite(Length + HoleWidth + LeftLaneWidth + RightLaneWidth) ? null : "Crab dimensions must be positive and finite.";
        public override void BuildGeometry(Transform root) { /* The track baker owns both lane meshes and colliders. */ }
    }
}
