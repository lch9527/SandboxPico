using UnityEngine;

namespace PhysicalAssembly
{
    [SelectionBase]
    public sealed class BuildingPartSpawn : MonoBehaviour
    {
        public PartKind Kind;
        [HideInInspector] public GameObject Preview;
        public Pose Pose => new(transform.position,transform.rotation);
        // Marker scale sizes the complete preview; bake the same uniform factor
        // into generated physics geometry rather than scaling Rigidbody roots.
        public float SizeMultiplier => Mathf.Clamp(transform.localScale.x,.1f,5f)*(GetComponentInParent<SandboxLevelLayout>(true) is SandboxLevelLayout layout ? layout.BuildingScale : 1f);
        void OnDrawGizmosSelected()
        {
            Gizmos.color=Color.cyan;
            Gizmos.DrawWireSphere(transform.position,.025f);
            Gizmos.DrawRay(transform.position,transform.forward*.12f);
        }
    }
}
