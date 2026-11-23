using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicalAssembly
{
    // A car the building core makes in the first-entry intro. Its parts are real game parts in the intro's own
    // world, so it fails on the track exactly as a player's car would. The player's kit is never involved.
    public sealed class SandboxIntroCar
    {
        public AssemblyPart Core;
        public readonly List<AssemblyPart> Parts=new(); // the core first, then in the order the core attracts them
        public IEnumerable<AssemblyPart> Attracted => Parts.Skip(1);
        public IEnumerable<AssemblyBody> Bodies => Parts.SelectMany(p=>p.Bodies);
        public int Count(PartKind kind) => Parts.Count(p=>p.Kind==kind);
    }

    // A visual-only copy of a part: its meshes and materials, with no bodies, colliders or behaviours.
    public sealed class SandboxIntroGhost
    {
        public AssemblyPart Part;
        public Transform Root;
        public Renderer[] Renderers;
        public Vector3 Position; public Quaternion Rotation; // rest pose, local to the frame it was copied under
        // Only the meshes the part was made with: outline silhouettes and other helpers added later are not its looks.
        public static Renderer[] Visuals(AssemblyPart part) => part.VisualRenderers
            .Where(r=>r is MeshRenderer && !r.name.Contains("Grid") && r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh).ToArray();
        // The frame must be unscaled when the copy is made; move and scale the frame afterwards.
        public static SandboxIntroGhost Copy(AssemblyPart part,Transform frame)
        {
            var source=part.MainBody.transform;
            var root=new GameObject("Intro "+part.Kind).transform; root.SetParent(frame,false); root.SetPositionAndRotation(source.position,source.rotation);
            var renderers=new List<Renderer>();
            foreach(var renderer in Visuals(part)) {
                var copy=new GameObject(renderer.name).transform; copy.SetParent(root,false);
                copy.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation); copy.localScale=renderer.transform.lossyScale;
                copy.gameObject.AddComponent<MeshFilter>().sharedMesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                var visual=copy.gameObject.AddComponent<MeshRenderer>(); visual.sharedMaterials=renderer.sharedMaterials;
                renderers.Add(visual);
            }
            return new SandboxIntroGhost {Part=part,Root=root,Renderers=renderers.ToArray(),Position=root.localPosition,Rotation=root.localRotation};
        }
    }

    public static class SandboxIntroCars
    {
        public const int Count=2;
        // Building units from a block face to the centre of a wheel mounted on it (half the wheel's width).
        const float HalfWheel=.23f/.38f*.5f;
        // Which side of car two carries its two wheels: 1 is the viewer's side, -1 the far side. Facing the viewer
        // the wheels are plain to see, and the car drifts away from them and drops off the far edge of the track,
        // out of sight. On the far side the car hides its own wheels but falls off the near edge in full view.
        public const float WheelSide=1;
        // One row per attracted part: its kind, the part it joins (index into the car, 0 is the core), its centre and
        // Euler rotation in the car's frame, and the face of the target it sits on. Centres are in building units;
        // +Z is the car's front and side (+1 or -1) mirrors the car so its odd half faces the viewer.
        static (PartKind kind,int target,Vector3 centre,Vector3 euler,Vector3 face)[] Recipe(int car,float side) => car==0
            // Nonsense: a mast, a bar sticking out sideways from its top, a wheel lying flat on the bar and a wheel for a nose.
            ? new[] {
                (PartKind.DownhillShortBlock,0,new Vector3(0,1.5f,0),new Vector3(-90,0,0),Vector3.up),
                (PartKind.DownhillLongBlock,1,new Vector3(side,3,0),new Vector3(0,90,0),Vector3.up),
                (PartKind.DownhillSmallWheel,2,new Vector3(side*2,3.5f+HalfWheel,0),new Vector3(0,0,90),Vector3.up),
                (PartKind.DownhillSmallWheel,0,new Vector3(0,0,.5f+HalfWheel),new Vector3(0,-90,0),Vector3.forward) }
            // Nearly a car: the level's own starter car (the core, the long block ahead of it, two wheels), except
            // that both wheels are on the same side of the block, front and back. It rolls on that side, drags on
            // the other and so drifts toward the side without wheels until it runs off the track.
            : new[] {
                (PartKind.DownhillLongBlock,0,new Vector3(0,0,2),Vector3.zero,Vector3.forward),
                (PartKind.DownhillSmallWheel,1,new Vector3(side*WheelSide*(.5f+HalfWheel),0,3),new Vector3(0,side*WheelSide>0?0:180,0),Vector3.right*(side*WheelSide)),
                (PartKind.DownhillSmallWheel,1,new Vector3(side*WheelSide*(.5f+HalfWheel),0,1),new Vector3(0,side*WheelSide>0?0:180,0),Vector3.right*(side*WheelSide)) };

        // Builds the car with its core at pose, held still and weightless. Throws if the game's own rules refuse a joint.
        public static SandboxIntroCar Build(AssemblyWorld world,float scale,Pose pose,int index,float side)
        {
            var car=new SandboxIntroCar(); float unit=BuildingGeometry.Unit*scale;
            AssemblyPart Make(PartKind kind,Vector3 centre,Quaternion rotation,bool core)
            {
                var part=DownhillParts.Create(world,kind,new Pose(pose.position+pose.rotation*(centre*unit),pose.rotation*rotation),scale,null,DownhillParts.DesignerGridVersion,core);
                part.transform.SetParent(world.transform,true);
                foreach(var body in part.Bodies) { body.Rigidbody.isKinematic=true; body.Rigidbody.useGravity=false; }
                car.Parts.Add(part); return part;
            }
            car.Core=Make(PartKind.DownhillCore,Vector3.zero,Quaternion.identity,true);
            foreach(var piece in Recipe(index,side)) {
                var part=Make(piece.kind,piece.centre,Quaternion.Euler(piece.euler),false);
                PresetAttachment.Connect(world,part,car.Parts[piece.target],pose.rotation,piece.face,"Intro car parts overlap: ","Intro car connection failed.");
            }
            // As in a test run: nothing inside one welded vehicle collides, including blocks that are not neighbours.
            var bodies=car.Bodies.ToArray();
            for(int i=0;i<bodies.Length;i++) for(int j=i+1;j<bodies.Length;j++)
                foreach(var a in bodies[i].Colliders) foreach(var b in bodies[j].Colliders) world.Collisions.Acquire(a,b,"introCar"+index);
            return car;
        }
        public static void Show(AssemblyPart part,bool visible) { foreach(var renderer in SandboxIntroGhost.Visuals(part)) renderer.enabled=visible; }
        // Moves the whole car rigidly so its core takes the given pose.
        public static void Move(SandboxIntroCar car,Pose target)
        {
            var core=car.Core.MainBody.Rigidbody; var origin=core.position; var delta=target.rotation*Quaternion.Inverse(core.rotation);
            foreach(var body in car.Bodies) {
                var rb=body.Rigidbody; var position=target.position+delta*(rb.position-origin); var rotation=delta*rb.rotation;
                rb.position=position; rb.rotation=rotation; body.transform.SetPositionAndRotation(position,rotation);
            }
            Physics.SyncTransforms();
        }
        // Rests the car on the level's start with the same clearance a test run uses, without the game's wheel
        // levelling: these cars are meant to sit as badly as they were built.
        public static Pose PlaceAtStart(DownhillController level,SandboxIntroCar car)
        {
            var start=level.FactoryLevel.Start; var up=start.rotation*Vector3.up;
            Move(car,new Pose(start.position,start.rotation));
            float lowest=float.PositiveInfinity;
            foreach(var body in car.Bodies) foreach(var collider in body.Colliders) foreach(var point in level.ColliderPoints(collider))
                lowest=Mathf.Min(lowest,Vector3.Dot(point-start.position,up));
            var pose=new Pose(start.position+up*(.003f*level.GarageWorldScale-lowest),start.rotation);
            Move(car,pose); return pose;
        }
        // Hands the parts to physics the way a test run does when its countdown ends.
        public static void Release(IEnumerable<AssemblyPart> parts)
        {
            foreach(var body in parts.SelectMany(p=>p.Bodies)) {
                var rb=body.Rigidbody; rb.isKinematic=false; rb.useGravity=true; rb.linearVelocity=Vector3.zero; rb.angularVelocity=Vector3.zero;
                rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic; rb.ResetInertiaTensor();
            }
        }
        // Holds the parts where they are, as the game holds a car that has left the level.
        public static void Freeze(IEnumerable<AssemblyPart> parts)
        {
            foreach(var body in parts.SelectMany(p=>p.Bodies)) {
                var rb=body.Rigidbody; if(!rb.isKinematic) { rb.linearVelocity=Vector3.zero; rb.angularVelocity=Vector3.zero; }
                rb.isKinematic=true; rb.useGravity=false;
            }
        }
        // The core lets go of everything joined to it. Returns the parts it dropped, which are left to physics as
        // they are: nothing is thrown, they only lose what the core was holding up.
        public static AssemblyPart[] DetachCore(AssemblyWorld world,SandboxIntroCar car)
        {
            string id=car.Core.InstanceId;
            foreach(var edge in world.Connections.Where(e=>e.a.partId==id || e.b.partId==id).ToArray()) world.RemoveConnection(edge.id);
            var dropped=car.Attracted.ToArray();
            foreach(var body in dropped.SelectMany(p=>p.Bodies)) if(!body.Rigidbody.isKinematic) body.Rigidbody.WakeUp();
            return dropped;
        }
    }
}
