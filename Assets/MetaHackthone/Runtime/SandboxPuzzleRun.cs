using System.Linq;
using UnityEngine;

namespace PhysicalAssembly
{
    // On the existing simulation root, so the existing drill discovers this adapter.
    public sealed class SandboxPuzzleRun : MonoBehaviour, ISandboxDrillTarget
    {
        public DownhillController Level;
        bool probesAdded;
        void Update()
        {
            if(probesAdded || !Level || !Level.FactoryLevel.DebugPhysics || !Level.SimulationWorld) return;
            foreach(var body in Level.SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies)) body.gameObject.AddComponent<SandboxPuzzleContact>();
            probesAdded=true;
        }
        public bool DrillActive(AssemblyPart part) => Level && Level.State == DownhillState.Running && part && part.Kind == PartKind.DownhillDrill;
        public void SweepDrill(AssemblyPart part, Vector3 from, Vector3 to, float radius)
        {
            if (!DrillActive(part)) return;
            var delta = to - from; float length = delta.magnitude;
            var contacts = Physics.OverlapSphere(to, radius, ~0, QueryTriggerInteraction.Ignore).ToList();
            if (length > .000001f) contacts.AddRange(Physics.SphereCastAll(from, radius, delta / length, length, ~0, QueryTriggerInteraction.Ignore).Select(h => h.collider));
            // The bit's collision box is what physically meets the target. If its side or edge
            // touches first, the car is stopped before the tip line can reach the target, so any
            // contact between that box and a target counts as a drill hit. The margin covers this
            // step's travel: a hard impact can rebound the car before the next sample.
            var bits = (part.Bodies ?? System.Array.Empty<AssemblyBody>()).Where(b => b).SelectMany(b => b.Colliders).OfType<BoxCollider>().Where(c => c && c.enabled && c.name == "Bit collision").ToArray();
            var touching = new System.Collections.Generic.Dictionary<Collider, Vector3>();
            foreach (var bit in bits) {
                var t = bit.transform; var center = t.TransformPoint(bit.center);
                var half = Vector3.Scale(bit.size * .5f, t.lossyScale) + Vector3.one * (radius * .5f + length);
                foreach (var hit in Physics.OverlapBox(center, half, t.rotation, ~0, QueryTriggerInteraction.Ignore))
                    if (!touching.ContainsKey(hit)) touching.Add(hit, center);
            }
            foreach (var puzzle in Level.FactoryLevel.Puzzles)
                if (puzzle.DrillCollider && (contacts.Contains(puzzle.DrillCollider) || touching.ContainsKey(puzzle.DrillCollider))) {
                    var target=puzzle.DrillCollider;
                    var at=contacts.Contains(target) ? to : target.ClosestPoint(touching[target]);
                    var point=target.ClosestPoint(at);
                    bool wasReleased=puzzle is SandboxDropBridge before && before.Released;
                    puzzle.DrillHit(at);
                    // The drill broke the target: the stone cracks apart and the bridge deck drops.
                    if(!wasReleased && puzzle is SandboxDropBridge broken && broken.Released && Level.Feedback)
                        Level.Feedback.Emit(SandboxSoundCue.DrillBreak,point,part,(from-to).normalized,1,SandboxSurfaceMaterial.Wood,.3f);
                    // The breaking hit already owns physical stone debris. Only sustained contact adds chips.
                    if(puzzle.DrillCollider && Level.Feedback) Level.Feedback.DrillContact(part,point,(from-to).normalized);
                    // The stone target shatters into physical chunks; keep them from ever touching the car.
                    if (puzzle is SandboxDropBridge bridge && bridge.Debris && Level.SimulationWorld)
                        foreach (var body in Level.SimulationWorld.Parts.Values.SelectMany(p => p.Bodies))
                            foreach (var collider in body.Colliders) bridge.Debris.Ignore(collider);
                }
        }
        void OnDrawGizmos()
        {
            if (!Level || !Level.FactoryLevel || !Level.FactoryLevel.DebugPhysics || !Level.SimulationWorld) return;
            foreach (var body in Level.SimulationWorld.Parts.Values.SelectMany(p => p.Bodies)) {
                var rb = body.Rigidbody; Gizmos.color = Color.yellow; Gizmos.DrawSphere(rb.worldCenterOfMass, .004f);
                Gizmos.color = Color.cyan; Gizmos.DrawRay(rb.worldCenterOfMass, rb.linearVelocity * .2f);
                Gizmos.color = Color.magenta; Gizmos.DrawRay(rb.worldCenterOfMass, rb.angularVelocity * .02f);
            }
        }
        void OnGUI()
        {
            if(!Level || !Level.FactoryLevel.DebugPhysics || !Level.SimulationCore) return;
            var rb=Level.SimulationCore.MainBody.Rigidbody;
            GUI.Label(new Rect(16,Screen.height-60,650,50),$"Physics: {Level.State}   Speed {rb.linearVelocity.magnitude:0.000} m/s   Spin {rb.angularVelocity.magnitude:0.00} rad/s\nYellow: center of mass   Cyan: velocity   Magenta: angular velocity   Red: contact normals");
        }
    }
    public sealed class SandboxPuzzleContact : MonoBehaviour
    {
        public string LastSurface; public Vector3 Point, Normal;
        void OnCollisionStay(Collision collision)
        {
            if(collision.contactCount==0) return;
            LastSurface=collision.collider.name; var contact=collision.GetContact(0); Point=contact.point; Normal=contact.normal;
        }
        void OnDrawGizmos() { if(string.IsNullOrEmpty(LastSurface)) return; Gizmos.color=Color.red; Gizmos.DrawRay(Point,Normal*.06f); }
    }
}
