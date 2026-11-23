using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAssembly
{
    // Input-only geometry query: never changes the simulation's colliders.
    public sealed class SandboxMeshContact
    {
        readonly Dictionary<Mesh,(Vector3[] vertices,int[] indices)> cache=new();
        public AssemblyPart Find(IEnumerable<AssemblyPart> parts,Vector3 thumb,Vector3 index,float tolerance)
        {
            AssemblyPart best=null;float score=float.PositiveInfinity;
            tolerance=Mathf.Max(0,tolerance);
            var mid=(thumb+index)*.5f;
            var query=new Bounds(thumb,Vector3.zero);query.Encapsulate(index);query.Expand(tolerance*2);
            foreach(var part in parts) {
                if(!part || !part.gameObject.activeInHierarchy || part.VisualMeshes==null)continue;
                foreach(var filter in part.VisualMeshes) {
                    if(!filter || !filter.gameObject.activeInHierarchy)continue;
                    var renderer=filter.GetComponent<Renderer>();var mesh=filter.sharedMesh;
                    if(!renderer || !renderer.enabled || !mesh || mesh.name=="GridMarkers" || filter.GetComponentInParent<AssemblySocket>() || !renderer.bounds.Intersects(query))continue;
                    if(!cache.TryGetValue(mesh,out var data)) {
                        if(!mesh.isReadable)continue;
                        data=(mesh.vertices,mesh.triangles);cache.Add(mesh,data);
                    }
                    var matrix=filter.transform.localToWorldMatrix;
                    float candidate=float.PositiveInfinity;
                    for(int i=0;i+2<data.indices.Length;i+=3) {
                        var a=matrix.MultiplyPoint3x4(data.vertices[data.indices[i]]);var b=matrix.MultiplyPoint3x4(data.vertices[data.indices[i+1]]);var c=matrix.MultiplyPoint3x4(data.vertices[data.indices[i+2]]);
                        if(DistanceSquared(thumb,index,a,b,c)>tolerance*tolerance)continue;
                        candidate=Mathf.Min(candidate,DistanceSquared(mid,mid,a,b,c));
                    }
                    // A closing pinch can lie wholly inside a solid part, with
                    // neither fingertip touching its surface. Test the actual
                    // volume, not a filled renderer AABB or convex physics hull.
                    if(float.IsPositiveInfinity(candidate)) {
                        var localMid=filter.transform.InverseTransformPoint(mid);
                        if(mesh.bounds.Contains(localMid) && Contains(data.vertices,data.indices,localMid))candidate=0;
                    }
                    if(!float.IsPositiveInfinity(candidate) && (candidate<score || candidate==score && (!best || string.CompareOrdinal(part.InstanceId,best.InstanceId)<0))){score=candidate;best=part;}
                }
            }
            return best;
        }
        public void Clear()=>cache.Clear();
        static bool Contains(Vector3[] vertices,int[] indices,Vector3 point)
        {
            // Closed triangle surfaces enclose +/-4*pi steradians. Holes and
            // concave empty regions sum to zero. Local space keeps this test
            // independent of scale/rotation; distance tolerance stays in meters.
            double angle=0;
            for(int i=0;i+2<indices.Length;i+=3) {
                var a=vertices[indices[i]]-point;var b=vertices[indices[i+1]]-point;var c=vertices[indices[i+2]]-point;
                double la=a.magnitude,lb=b.magnitude,lc=c.magnitude;
                double determinant=Vector3.Dot(a,Vector3.Cross(b,c));
                if(determinant==0)continue;
                double denominator=la*lb*lc+Vector3.Dot(a,b)*lc+Vector3.Dot(b,c)*la+Vector3.Dot(c,a)*lb;
                angle+=2*System.Math.Atan2(determinant,denominator);
            }
            return System.Math.Abs(angle)>2*System.Math.PI;
        }
        public static float DistanceSquared(Vector3 p,Vector3 q,Vector3 a,Vector3 b,Vector3 c)
        {
            var n=Vector3.Cross(b-a,c-a);float nn=n.sqrMagnitude;
            if(nn>1e-16f){float d=Vector3.Dot(n,q-p);if(Mathf.Abs(d)>1e-12f){float t=Vector3.Dot(n,a-p)/d;if(t>=0 && t<=1 && Inside(p+(q-p)*t,a,b,c,n))return 0;}}
            return Mathf.Min(Mathf.Min(PointTriangle(p,a,b,c,n),PointTriangle(q,a,b,c,n)),Mathf.Min(Segments(p,q,a,b),Mathf.Min(Segments(p,q,b,c),Segments(p,q,c,a))));
        }
        static bool Inside(Vector3 p,Vector3 a,Vector3 b,Vector3 c,Vector3 n)=>Vector3.Dot(Vector3.Cross(b-a,p-a),n)>=0 && Vector3.Dot(Vector3.Cross(c-b,p-b),n)>=0 && Vector3.Dot(Vector3.Cross(a-c,p-c),n)>=0;
        static float PointTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c,Vector3 n)
        {
            float nn=n.sqrMagnitude;if(nn>1e-16f){float h=Vector3.Dot(p-a,n);var projected=p-n*(h/nn);if(Inside(projected,a,b,c,n))return h*h/nn;}
            return Mathf.Min(Segments(p,p,a,b),Mathf.Min(Segments(p,p,b,c),Segments(p,p,c,a)));
        }
        static float Segments(Vector3 p,Vector3 q,Vector3 a,Vector3 b)
        {
            var u=q-p;var v=b-a;var w=p-a;float uu=Vector3.Dot(u,u),vv=Vector3.Dot(v,v),uv=Vector3.Dot(u,v),uw=Vector3.Dot(u,w),vw=Vector3.Dot(v,w);
            float s=0,t=0;
            if(uu<1e-16f)t=vv>1e-16f?Mathf.Clamp01(vw/vv):0;
            else if(vv<1e-16f)s=Mathf.Clamp01(-uw/uu);
            else {float denom=uu*vv-uv*uv;s=denom>1e-16f?Mathf.Clamp01((uv*vw-uw*vv)/denom):0;t=(uv*s+vw)/vv;if(t<0){t=0;s=Mathf.Clamp01(-uw/uu);}else if(t>1){t=1;s=Mathf.Clamp01((uv-uw)/uu);}}
            return (w+s*u-t*v).sqrMagnitude;
        }
    }
}
