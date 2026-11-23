using System;
using System.Linq;
using UnityEngine;

namespace PhysicalAssembly
{
    public sealed partial class DownhillController
    {
        public static string DesignerInstructions => DesignModeSession.Enabled?"DRAG & SNAP\nPinch nearby, or point + pinch to drag.\nGreen connects. Red is blocked.\nUse the 90 degree buttons to rotate.":"DRAG & SNAP\nPinch nearby, or point + pinch to drag.\nGreen connects. Red is blocked.\nSTART begins the countdown.";
        public static Vector3 DesignerOffset(int index) => index switch {
            1=>new Vector3(-.25f,0,-.13f), 2=>new Vector3(-.25f,0,.13f),
            3=>new Vector3(.25f,.04f,-.13f), 4=>new Vector3(.25f,.04f,.13f),
            5=>new Vector3(-.38f,0,0), 6=>new Vector3(.38f,0,0),
            7=>new Vector3(-.14f,0,-.28f), 8=>new Vector3(.14f,0,-.28f), _=>Vector3.zero };
        void SpawnDesignerKit()
        {
            float buildingScale=ActiveLayout?ActiveLayout.BuildingScale:1f;
            Workshop.World.SocketDistance=Settings.SnapRadius*buildingScale;
            Workshop.World.AttachDistance=Mathf.Max(.10f,Settings.SnapRadius+.01f)*buildingScale;
            var markers=Workshop.Layout.Spawns;
            if(FactoryLevel && !FactoryLevel.UseStarterPalette) {
                if(markers==null || markers.Count(m=>m && m.Kind==PartKind.DownhillCore)!=1) throw new InvalidOperationException("Factory parts require exactly one Core marker.");
                foreach(var marker in markers.Where(m=>m).OrderBy(m=>m.Kind==PartKind.DownhillCore?0:1)) {
                    var p=DownhillParts.Create(Workshop.World,marker.Kind,marker.Pose,marker.SizeMultiplier,Settings,DownhillParts.DesignerGridVersion,marker.Kind==PartKind.DownhillCore);
                    kit.Add(p); spawns[p]=Capture(p.Bodies).ToArray(); if(p.BuildingCore) Core=p;
                }
                RefreshDesignerKitAvailability(); TopologyChanged(); RebuildGarageCourse(); Workshop.Session.Select(null); UpdateControls(); return;
            }
            bool authored=markers!=null && markers.Length==9 && markers[0] && markers[0].Kind==PartKind.DownhillCore;
            float scale=markers!=null && markers.Length>0 && markers[0]?markers[0].SizeMultiplier:.4f;
            for(int i=0;i<DownhillParts.DesignerKit.Length;i++) {
                var pose=new Pose(Presentation.AssemblyAnchor.TransformPoint(DesignerOffset(i)),Presentation.AssemblyAnchor.rotation);
                if(authored && markers[i]) pose=markers[i].Pose;
                // The authored drill slot overlaps the front wheel. Keep the loose
                // tool in its own workbench space, including while locked in level 1.
                if(Settings.SandboxDrillCourse && i==4) pose=new Pose(Presentation.AssemblyAnchor.TransformPoint(new Vector3(-.25f,.04f,.13f)),Presentation.AssemblyAnchor.rotation);
                var kind=Settings.SandboxDrillCourse && i==4?PartKind.DownhillDrill:DownhillParts.DesignerKit[i];
                if(kind==PartKind.DownhillLargeWheel) kind=PartKind.DownhillSmallWheel;
                var part=DownhillParts.Create(Workshop.World,kind,pose,scale,Settings,DownhillParts.DesignerGridVersion,i==0);
                kit.Add(part); spawns[part]=Capture(part.Bodies).ToArray(); if(i==0) Core=part;
            }
            EnsureCampaignParts();
            RefreshDesignerKitAvailability();
            TopologyChanged(); RebuildGarageCourse();
            Workshop.Session.Select(null); Workshop.Session.Notify(DesignerInstructions); UpdateControls();
            BuildStarterCar();
        }
        void EnsureCampaignParts()
        {
            if(FactoryLevel && !FactoryLevel.UseStarterPalette) return;
            int number=SandboxCourseCatalog.Number(CurrentCourse);
            void Ensure(PartKind kind,int count) {
                int existing=kit.Count(p=>p.Kind==kind);
                for(int i=existing;i<count;i++) {
                    float scale=Core.GeometryScale;
                    var pose=new Pose(Presentation.AssemblyAnchor.TransformPoint(new Vector3(.25f,.15f,(i-count*.5f)*.15f)),Presentation.AssemblyAnchor.rotation);
                    var part=DownhillParts.Create(Workshop.World,kind,pose,scale,Settings,DownhillParts.DesignerGridVersion);
                    kit.Add(part); spawns[part]=Capture(part.Bodies).ToArray();
                }
            }
            if(SandboxCourseCatalog.Reached(CurrentCourse,SandboxCourseCatalog.LargeWheelCourse)) Ensure(PartKind.DownhillLargeWheel,2);
            if(SandboxCourseCatalog.Reached(CurrentCourse,SandboxCourseCatalog.LongBlockCourse)) Ensure(PartKind.DownhillLongBlock,3);
        }
        bool DesignerPartAvailable(PartKind kind) => FactoryLevel && !FactoryLevel.UseStarterPalette || SandboxCourseCatalog.PartAvailable(CurrentCourse,kind);

        void RefreshDesignerKitAvailability()
        {
            if(!Settings.DesignerBuilder) return;
            // Retain authored slots and identities while keeping locked parts out of
            // physics, grabbing, and socket search until the second course starts.
            foreach(var part in kit)
            {
                bool available=DesignerPartAvailable(part.Kind);
                part.gameObject.SetActive(available);
                bool registered=Workshop.World.Parts.ContainsKey(part.InstanceId);
                if(available && !registered) Workshop.World.RegisterPart(part);
                else if(!available && registered) Workshop.World.UnregisterPart(part);
            }
        }
        void TranslateDesignerKit(Vector3 shift)
        {
            foreach(var p in kit) foreach(var b in p.Bodies) {
                var rb=b.Rigidbody;
                var position=(b.gameObject.activeInHierarchy?rb.position:b.transform.position)+shift;
                var rotation=b.gameObject.activeInHierarchy?rb.rotation:b.transform.rotation;
                b.transform.SetPositionAndRotation(position,rotation);rb.position=position;rb.rotation=rotation;
            }
            Physics.SyncTransforms();
        }
        // Moves the source (with everything attached to it) onto a free socket of the target that faces
        // the given workbench direction and connects it. far picks the socket farthest along the workbench's forward.
        bool Join(AssemblyPart source,AssemblyPart target,Vector3 direction,bool far)
        {
            var world=Workshop.World;
            var normal=Presentation.AssemblyAnchor.rotation*direction;
            var sockets=target.Sockets.Where(s=>!world.SocketOccupied(s) && Vector3.Dot(s.Normal,normal)>.99f);
            var socket=(far?sockets.OrderByDescending(s=>Presentation.AssemblyAnchor.InverseTransformPoint(s.WorldPosition).z):sockets.OrderBy(s=>Presentation.AssemblyAnchor.InverseTransformPoint(s.WorldPosition).z)).FirstOrDefault();
            if(!socket) return false;
            var sourceSocket=source.Sockets.Where(s=>!world.SocketOccupied(s)).OrderBy(s=>Vector3.Dot(s.Normal,normal)).FirstOrDefault();
            if(!sourceSocket) return false;
            var candidate=new AttachmentCandidate(sourceSocket.Body,socket.Body,sourceSocket.WorldPosition,socket.WorldPosition,sourceSocket,socket);
            var group=world.GetConnectedParts(source.InstanceId);
            if(!world.PlacementClear(candidate,group)) return false;
            foreach(var b in group.SelectMany(p=>p.Bodies)) {
                var rb=b.Rigidbody; rb.position=candidate.targetPoint+candidate.rotationDelta*(rb.position-candidate.sourcePoint);
                rb.rotation=candidate.rotationDelta*rb.rotation; b.transform.SetPositionAndRotation(rb.position,rb.rotation);
            }
            Physics.SyncTransforms();
            return world.Connect(new ConnectionRecord {id=Guid.NewGuid().ToString("N"),a=new BodyRef(source.InstanceId,sourceSocket.Body.Id),b=new BodyRef(target.InstanceId,socket.Body.Id),socketA=sourceSocket.Id,socketB=socket.Id,anchorA=sourceSocket.transform.localPosition,anchorB=socket.transform.localPosition});
        }
        // Level 1 of the campaign starts with a half-built car: the core, the long block and the front
        // pair of small wheels are already connected. The other two small wheels lie loose for the player.
        public bool StarterCarCourse => Settings.Campaign && !DesignModeSession.Enabled && Settings.DesignerBuilder && FactoryLevel && FactoryLevel.UseStarterPalette
            && CurrentCourse==SandboxCourseId.BeautifulCorner && kit.Count>=9 && kit[1].Kind==PartKind.DownhillLongBlock
            && kit.Skip(5).Take(4).All(p=>p.Kind==PartKind.DownhillSmallWheel);
        public bool StarterCarBuilt { get; private set; }
        // Only on an untouched kit: never rearranges something the player has already connected.
        bool BuildStarterCar()
        {
            if(!StarterCarCourse || !IsBuilding || !Core) { StarterCarBuilt=false; return false; }
            if(Workshop.World.Connections.Count>0) return false; // already built, or the player has started building
            bool built=Join(kit[1],Core,Vector3.forward,false) && Join(kit[5],kit[1],Vector3.right,true) && Join(kit[6],kit[1],Vector3.left,true);
            if(!built) {
                // Leave a clean loose kit instead of a partly joined one.
                foreach(var edge in Workshop.World.Connections.ToArray()) Workshop.World.RemoveConnection(edge.id);
                foreach(var part in kit) Restore(spawns[part]);
                Debug.LogWarning("Level 1 starter car could not be placed; the kit stays loose.");
            }
            StarterCarBuilt=built; TopologyChanged(); Workshop.World.Collisions.Reapply(); UpdateControls();
            return built;
        }
        public bool TryLoadDesignerExample()
        {
            if((!DesignModeSession.Enabled && !FactoryLevel) || !Settings.DesignerBuilder || !IsBuilding || !CanSwitchMode || Rotating || !TryResetBuild()) return false;
            bool level2=!FactoryLevel && DesignModeSession.SelectedLevel==DesignLevelId.WaterfallValley;
            bool creekPreset=!level2 && Settings.Campaign && GarageCourse && GarageCourse.GetComponent<CreekCampaignTheme>();
            bool success=StarterCarBuilt
                ? Join(kit[7],kit[1],Vector3.right,false) && Join(kit[8],kit[1],Vector3.left,false)
                : level2
                ? kit[4].Kind==PartKind.DownhillDrill
                    && Join(kit[2],Core,Vector3.forward,false)
                    && Join(kit[4],kit[2],Vector3.forward,false)
                    && Join(kit[5],kit[2],Vector3.right,true) && Join(kit[6],kit[2],Vector3.left,true)
                    && Join(kit[7],Core,Vector3.right,false) && Join(kit[8],Core,Vector3.left,false)
                : creekPreset
                ? Join(kit[5],Core,Vector3.right,false) && Join(kit[6],Core,Vector3.left,false)
                : Join(kit[1],Core,Vector3.forward,false) && Join(kit[2],kit[1],Vector3.forward,false)
                && Join(kit[5],kit[1],Vector3.right,false) && Join(kit[6],kit[1],Vector3.left,false)
                && Join(kit[7],kit[2],Vector3.right,true) && Join(kit[8],kit[2],Vector3.left,true);
            if(!success) { TryResetBuild(); Workshop.Session.Notify("Example placement blocked; kit reset."); return false; }
            Workshop.Session.Select(creekPreset && !StarterCarBuilt?Core:kit[level2?2:1]); Workshop.Session.Notify("Example ready. Drag a block to detach its branch, or press START."); return true;
        }
    }
}
