using System;
using UnityEngine;

namespace PhysicalAssembly
{
    public interface ISandboxPuzzleReset { void ResetPuzzle(bool running); }

    // Scene-owned authoring input. Generated children are disposable; the placement is not.
    [SelectionBase]
    public abstract class SandboxPuzzle : MonoBehaviour, ISandboxPuzzleReset
    {
        public SandboxTrackGenerator Track;
        [Min(0)] public float Distance;
        public Vector3 Offset;
        public Vector3 Rotation;
        public Material Material;
        [HideInInspector] public GameObject Generated;
        [HideInInspector] public string GeometrySignature, ValidationError;
        public virtual float Span => 0;
        public virtual bool ReplacesRoad => false;
        public virtual Vector2[] RoadBands(float width) => Array.Empty<Vector2>();
        public virtual string ValidatePuzzle() => null;
        public abstract void BuildGeometry(Transform root);
        public virtual void ResetPuzzle(bool running) { }
        public virtual void DrillHit(Vector3 position) { }
        public virtual Collider DrillCollider => null;
        public string Signature { get { var saved = GeometrySignature; GeometrySignature = ""; var json = JsonUtility.ToJson(this); GeometrySignature = saved; return json+transform.lossyScale.ToString("R"); } }

        protected GameObject Shape(Transform root, string label, PrimitiveType type, Vector3 position, Vector3 size, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(type); go.name = label;
            go.transform.SetParent(root, false); go.transform.SetLocalPositionAndRotation(position, rotation); go.transform.localScale = size;
            var material = Material ? Material : Track ? Track.TimberMaterial : null;
            if (material) go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
        protected GameObject Box(Transform root, string label, Vector3 position, Vector3 size)
            => Shape(root, label, PrimitiveType.Cube, position, size, Quaternion.identity);
    }

    [Serializable]
    public struct SandboxTrackBinding
    {
        public SandboxTrackGenerator Track;
        public float Distance;
        public Vector3 Offset, Rotation;
    }
}
