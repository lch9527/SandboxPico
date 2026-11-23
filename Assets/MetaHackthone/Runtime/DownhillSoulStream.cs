using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAssembly
{
    // While the available Start button is hovered in build mode, particles drift from the car to
    // the course start as if it were being pulled there. The car itself stays put.
    public sealed partial class DownhillController
    {
        public int SoulStreamPoints=128;
        public float SoulStreamSeconds=.9f, SoulStreamArc=.12f;
        public Color SoulStreamColor=new(.55f,.9f,1f,1f);
        public bool SoulStreamActive => soul!=null;
        SandboxFragments soul;
        readonly List<(Transform transform,Vector3 local)> soulSources=new();
        Pose soulPlacement;
        Quaternion soulDelta;
        float soulAge;

        void TickSoulStream(float delta)
        {
            bool wanted=IsBuilding && Presentation.ReadyButton.IsHovered && Presentation.ReadyButton.Interactable && CanSwitchMode && !Workshop.RepositionRequested && !SandboxFirstEntryIntro.Playing && Core && connected.Count>0 && !Rotating && !Workshop.World.HasHolds;
            if(!wanted) { StopSoulStream(); return; }
            if(soul==null && !BeginSoulStream()) return;
            soulAge+=delta;
            var origin=Core.MainBody.Rigidbody.position; float scale=TransferScale;
            var points=soul.Points;
            for(int i=0;i<points.Length;i++) {
                var source=soulSources[i%soulSources.Count];
                if(!source.transform) { StopSoulStream(); return; }
                var start=source.transform.TransformPoint(source.local);
                var end=soulPlacement.position+soulDelta*(start-origin)*scale;
                // Staggered phases give a continuous flow; u*u starts slowly and accelerates toward the start point.
                float u=Mathf.Repeat(soulAge/Mathf.Max(.05f,SoulStreamSeconds)+i*.61803f,1), eased=u*u;
                var position=Vector3.Lerp(start,end,eased)+Vector3.up*(Mathf.Sin(eased*Mathf.PI)*SoulStreamArc*Vector3.Distance(start,end));
                var color=SoulStreamColor; color.a*=Mathf.Sin(u*Mathf.PI);
                points[i].position=position; points[i].startColor=color;
                points[i].startSize=(.003f+(i%3)*.001f)*2f; points[i].startLifetime=2; points[i].remainingLifetime=2;
            }
            soul.Draw();
        }
        bool BeginSoulStream()
        {
            var candidates=new List<(Transform,Vector3)>(); var vertices=new List<Vector3>();
            foreach(var part in connected) foreach(var renderer in part.VisualRenderers) {
                if(!renderer || !renderer.enabled) continue;
                var filter=renderer.GetComponent<MeshFilter>(); if(!filter || !filter.sharedMesh) continue;
                if(filter.sharedMesh.isReadable) { filter.sharedMesh.GetVertices(vertices); foreach(var vertex in vertices) candidates.Add((renderer.transform,vertex)); }
                else candidates.Add((renderer.transform,filter.sharedMesh.bounds.center));
            }
            if(candidates.Count==0) return false;
            var placement=SandboxPlacement(out soulDelta);
            placement.position+=Course.SandboxFrame.parent.TransformVector(Presentation.TestPosition-Presentation.BuildPosition);
            soulPlacement=placement;
            soul=new SandboxFragments(transform,SoulStreamPoints);
            if(soul.Points.Length==0) { StopSoulStream(); return false; }
            soulSources.Clear();
            for(int i=0;i<soul.Points.Length;i++) soulSources.Add(candidates[(i*7919)%candidates.Count]);
            soulAge=0; return true;
        }
        void StopSoulStream()
        {
            if(soul==null) return;
            soul.Release(); soul=null; soulSources.Clear();
        }
    }
}
