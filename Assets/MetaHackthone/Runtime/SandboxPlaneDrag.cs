using UnityEngine;

namespace PhysicalAssembly
{
    // One stable drag plane and grab offset per pointer, shared by mouse and XR.
    public sealed class SandboxPlaneDrag
    {
        Plane plane;
        Pose last;
        Vector3 desktopOffset;
        public void Begin(Vector3 point,Vector3 up,Quaternion rotation) { plane=new Plane(up,point); last=new Pose(point,rotation); desktopOffset=Vector3.zero; }
        public Pose Sample(Ray ray) { if(plane.Raycast(ray,out float distance) && distance>0 && distance<20) last.position=ray.GetPoint(distance); return last; }
        public Pose DesktopSample(Ray ray, Vector3 cameraForward, float depth, Quaternion turn)
        {
            if(plane.Raycast(ray,out float distance) && distance>0 && distance<20) last.position=ray.GetPoint(distance)+desktopOffset;
            if(depth!=0) {
                last.position+=cameraForward*depth; plane=new Plane(plane.normal,last.position);
                if(plane.Raycast(ray,out distance)) desktopOffset=last.position-ray.GetPoint(distance);
            }
            last.rotation=turn*last.rotation;
            return last;
        }
    }
}
