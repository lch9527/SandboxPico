using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalAssembly
{
    public sealed class SandboxReturnEffect : MonoBehaviour
    {
        readonly List<(Renderer renderer,bool enabled)> originals=new();
        readonly List<(Collider collider,bool enabled)> colliders=new();
        readonly List<(Rigidbody body,bool kinematic,bool gravity)> bodies=new();
        readonly List<(Transform proxy,Vector3 position,Quaternion rotation,Vector3 scale)> proxies=new();
        readonly List<Material> materials=new();
        SandboxFragments fragments;
        readonly List<(Vector3 position,Quaternion rotation,Vector3 scale)> destinations=new();
        Vector3[] targetPoints;
        readonly List<Mesh> destinationMeshes=new();
        ParticleSystem.Particle[] points;
        Vector3[] sourcePoints;
        Transform frame;
        Vector3 source,target;
        Quaternion deltaRotation;
        float age,decompose,flow,recompose;
        bool restored;
        // Set before Initialize. Vanish: dissolve in place and let the motes fade instead of re-forming at a destination.
        public bool Vanish;
        public float MoteScale=1, VanishRise=.12f; // mote size multiplier; metres the motes drift up while fading
        // The game's gold unless set before Initialize. Tinted also recolours the dissolving surface and its burning edge.
        public Color MoteColor=new(1,.72f,.03f), BodyColor, EdgeColor;
        public bool Tinted;

        public void Initialize(AssemblyPart part,Transform workbench,Vector3 destination,Quaternion rotation,float dissolve,float travel,float compose,int budget)
            => Initialize(part,null,workbench,destination,rotation,dissolve,travel,compose,budget);
        public void Initialize(AssemblyPart part,AssemblyPart destinationPart,Transform workbench,Vector3 destination,Quaternion rotation,float dissolve,float travel,float compose,int budget,bool freezeSource=true)
        {
            if(freezeSource) foreach(var body in part.Bodies) {
                var rb=body.Rigidbody; bodies.Add((rb,rb.isKinematic,rb.useGravity));
                if(!rb.isKinematic) { rb.linearVelocity=Vector3.zero; rb.angularVelocity=Vector3.zero; }
                rb.isKinematic=true; rb.useGravity=false;
                foreach(var c in body.Colliders) { colliders.Add((c,c.enabled)); c.enabled=false; }
            }
            Initialize(part.VisualRenderers,destinationPart?destinationPart.VisualRenderers:null,part.MainBody.Rigidbody.position,part.MainBody.Rigidbody.rotation,
                workbench,destination,rotation,dissolve,travel,compose,budget);
        }
        // Visual-only entry point: no part, bodies or colliders are required.
        public void Initialize(Renderer[] visuals,Renderer[] destinationVisuals,Vector3 sourcePosition,Quaternion sourceRotation,Transform workbench,Vector3 destination,Quaternion rotation,float dissolve,float travel,float compose,int budget)
        {
            frame=workbench; target=destination; source=frame.InverseTransformPoint(sourcePosition);
            deltaRotation=rotation*Quaternion.Inverse(Quaternion.Inverse(frame.rotation)*sourceRotation);
            decompose=Mathf.Max(.01f,dissolve); flow=Mathf.Max(.01f,travel); recompose=Mathf.Max(.01f,compose);
            var samples=new List<Vector3>(); var ends=new List<Vector3>();
            int visualIndex=0;
            foreach(var renderer in visuals) {
                var destinationRenderer=destinationVisuals!=null && visualIndex<destinationVisuals.Length?destinationVisuals[visualIndex]:null;
                visualIndex++;
                originals.Add((renderer,renderer.enabled)); if(!renderer.enabled) continue;
                var filter=renderer.GetComponent<MeshFilter>(); if(!filter || !filter.sharedMesh) continue;
                var go=new GameObject("Decomposing surface"); go.transform.SetParent(frame,false);
                go.transform.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation);
                go.transform.localScale=new Vector3(renderer.transform.lossyScale.x/frame.lossyScale.x,renderer.transform.lossyScale.y/frame.lossyScale.y,renderer.transform.lossyScale.z/frame.lossyScale.z);
                go.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
                var visual=go.AddComponent<MeshRenderer>(); visual.shadowCastingMode=ShadowCastingMode.Off;
                var mat=new Material(Resources.Load<Shader>("SandboxDissolve")); materials.Add(mat);
                if(Tinted) { mat.SetColor("_Color",BodyColor); mat.SetColor("_Edge",EdgeColor); }
                var mats=new Material[filter.sharedMesh.subMeshCount]; for(int i=0;i<mats.Length;i++) mats[i]=mat; visual.sharedMaterials=mats;
                proxies.Add((go.transform,go.transform.localPosition,go.transform.localRotation,go.transform.localScale));
                Vector3 endPosition=destinationRenderer?frame.InverseTransformPoint(destinationRenderer.transform.position):target+deltaRotation*(go.transform.localPosition-source);
                Quaternion endRotation=destinationRenderer?Quaternion.Inverse(frame.rotation)*destinationRenderer.transform.rotation:deltaRotation*go.transform.localRotation;
                Vector3 endScale=destinationRenderer?new Vector3(destinationRenderer.transform.lossyScale.x/frame.lossyScale.x,destinationRenderer.transform.lossyScale.y/frame.lossyScale.y,destinationRenderer.transform.lossyScale.z/frame.lossyScale.z):go.transform.localScale;
                destinations.Add((endPosition,endRotation,endScale));
                var destinationFilter=destinationRenderer?destinationRenderer.GetComponent<MeshFilter>():null;
                var endMesh=destinationFilter?destinationFilter.sharedMesh:filter.sharedMesh;
                destinationMeshes.Add(endMesh);
                var endVertices=endMesh.isReadable?endMesh.vertices:null;
                int vertexIndex=0;
                void AddPoint(Vector3 vertex) {
                    samples.Add(frame.InverseTransformPoint(renderer.transform.TransformPoint(vertex)));
                    var endVertex=endVertices!=null && vertexIndex<endVertices.Length?endVertices[vertexIndex]:endMesh.bounds.center;
                    ends.Add(endPosition+endRotation*Vector3.Scale(endVertex,endScale));
                    vertexIndex++;
                }
                if(filter.sharedMesh.isReadable) foreach(var vertex in filter.sharedMesh.vertices) AddPoint(vertex);
                else AddPoint(filter.sharedMesh.bounds.center);
                renderer.enabled=false;
            }
            fragments=new SandboxFragments(transform,budget);
            points=fragments.Points; sourcePoints=new Vector3[points.Length]; targetPoints=new Vector3[points.Length];
            for(int i=0;i<points.Length;i++) {
                int index=samples.Count>0?(i*7919)%samples.Count:0;
                sourcePoints[i]=samples.Count>0?samples[index]:source;
                targetPoints[i]=Vanish?sourcePoints[i]+Vector3.up*VanishRise:ends.Count>0?ends[index]:target;
            }
            Advance(0);
        }
        public bool Advance(float dt)
        {
            age+=dt; float total=decompose+flow+recompose;
            float t=Mathf.Clamp01((age-decompose)/flow);
            float dissolve=age<decompose?age/decompose:Vanish||age<decompose+flow?1:1-(age-decompose-flow)/recompose;
            foreach(var material in materials) material.SetFloat("_Dissolve",dissolve);
            bool atTarget=!Vanish && age>=decompose+flow;
            for(int i=0;i<proxies.Count;i++) {
                var proxy=proxies[i]; if(!proxy.proxy) continue;
                proxy.proxy.localPosition=atTarget?destinations[i].position:proxy.position;
                proxy.proxy.localRotation=atTarget?destinations[i].rotation:proxy.rotation;
                proxy.proxy.localScale=atTarget?destinations[i].scale:proxy.scale;
                if(atTarget) proxy.proxy.GetComponent<MeshFilter>().sharedMesh=destinationMeshes[i];
                proxy.proxy.gameObject.SetActive(age<decompose || atTarget);
            }
            float visibility=age<decompose?age/decompose:Vanish?Mathf.Clamp01((total-age)/(flow+recompose)):atTarget?Mathf.Clamp01((total-age)/recompose):1;
            for(int i=0;i<points.Length;i++) {
                var start=sourcePoints[i]; var end=targetPoints[i];
                // Staggered departures stretch the swarm into a comet; every mote still lands exactly at t=1.
                float lag=Mathf.Repeat(i*.61803f,1);
                float u=Mathf.SmoothStep(0,1,Mathf.Clamp01(t*1.3f-lag*.3f)), arc=Mathf.Sin(u*Mathf.PI), swirl=i*2.399963f+u*5f;
                var position=Vector3.Lerp(start,end,u)+Vector3.up*(arc*(.06f+.05f*lag))+new Vector3(Mathf.Cos(swirl),0,Mathf.Sin(swirl))*(arc*.012f);
                points[i].position=frame.TransformPoint(position);
                var color=Color.Lerp(MoteColor,Color.white,arc*.4f*lag); color.a=visibility*(.7f+.3f*Mathf.Sin(age*22+i*1.7f));
                points[i].startColor=color;
                points[i].startSize=(.003f+(i%3)*.001f)*2f*MoteScale; points[i].startLifetime=2; points[i].remainingLifetime=2;
            }
            fragments.Draw();
            return age>=total;
        }
        public void Restore()
        {
            if(restored) return; restored=true;
            fragments?.Release();
            foreach(var entry in originals) if(entry.renderer) entry.renderer.enabled=entry.enabled;
            foreach(var entry in colliders) if(entry.collider) entry.collider.enabled=entry.enabled;
            foreach(var entry in bodies) if(entry.body) { entry.body.isKinematic=entry.kinematic; entry.body.useGravity=entry.gravity; }
        }
        void OnDestroy()
        {
            Restore(); foreach(var proxy in proxies) if(proxy.proxy) Destroy(proxy.proxy.gameObject);
            foreach(var material in materials) if(material) Destroy(material);
        }
    }
}

