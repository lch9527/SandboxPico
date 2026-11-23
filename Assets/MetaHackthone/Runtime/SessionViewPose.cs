using UnityEngine;
namespace PhysicalAssembly
{
    // Tracking-space reference for one app session, shared across scene reloads.
    // Looking sideways at QUIT must not establish a new room heading.
    public static class SessionViewPose
    {
        public static bool IsSet { get; private set; }
        public static Pose Pose { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset(){IsSet=false;Pose=UnityEngine.Pose.identity;}
        public static Pose CaptureOnce(Vector3 position,Quaternion rotation)
        {
            if(!IsSet){var forward=Vector3.ProjectOnPlane(rotation*Vector3.forward,Vector3.up);Pose=new Pose(position,Quaternion.LookRotation(forward.sqrMagnitude>.001f?forward:Vector3.forward));IsSet=true;}
            return Pose;
        }
    }
}
