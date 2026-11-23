using UnityEngine;

namespace PhysicalAssembly
{
    [DisallowMultipleComponent]
    public sealed class SandboxBodyAudio : MonoBehaviour
    {
        public SandboxAudioController Owner { get; private set; }
        public SandboxPresentation Presentation { get; set; }
        Vector3 Up => Presentation ? Presentation.Up : Owner ? Owner.CourseUp : Vector3.up;
        public AssemblyBody Body { get; private set; }
        public PassiveWheel Wheel { get; private set; }
        public SandboxDrill Drill { get; private set; }
        float supportedUntil = -1;
        public bool Grounded => Time.fixedTime <= supportedUntil;
        public void Initialize(SandboxAudioController owner, AssemblyBody body)
        {
            Owner = owner; Body = body; Wheel = GetComponent<PassiveWheel>(); Drill = GetComponent<SandboxDrill>();
            ResetContact();
        }
        public void ResetContact() => supportedUntil = -1;
        bool External(Collision collision)
        {
            if (!(Presentation ? Presentation.Running : Owner && Owner.VehicleRunning) || !Body || collision.contactCount == 0) return false;
            var other = collision.collider.GetComponentInParent<AssemblyPart>();
            return !other || other.World != Body.Part.World;
        }
        void Support(Collision collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
                if (Vector3.Dot(collision.GetContact(i).normal, Up) > .3f)
                { supportedUntil = Time.fixedTime + Time.fixedDeltaTime * 2; break; }
        }
        void OnCollisionEnter(Collision collision)
        {
            if (!External(collision)) return;
            Support(collision);
            float speed = 0; Vector3 point = collision.GetContact(0).point, normal=collision.GetContact(0).normal;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                float normalSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
                if (normalSpeed > speed) { speed = normalSpeed; point = contact.point; normal=contact.normal; }
            }
            var surface = collision.collider.GetComponentInParent<SandboxAudioSurface>();
            var material = surface ? surface.Material : DownhillParts.IsWheel(Body.Part.Kind)
                ? SandboxSurfaceMaterial.Rubber : Body.Part.Kind == PartKind.DownhillDrill
                ? SandboxSurfaceMaterial.Metal : SandboxSurfaceMaterial.Wood;
            if(Presentation) Presentation.Impact(material,point,normal,speed,Body.Part);
            else if(Owner) Owner.Impact(material, point, speed, GetInstanceID());
        }
        void OnCollisionStay(Collision collision) { if (External(collision)) Support(collision); }
        void OnDisable() => ResetContact();
    }
}
