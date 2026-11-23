using System;
using System.Linq;
using UnityEngine;

namespace PhysicalAssembly
{
    public sealed partial class DownhillController
    {
        SandboxFactoryLevel campaignFactory;
        public SandboxFactoryLevel FactoryLevel => campaignFactory ? campaignFactory : Workshop && Workshop.Layout ? Workshop.Layout.GetComponent<SandboxFactoryLevel>() : null;
        void BindFactoryCourse()
        {
            var factory=FactoryLevel;
            Course.StartPoint.SetPositionAndRotation(factory.Start.position,factory.Start.rotation);
            if(Presentation.Interface) { Presentation.Interface.CourseTitle=factory.Title; Presentation.Interface.CourseNumber=1; Presentation.Interface.CourseCount=1; Presentation.Interface.RefreshCourseCard(); }
            RefreshCampaignCard();
            Presentation.SetBuilding(IsBuilding); SpawnPreview.Refresh();
        }
        void UpdateFactoryRun()
        {
            elapsed+=Time.fixedDeltaTime;
            if(finishBraking) { UpdateFinishBrake(); return; }
            var factory=FactoryLevel; var position=SimulationCore.MainBody.Rigidbody.position;
            if(CheckFactoryWater(out var waterPoint,out var waterNormal)) {
                if(Feedback) Feedback.Emit(SandboxSoundCue.Splash,waterPoint,SimulationCore,waterNormal);
                Resolve(false); Workshop.Session.Notify("Vehicle fell into the water. Retry or Back to Build.");
            }
            else if(SandboxFactoryLevel.Contains(factory.Finish,position)) BeginFinishBrake();
            else if(!SandboxFactoryLevel.Contains(factory.TestBounds,position) || elapsed>=factory.Timeout) Resolve(false);
        }
        // Campaign water is scenery without colliders. The flat water meshes themselves define the hazard:
        // a vehicle collider whose lowest point drops below a surface, inside that surface's footprint, is in the water.
        sealed class FactoryWater
        {
            public float SurfaceY; public Vector2 Min, Max; public Vector2[] Triangles;
            public bool Covers(Vector2 p)
            {
                if(p.x<Min.x || p.y<Min.y || p.x>Max.x || p.y>Max.y) return false;
                for(int i=0;i<Triangles.Length;i+=3) {
                    Vector2 a=Triangles[i],b=Triangles[i+1],c=Triangles[i+2];
                    float d1=(p.x-b.x)*(a.y-b.y)-(a.x-b.x)*(p.y-b.y), d2=(p.x-c.x)*(b.y-c.y)-(b.x-c.x)*(p.y-c.y), d3=(p.x-a.x)*(c.y-a.y)-(c.x-a.x)*(p.y-a.y);
                    if(!((d1<0 || d2<0 || d3<0) && (d1>0 || d2>0 || d3>0))) return true;
                }
                return false;
            }
        }
        readonly System.Collections.Generic.List<FactoryWater> factoryWater=new();
        SandboxFactoryLevel factoryWaterLevel;
        public int FactoryWaterSurfaceCount { get { var factory=FactoryLevel; if(factory && factoryWaterLevel!=factory) BuildFactoryWater(factory); return factoryWater.Count; } }
        void BuildFactoryWater(SandboxFactoryLevel factory)
        {
            factoryWater.Clear(); factoryWaterLevel=factory;
            var space=factory.transform;
            foreach(var filter in factory.GetComponentsInChildren<MeshFilter>(true)) {
                var renderer=filter.GetComponent<Renderer>(); var mesh=filter.sharedMesh;
                if(!renderer || !mesh || !mesh.isReadable || !renderer.sharedMaterials.Any(m=>m && m.shader && m.shader.name.IndexOf("water",StringComparison.OrdinalIgnoreCase)>=0)) continue;
                var vertices=mesh.vertices; var triangles=mesh.triangles; if(triangles.Length<3) continue;
                var local=new Vector3[vertices.Length];
                for(int i=0;i<vertices.Length;i++) local[i]=space.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                float minY=local.Min(v=>v.y), maxY=local.Max(v=>v.y);
                Vector2 min=new(local.Min(v=>v.x),local.Min(v=>v.z)), max=new(local.Max(v=>v.x),local.Max(v=>v.z));
                // Only lying surfaces: waterfall curtains, droplets, and spring runs are not pools the car can fall into.
                if(maxY-minY>.1f*Mathf.Max(max.x-min.x,max.y-min.y) || Mathf.Max(max.x-min.x,max.y-min.y)<.05f) continue;
                var flat=new Vector2[triangles.Length];
                for(int i=0;i<triangles.Length;i++) { var v=local[triangles[i]]; flat[i]=new Vector2(v.x,v.z); }
                factoryWater.Add(new FactoryWater{SurfaceY=maxY,Min=min,Max=max,Triangles=flat});
            }
        }
        bool CheckFactoryWater(out Vector3 point,out Vector3 normal)
        {
            point=default; var factory=FactoryLevel; normal=factory.transform.up;
            if(factoryWaterLevel!=factory) BuildFactoryWater(factory);
            if(factoryWater.Count==0 || !SimulationWorld) return false;
            var space=factory.transform;
            foreach(var part in SimulationWorld.Parts.Values) foreach(var body in part.Bodies) foreach(var collider in body.Colliders) {
                if(!collider || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
                var bounds=collider.bounds;
                var low=space.InverseTransformPoint(collider.ClosestPoint(bounds.center-normal*(bounds.size.magnitude+1)));
                foreach(var water in factoryWater)
                    if(low.y<water.SurfaceY && water.Covers(new Vector2(low.x,low.z))) { point=space.TransformPoint(new Vector3(low.x,water.SurfaceY,low.z)); return true; }
            }
            return false;
        }
        // Reaching the finish wins the run; the car then brakes to rest instead of freezing mid-roll.
        public bool FinishBraking => finishBraking;
        public float FinishBrakeDistance { get; private set; }
        const float FinishBrakeTimeout=2f;
        bool finishBraking;
        float brakeRemaining,brakeAge,brakeStopSpeed;
        void BeginFinishBrake()
        {
            var velocity=SimulationCore.MainBody.Rigidbody.linearVelocity; float speed=velocity.magnitude;
            float length=speed>1e-5f?VehicleLength(velocity/speed):0;
            float distance=Settings.FinishBrakeLengths*length;
            // Never brake past the end of the road: keep the whole car on the track.
            float room=-1;
            if(length>0 && FactoryLevel.Tracks!=null) foreach(var track in FactoryLevel.Tracks) {
                if(!track || track.Samples==null || track.Samples.Length==0) continue;
                var end=track.transform.TransformPoint(track.Samples[track.Samples.Length-1]);
                room=Mathf.Max(room,Vector3.Dot(end-SimulationCore.MainBody.Rigidbody.position,velocity/speed));
            }
            if(room>0) distance=Mathf.Min(distance,Mathf.Max(room-length*.5f,length*.25f));
            FinishBrakeDistance=brakeRemaining=distance;
            brakeStopSpeed=length*.05f; brakeAge=0;
            if(brakeRemaining<=1e-5f || speed<=brakeStopSpeed) { Resolve(true); return; }
            finishBraking=true;
            // The win is decided on arrival; leaving during the brake must not lose it.
            if(Settings.Campaign && !DesignModeSession.Enabled) SandboxCampaignSession.Complete(CurrentCourse,Settings.Level4);
        }
        void UpdateFinishBrake()
        {
            float dt=Time.fixedDeltaTime, speed=SimulationCore.MainBody.Rigidbody.linearVelocity.magnitude;
            brakeRemaining-=speed*dt; brakeAge+=dt;
            if(speed<=brakeStopSpeed || brakeRemaining<=1e-5f || brakeAge>=FinishBrakeTimeout) { Resolve(true); return; }
            // Constant deceleration that reaches zero exactly at the end of the braking distance.
            // One factor for every body keeps wheels and joints moving as one rigid vehicle.
            float factor=Mathf.Clamp01(1-speed*dt/(2*brakeRemaining));
            foreach(var body in SimulationWorld.Parts.Values.SelectMany(p=>p.Bodies)) {
                var rb=body.Rigidbody; if(rb.isKinematic) continue;
                rb.linearVelocity*=factor; rb.angularVelocity*=factor;
            }
        }
        float VehicleLength(Vector3 direction)
        {
            float min=float.PositiveInfinity,max=float.NegativeInfinity;
            foreach(var part in SimulationWorld.Parts.Values) foreach(var body in part.Bodies) foreach(var collider in body.Colliders)
                foreach(var point in ColliderPoints(collider)) { float d=Vector3.Dot(point,direction); min=Mathf.Min(min,d); max=Mathf.Max(max,d); }
            return max>min?max-min:0;
        }
        public bool TryBuildFactoryCar()
        {
            if(!FactoryLevel || !IsBuilding || !CanSwitchMode || Rotating) return false;
            CompleteBoundaryReturns(); ClearGarageKit(); SpawnDesignerKit(); return true;
        }
        public bool TryLoadFactoryCar(SandboxTestCarAsset asset)
        {
            if(!FactoryLevel || !IsBuilding || !CanSwitchMode || Rotating || !asset) return false;
            var old=kit.ToArray(); var rules=Workshop.World.Rules;
            try {
                AssemblySaveService.Validate(asset.Assembly);
                if(asset.Assembly.parts.Count(p=>p.buildingCore)!=1 || asset.Assembly.parts.Any(p=>!DownhillParts.DesignerKit.Contains(p.kind) && p.kind!=PartKind.DownhillDrill))
                    throw new InvalidOperationException("Use a factory car with exactly one building core and sandbox parts.");
                if(FactoryLevel.UseStarterPalette && asset.Assembly.parts.Any(p=>!SandboxCourseCatalog.PartAvailable(CurrentCourse,p.kind)))
                    throw new InvalidOperationException("This car uses parts that are locked on this course.");
                CompleteBoundaryReturns();
                foreach(var part in old) part.gameObject.SetActive(false);
                Workshop.World.Rules=null;
                var assembly=JsonUtility.FromJson<AssemblySaveV1>(JsonUtility.ToJson(asset.Assembly));
                float ratio=Workshop.Layout.Spawns.First(s=>s && s.Kind==PartKind.DownhillCore).SizeMultiplier/assembly.parts.Single(p=>p.buildingCore).geometryScale;
                foreach(var part in assembly.parts) { part.geometryScale*=ratio; foreach(var body in part.bodies) body.position*=ratio; }
                foreach(var edge in assembly.connections) { edge.anchorA*=ratio; edge.anchorB*=ratio; }
                var loaded=Workshop.Saves.Restore(assembly,new Pose(Presentation.AssemblyAnchor.position,Presentation.AssemblyAnchor.rotation));
                Workshop.World.Rules=rules;
                // Commit only after all bodies, sockets, and connections have been restored.
                var ids=old.Select(p=>p.InstanceId).ToHashSet();
                foreach(var edge in Workshop.World.Connections.Where(e=>ids.Contains(e.a.partId)||ids.Contains(e.b.partId)).ToArray()) Workshop.World.RemoveConnection(edge.id);
                foreach(var part in old) { Workshop.World.UnregisterPart(part); Destroy(part.gameObject); }
                kit.Clear(); kit.AddRange(loaded); spawns.Clear(); saved.Clear(); returns.Clear(); previouslyHeld.Clear();
                Core=loaded.Single(p=>p.BuildingCore);
                foreach(var part in loaded) spawns[part]=Capture(part.Bodies).ToArray();
                TopologyChanged(); RebuildGarageCourse(); Workshop.Session.Select(Core); UpdateControls();
                Workshop.Session.Notify("Saved test car loaded."); return true;
            } catch(Exception e) {
                foreach(var part in old) if(part) part.gameObject.SetActive(true);
                Workshop.Session.Notify("Car load failed; build preserved: "+e.Message); return false;
            } finally { Workshop.World.Rules=rules; }
        }
    }
}
