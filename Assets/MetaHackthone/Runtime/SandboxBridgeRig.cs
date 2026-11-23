using UnityEngine;

namespace PhysicalAssembly
{
    // Cosmetic rig follows the real hinged deck. It never drives the vehicle collider.
    public sealed class SandboxBridgeRig : MonoBehaviour
    {
        public SandboxDropBridge Bridge;
        public Transform[] DeckEyes, Pulleys, TailGuides;
        public LineRenderer[] Cables;
        public Transform Drum, ReleaseLever;
        Quaternion drumRest, leverRest;
        bool captured;
        float releaseTime = -1;
        public void ResetVisuals()
        {
            if (!captured)
            {
                if (Drum) drumRest = Drum.localRotation;
                if (ReleaseLever) leverRest = ReleaseLever.localRotation;
                captured = true;
            }
            releaseTime = -1;
            if (Drum) Drum.localRotation = drumRest;
            if (ReleaseLever) ReleaseLever.localRotation = leverRest;
            UpdateRig(0);
        }
        public void Release() { releaseTime = Time.time; }
        void LateUpdate()
        {
            if (!Bridge) return;
            if (Bridge.Released && releaseTime < 0) releaseTime = Time.time;
            UpdateRig(Bridge.Released ? Mathf.Clamp01((Time.time - releaseTime) / .8f) : 0);
        }
        public void UpdateRig(float released)
        {
            if (!Bridge || !Bridge.Deck || !Bridge.Pivot) return;
            float angle = Quaternion.Angle(Bridge.Pivot.rotation, Bridge.Deck.rotation);
            if (Drum) Drum.localRotation = drumRest * Quaternion.AngleAxis((Bridge.RaisedAngle - angle) * 3, Vector3.right);
            if (ReleaseLever) ReleaseLever.localRotation = leverRest * Quaternion.AngleAxis(-68 * Mathf.SmoothStep(0, 1, released), Vector3.forward);
            for (int i = 0; i < Cables.Length; i++)
            {
                if (!Cables[i] || i >= DeckEyes.Length || i >= Pulleys.Length || i >= TailGuides.Length) continue;
                var a = DeckEyes[i].position; var b = Pulleys[i].position; var c = TailGuides[i].position;
                // The released line remains visible, pays out through its pulley and droops at the side.
                float scale = Bridge.transform.lossyScale.x;
                Cables[i].positionCount = 17;
                for (int j = 0; j < 9; j++)
                {
                    float t = j / 8f;
                    Cables[i].SetPosition(j, Vector3.Lerp(a, b, t) - Bridge.transform.up * (Mathf.Sin(t * Mathf.PI) * released * .12f * scale));
                }
                var end = c - Bridge.transform.up * (released * .85f * scale);
                for (int j = 1; j <= 8; j++)
                {
                    float t = j / 8f;
                    Cables[i].SetPosition(8 + j, Vector3.Lerp(b, end, t) - Bridge.transform.up * (Mathf.Sin(t * Mathf.PI) * released * .22f * scale));
                }
            }
        }
        void OnEnable() { if (Bridge) ResetVisuals(); }
    }
}
