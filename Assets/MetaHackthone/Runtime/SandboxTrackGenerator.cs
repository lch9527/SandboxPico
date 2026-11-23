using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace PhysicalAssembly
{
    public enum SandboxDeckStyle { Plain, CreekDeck }
    [Serializable]
    public sealed class SandboxRailSectionSettings
    {
        public int Index;
        public bool Enabled = true;
        public float Height = -1, Thickness = -1;
    }
    // Authoring input and saved output only. Players never generate track geometry.
    [DisallowMultipleComponent, RequireComponent(typeof(SplineContainer))]
    public sealed class SandboxTrackGenerator : MonoBehaviour
    {
        public bool AuthoredObjects => GetComponentInParent<SandboxFactoryLevel>(true) is SandboxFactoryLevel level && level.AuthoredObjects;
        public const string WidthData = "Factory.TrackWidth";
        public float DefaultWidth = 2.4f;
        public float DeckThickness = .12f;
        [Tooltip("Creek Deck bends the gallery artwork along the spline.")]
        public SandboxDeckStyle DeckStyle;
        [Tooltip("Keep real timber close up; use pixel-filtered plank seams for the distant straight-deck artwork. Rendering only.")]
        public bool FilterBridgeSeams=true;
        [Tooltip("Hide generated visuals while preserving driving collision. Use with separately authored bridge art.")]
        public bool ShowGeneratedVisuals = true;
        [Tooltip("Creek Deck source prefab. Uses its meshes, materials, and Anchor_Entry / Anchor_Exit; the source stays unchanged.")]
        public GameObject DeckModule;
        [HideInInspector] public bool Guardrails = true;
        // Retain legacy dimensions so existing scenes keep their authored rails.
        [HideInInspector] public float RailHeight = .65f, RailThickness = .1f;
        [HideInInspector] public bool LeftRailEnabled = true, RightRailEnabled = true;
        // -1 inherits the legacy dimension until that side is edited.
        [HideInInspector] public float LeftRailHeight = -1, RightRailHeight = -1;
        [HideInInspector] public float LeftRailThickness = -1, RightRailThickness = -1;
        [HideInInspector] public float RailSectionLength;
        [HideInInspector] public List<SandboxRailSectionSettings> LeftRailSections = new(), RightRailSections = new();
        public SandboxRailSectionSettings RailSection(int side,int index) =>
            (side < 0 ? LeftRailSections : RightRailSections).Find(s=>s.Index==index);
        public bool RailSectionEnabled(int side,int index) => RailEnabled(side) && (RailSection(side,index)?.Enabled ?? true);
        public float RailSectionHeight(int side,int index) {
            float value=RailSection(side,index)?.Height ?? -1;
            return value == -1 ? RailHeightForSide(side) : value;
        }
        public float RailSectionThickness(int side,int index) {
            float value=RailSection(side,index)?.Thickness ?? -1;
            return value == -1 ? RailThicknessForSide(side) : value;
        }
        public bool RailEnabled(int side) => Guardrails && (side < 0 ? LeftRailEnabled : RightRailEnabled);
        public float RailHeightForSide(int side) {
            float value = side < 0 ? LeftRailHeight : RightRailHeight;
            return value == -1 ? RailHeight : value;
        }
        public float RailThicknessForSide(int side) {
            float value = side < 0 ? LeftRailThickness : RightRailThickness;
            return value == -1 ? RailThickness : value;
        }
        public Material RoadMaterial, TimberMaterial;
        public PhysicsMaterial RoadPhysics, RailPhysics;
        [Header("Supports (course units)")]
        public bool Supports = true;
        public float SupportSpacing = 2.4f, SupportThickness = .18f, SupportInset = .2f, MaximumReach = 30;
        [Tooltip("Ground needs SandboxTrackGround, or a TerrainCollider. Track and car colliders are always excluded.")]
        public LayerMask GroundMask = ~0;
        [Header("Sampling (course units)")]
        [Tooltip("Rebuild Road and Supports recalculates smooth tangent handles while preserving control-point positions and widths. Turn off to keep hand-edited tangents.")]
        public bool AutoSmoothOnRebuild = true;
        [Tooltip("Off: edit points and puzzle settings first, then click Rebuild. Saving/testing also rebuilds pending changes.")]
        public bool LiveRebuild;
        [Min(.02f)] public float SampleSpacing = .2f;
        [HideInInspector] public GameObject Generated;
        [HideInInspector] public string BakedSignature, ValidationError, SupportWarning, MeshAssetPath, BakeOwner;
        [HideInInspector] public Vector3[] Samples = Array.Empty<Vector3>();
        public SplineContainer Curve => GetComponent<SplineContainer>();
        void Start()
        {
            if(FilterBridgeSeams && DeckStyle==SandboxDeckStyle.CreekDeck && ShowGeneratedVisuals && !GetComponent<BridgeDeckLod>())
                gameObject.AddComponent<BridgeDeckLod>().hideFlags=HideFlags.DontSave;
        }

        public float KnotWidth(int knot)
        {
            var data = Curve.Spline.GetOrCreateFloatData(WidthData);
            for (int i = 0; i < data.Count; i++)
                if (Mathf.Abs(data[i].Index - knot) < .0001f) return data[i].Value;
            return DefaultWidth;
        }
        public void SetKnotWidth(int knot, float width)
        {
            if (knot < 0 || knot >= Curve.Spline.Count) throw new ArgumentOutOfRangeException(nameof(knot));
            var data = Curve.Spline.GetOrCreateFloatData(WidthData);
            data.PathIndexUnit = PathIndexUnit.Knot;
            for (int i = 0; i < data.Count; i++)
                if (Mathf.Abs(data[i].Index - knot) < .0001f) { data[i] = new DataPoint<float>(knot, width); return; }
            data.Add(knot, width);
        }
        public float WidthAt(float knotParameter)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(knotParameter), 0, Curve.Spline.Count - 2);
            return Mathf.Lerp(KnotWidth(i), KnotWidth(i + 1), Mathf.SmoothStep(0, 1, knotParameter - i));
        }
    }
}
