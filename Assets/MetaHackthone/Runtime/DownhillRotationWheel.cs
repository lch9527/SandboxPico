using UnityEngine;

namespace PhysicalAssembly
{
    public sealed class DownhillRotationWheel : MonoBehaviour
    {
        [System.NonSerialized] public DownhillController Controller;
        public const float Radius=.17f;
        [Min(0), Tooltip("Maximum idle edge displacement in local metres.")]
        public float IdleRippleAmplitude=.002f;
        [Min(0), Tooltip("Ripple cycles per second at a point on the rim.")]
        public float IdleRippleFrequency=.6f;
        [Min(0), Tooltip("Seconds to restore the ripple after releasing the wheel.")]
        public float RippleReleaseDuration=.25f;
        static readonly int RippleAmplitudeId=Shader.PropertyToID("_RippleAmplitude");
        static readonly int RipplePhaseId=Shader.PropertyToID("_RipplePhase");
        static readonly int SweepPhaseId=Shader.PropertyToID("_SweepPhase");
        Mesh generatedMesh;
        Transform handwheelArt;
        Renderer handwheelRenderer;
        Mesh grabMesh;
        Vector3[] grabVertices;
        int[] grabTriangles;
        const float HandleGrabTolerance=.015f;

        Renderer rimRenderer;
        MaterialPropertyBlock rimProperties;
        float ripplePhase,sweepPhase,rippleFade;

        public bool IsNearRim(Vector3 point)
        {
            var local=transform.InverseTransformPoint(point);
            float radial=new Vector2(local.x,local.z).magnitude-Radius;
            if(radial*radial+local.y*local.y<.035f*.035f) return true;
            // The wooden handles extend beyond the old rim circle. Query the actual
            // solid handwheel, never its filled bounds (which would include the hole).
            // Use LOD0 cached at setup, so grabbing does not depend on viewing distance.
            if(!handwheelArt || !handwheelArt.gameObject.activeInHierarchy || !handwheelRenderer || !handwheelRenderer.enabled || grabVertices==null) return false;
            var artPoint=handwheelArt.InverseTransformPoint(point);
            float toleranceSquared=HandleGrabTolerance*HandleGrabTolerance;
            if(grabMesh.bounds.SqrDistance(artPoint)>toleranceSquared) return false;
            for(int i=0;i+2<grabTriangles.Length;i+=3)
                if(SandboxMeshContact.DistanceSquared(artPoint,artPoint,grabVertices[grabTriangles[i]],grabVertices[grabTriangles[i+1]],grabVertices[grabTriangles[i+2]])<=toleranceSquared) return true;
            return false;
        }

        public static DownhillRotationWheel Create(Transform parent,Vector3 position,Material material)
        {
            var root=new GameObject("Build rotation wheel"); root.transform.SetParent(parent,false); root.transform.localPosition=position;
            var wheel=root.AddComponent<DownhillRotationWheel>();
            var rim=new GameObject("Steering rim"); rim.transform.SetParent(root.transform,false);
            rim.AddComponent<MeshFilter>(); rim.AddComponent<MeshRenderer>();
            wheel.ApplyEmblem();
            for(int i=0;i<24;i++)
            {
                var segment=new GameObject("Rim grip"); segment.transform.SetParent(root.transform,false);
                var rotation=Quaternion.Euler(0,i*15,0);
                segment.transform.localPosition=rotation*Vector3.forward*Radius; segment.transform.localRotation=rotation;
                var collider=segment.AddComponent<BoxCollider>(); collider.size=new Vector3(.045f,.02f,.02f); collider.isTrigger=true;
            }
            return wheel;
        }
        // The rim is drawn as the glowing ring emblem (formerly on the workbench tabletop): a flat
        // disc in the wheel's XZ plane with the SteeringEmblem material. Also run at Start so a wheel
        // saved with the old ring mesh is upgraded.
        public void ApplyEmblem()
        {
            var rim=transform.Find("Steering rim"); if(!rim) return;
            var material=Resources.Load<Material>("SteeringEmblem");
            if(!material) { Debug.LogError("Steering emblem material is missing.",this); return; }
            var disc=Resources.Load<Mesh>("SteeringEmblemDisc");
            if(!disc) { Debug.LogError("Steering emblem disc mesh is missing.",this); return; }
            rim.localRotation=Quaternion.identity; rim.localScale=Vector3.one;
            rim.GetComponent<MeshFilter>().sharedMesh=disc;
            rimRenderer=rim.GetComponent<MeshRenderer>();
            rimRenderer.sharedMaterial=material;
            ApplyRimAnimation(0);
            // The flat emblem alone does not read as something to take hold of: a solid handwheel
            // sits on the same grab circle, just above the emblem's plane.
            var handwheel=CarPartArtLibrary.AddHandwheel(transform);
            if(handwheel) {
                handwheel.localPosition=Vector3.up*HandwheelLift;
                handwheelArt=handwheel; handwheelRenderer=handwheel.GetComponent<Renderer>();
                var mesh=CarPartArtLibrary.Load().Handwheel[0];
                if(mesh!=grabMesh) {
                    grabMesh=mesh;
                    grabVertices=mesh && mesh.isReadable?mesh.vertices:null;
                    grabTriangles=grabVertices!=null?mesh.triangles:null;
                }
            }
        }
        public const float HandwheelLift=.012f;
        // The rim is the wheel. A layout that moved only the visible rim (Level 1's did, to sit it under the core)
        // leaves the grips and the grab test where the wheel was, away from what the player reaches for. So in
        // play the wheel goes to where its rim was put, and the rim sits at the wheel's origin. Done on Awake,
        // before the presentation measures where the wheel is.
        void Awake() { if(Application.isPlaying) SeatOnRim(); }
        public void SeatOnRim()
        {
            var rim=transform.Find("Steering rim"); if(!rim || rim.localPosition.sqrMagnitude<1e-10f) return;
            var at=rim.position; transform.position=at; rim.localPosition=Vector3.zero;
        }
        void OnEnable() => ResetRimAnimation();
        void OnDisable() => ResetRimAnimation();
        void ResetRimAnimation()
        {
            ripplePhase=sweepPhase=rippleFade=0;
            ApplyRimAnimation(0);
        }
        void LateUpdate()
        {
            if(!rimRenderer) return;
            // Grab ownership persists through temporary tracking loss. Never animate the
            // transform or colliders: the visible cue must not move the interaction target.
            if(!Controller || !Controller.IsBuilding || Controller.Rotating)
            {
                rippleFade=0;
                ApplyRimAnimation(0);
                return;
            }
            float dt=Time.unscaledDeltaTime;
            rippleFade=RippleReleaseDuration<=0?1:Mathf.MoveTowards(rippleFade,1,dt/RippleReleaseDuration);
            ripplePhase=Mathf.Repeat(ripplePhase+dt*Mathf.Max(0,IdleRippleFrequency)*2*Mathf.PI,2*Mathf.PI);
            sweepPhase=Mathf.Repeat(sweepPhase+dt*.035f,1);
            ApplyRimAnimation(Mathf.Max(0,IdleRippleAmplitude)*Mathf.SmoothStep(0,1,rippleFade));
        }
        void ApplyRimAnimation(float amplitude)
        {
            if(!rimRenderer) return;
            rimProperties??=new MaterialPropertyBlock();
            rimRenderer.GetPropertyBlock(rimProperties);
            rimProperties.SetFloat(RippleAmplitudeId,amplitude);
            rimProperties.SetFloat(RipplePhaseId,ripplePhase);
            rimProperties.SetFloat(SweepPhaseId,sweepPhase);
            rimRenderer.SetPropertyBlock(rimProperties);
        }
        void Start()
        {
            ApplyEmblem();
            // Wheels saved before the emblem had three solid spokes.
            for(int i=transform.childCount-1;i>=0;i--) if(transform.GetChild(i).name=="Steering spoke") Destroy(transform.GetChild(i).gameObject);
        }
        public void ReleaseGeneratedMeshOwnership() => generatedMesh=null;
        void OnDestroy()
        {
            if(generatedMesh) { if(Application.isPlaying) Destroy(generatedMesh); else DestroyImmediate(generatedMesh); }
        }
    }
}
