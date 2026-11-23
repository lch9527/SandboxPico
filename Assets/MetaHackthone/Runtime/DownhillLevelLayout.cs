using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicalAssembly
{
    public sealed partial class DownhillController
    {
        public SandboxLevelLayout ActiveLayout => Course?Course.GetComponent<SandboxLevelLayout>():null;
        SandboxLevelLayout FindLayout(SandboxCourseId id) => Workshop.Layout.LevelLayouts?.FirstOrDefault(l=>l && l.CourseId==id);

        void ActivateLayout(SandboxLevelLayout binding,bool preserveConstruction)
        {
            if(!binding || ActiveLayout==binding) return;
            if(!binding.Validate(out var error)) throw new InvalidOperationException(error);
            var previous=ActiveLayout;
            bool polish=Course.SandboxPolish;
            float ratio=binding.BuildingScale/(previous?previous.BuildingScale:1f);
            var poses=preserveConstruction?Capture(kit.Where(p=>p).SelectMany(p=>p.Bodies)).ToArray():Array.Empty<BodyPose>();
            
            if(previous) previous.gameObject.SetActive(false);
            binding.gameObject.SetActive(true); binding.Bind(Workshop.Layout);
            Course=binding.Course; Course.SandboxPolish|=polish; Presentation=binding.Presentation;
            Course.EnsureControls(Settings); Course.ConfigurePhysics();
            Presentation.Initialize(Course,Settings,Workshop.Layout); Presentation.Configure();
            if(preserveConstruction) {
                if(Mathf.Abs(ratio-1)>.0001f) ResizeConstruction(ratio,ref poses);
                Restore(poses);
            }
            RotationWheel.Controller=this; buttonLabel=Course.ModeButton.GetComponentInChildren<TextMesh>();
            Workshop.RebindLevelPresentation();
            
            volume=null; outsideTimes.Clear();
            Workshop.World.SocketDistance=Settings.SnapRadius*binding.BuildingScale;
            Workshop.World.AttachDistance=Mathf.Max(.10f,Settings.SnapRadius+.01f)*binding.BuildingScale;
            Workshop.Layout.HidePreviews();
        }

        // Recreate physical geometry at its final dimensions, never scale Rigidbody parents.
        // Logical identities, connections, loose/locked parts and reset poses survive the swap.
        void ResizeConstruction(float ratio,ref BodyPose[] poses)
        {
            var world=Workshop.World;
            var edges=world.Connections.Select(e=>JsonUtility.FromJson<ConnectionRecord>(JsonUtility.ToJson(e))).ToArray();
            var originals=kit.ToArray();
            var replacements=new Dictionary<AssemblyPart,AssemblyPart>();
            var rules=world.Rules;
            world.Rules=null;
            try {
                foreach(var old in originals) {
                    var replacement=DownhillParts.Create(world,old.Kind,new Pose(Vector3.zero,Quaternion.identity),old.GeometryScale*ratio,Settings,old.GridVersion,old.BuildingCore,old.DesignWheelRadius);
                    replacements.Add(old,replacement);
                    foreach(var body in replacement.Bodies) { body.Rigidbody.isKinematic=true; body.Rigidbody.useGravity=false; }
                }
                foreach(var edge in edges) world.RemoveConnection(edge.id);
                foreach(var old in originals) {
                    var replacement=replacements[old];
                    world.UnregisterPart(old); old.World=null; old.gameObject.SetActive(false);
                    world.UnregisterPart(replacement); replacement.InstanceId=old.InstanceId; world.RegisterPart(replacement);
                    if(old==Core) Core=replacement;
                    if(spawns.TryGetValue(old,out var reset)) {
                        spawns[replacement]=reset.Select(p=>new BodyPose {Body=replacement.FindBody(p.Body.Id),Position=p.Position,Rotation=p.Rotation}).ToArray();
                        spawns.Remove(old);
                    }
                    Destroy(old.gameObject);
                }
                poses=poses.Select(p=>new BodyPose { Body=replacements[p.Body.Part].FindBody(p.Body.Id),Position=p.Position,Rotation=p.Rotation }).ToArray();
                kit.Clear(); kit.AddRange(originals.Select(p=>replacements[p]));
                Restore(poses);
                foreach(var edge in edges) {
                    edge.anchorA*=ratio; edge.anchorB*=ratio;
                    if(!world.Connect(edge)) throw new InvalidOperationException("Could not restore construction connection "+edge.id);
                }
                Workshop.Session.ClearInput(); Workshop.Session.Select(Core);
            } finally { world.Rules=rules; }
        }
    }
}
