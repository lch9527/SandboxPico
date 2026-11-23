using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Unity.Mathematics;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class SandboxFactorySamples
    {
        public const string SamplePath="Assets/MetaHackthone/Scenes/FactoryFinalRun.unity";
        public const string PresetFolder="Assets/MetaHackthone/Generated/LevelFactory/Vehicles";
        [MenuItem("MetaHackthone/Level Factory/Open Final Run Sample")]
        public static void OpenSample()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(SamplePath)) CreateSample();
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(SamplePath);
        }
        public static void CreateSample()
        {
            SandboxFactoryAuthoring.EnsureTemplate();
            BuildPresets();
            if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(SamplePath)) AssetDatabase.CopyAsset(SandboxFactoryAuthoring.TemplatePath,SamplePath);
            var previous=SceneManager.GetActiveScene(); var scene=EditorSceneManager.OpenScene(SamplePath,OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene); bool suspended=SandboxTrackEditing.Suspended; SandboxTrackEditing.Suspended=true;
            try {
                var existing=SandboxFactoryAuthoring.Find(scene);
                if(existing.Tracks.Length>0) throw new InvalidOperationException("Sample already contains tracks; edit it instead of replacing it.");
                SandboxFactoryAuthoring.MakeIndependent(scene,SamplePath);
                var factory=SandboxFactoryAuthoring.Find(scene); factory.Title="Final Run"; factory.Timeout=90;
                factory.GetComponent<BuildingSceneLayout>().Downhill.SandboxFrame.localScale=Vector3.one;
                factory.TrackRoot.localScale=Vector3.one*factory.CourseScale;
                ConfigureKit(factory);
                var track=SandboxFactoryAuthoring.AddTrack(factory,false); track.Supports=false; track.DefaultWidth=2.8f; track.RailHeight=.55f;
                var spline=new Spline();
                foreach(var p in new[]{new float3(0,4,0),new float3(0,3.9f,2),new float3(0,3,8),new float3(0,2.5f,16),new float3(0,2,24),
                    new float3(2,1.8f,30),new float3(8,1.4f,34),new float3(14,1,30),new float3(16,.8f,24),new float3(16,.6f,16),new float3(16,-.2f,8)})
                    spline.Add(new BezierKnot(p),TangentMode.AutoSmooth);
                track.Curve.Spline=spline;
                for(int i=8;i<spline.Count;i++) track.SetKnotWidth(i,2.2f);
                string physicsPath=System.IO.Path.ChangeExtension(SamplePath,null)+"_Factory/SampleRoad.physicMaterial";
                var road=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physicsPath);
                if(!road) {road=new PhysicsMaterial("Smooth sample road") {staticFriction=.02f,dynamicFriction=.02f,frictionCombine=PhysicsMaterialCombine.Minimum};AssetDatabase.CreateAsset(road,physicsPath);}
                track.RoadPhysics=road;
                factory.TestBounds.center=Vector3.zero; factory.TestBounds.size=new Vector3(80,40,80);
                var roller=(SandboxRoller)SandboxPuzzleEditing.Add(track,typeof(SandboxRoller),SandboxPuzzleEditing.Nearest(track,track.transform.TransformPoint(new Vector3(0,2,10))));
                roller.Width=track.DefaultWidth; roller.Diameter=.12f;
                var drop=(SandboxDropBridge)SandboxPuzzleEditing.Add(track,typeof(SandboxDropBridge),SandboxPuzzleEditing.Nearest(track,track.transform.TransformPoint(new Vector3(0,2,22))));
                drop.Length=3; drop.Width=track.DefaultWidth; drop.TargetApproach=6; drop.TargetHeight=1.32f; drop.TargetLayer=4; drop.ReleaseSuspensionWithTarget=true; drop.TargetSize=SandboxImprovementsAuthoring.FinalRunTargetSize;
                drop.TargetApproach=SandboxImprovementsAuthoring.FinalRunTargetApproach; drop.TargetDistance=drop.Distance-drop.Length*.5f-drop.TargetApproach; // layer 4 meets the default "Combined wide high drill" car drop.SupportSide=-2;
                var crab=(SandboxCrabBridge)SandboxPuzzleEditing.Add(track,typeof(SandboxCrabBridge),SandboxPuzzleEditing.Nearest(track,track.transform.TransformPoint(new Vector3(16,.5f,15))));
                crab.Length=6; crab.HoleWidth=1.1f; crab.LeftLaneWidth=crab.RightLaneWidth=.55f;
                var path=SandboxTrackBaker.SamplePath(track);
                factory.StartBinding=new SandboxTrackBinding {Track=track,Distance=.6f}; factory.BindStart=true;
                var start=SandboxPuzzleEditing.At(path,.6f); factory.Start.SetPositionAndRotation(track.transform.TransformPoint(start.Position),track.transform.rotation*Quaternion.LookRotation(start.Forward,start.Up));
                var end=SandboxPuzzleEditing.At(path,path[path.Count-1].Distance-.5f);
                factory.Finish.transform.SetPositionAndRotation(track.transform.TransformPoint(end.Position+Vector3.up*.8f),track.transform.rotation*Quaternion.LookRotation(end.Forward,end.Up));
                factory.Finish.size=new Vector3(3,2,1);
                factory.Presets=AssetDatabase.FindAssets("t:SandboxTestCarAsset",new[]{PresetFolder}).Select(g=>AssetDatabase.LoadAssetAtPath<SandboxTestCarAsset>(AssetDatabase.GUIDToAssetPath(g))).OrderBy(a=>a.name).ToArray();
                factory.TestCar=factory.Presets.Single(p=>p.name=="Combined wide high drill");
                var arches=new[]{("Start",factory.Start,"StartArch"),("Goal",factory.Finish.transform,"GoalArch")};
                foreach(var item in arches) {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MetaHackthone/Resources/CourseArches/"+item.Item3+".prefab");
                    if(!prefab) continue;
                    var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,factory.TrackRoot); go.name=item.Item1+" banner";
                    go.transform.SetPositionAndRotation(item.Item2.position,item.Item2.rotation); go.transform.localScale=Vector3.one;
                }
                SandboxTrackEditing.Suspended=false;
                if(!SandboxTrackEditing.Flush(scene,true)) throw new InvalidOperationException("Sample validation failed: "+track.ValidationError);
                EditorSceneManager.SaveScene(scene);
            } finally { SandboxTrackEditing.Suspended=suspended; EditorSceneManager.CloseScene(scene,true); SceneManager.SetActiveScene(previous); }
        }
        public static void ConfigureKit(SandboxFactoryLevel factory)
        {
            var layout=factory.GetComponent<BuildingSceneLayout>();
            var core=layout.Spawns.First(p=>p && p.Kind==PartKind.DownhillCore);
            var kinds=new[]{PartKind.DownhillLongBlock,PartKind.DownhillLongBlock,PartKind.DownhillLongBlock,PartKind.DownhillShortBlock,PartKind.DownhillDrill,
                PartKind.DownhillLargeWheel,PartKind.DownhillLargeWheel,PartKind.DownhillSmallWheel,PartKind.DownhillSmallWheel};
            foreach(var p in layout.Spawns) if(p && p!=core) Object.DestroyImmediate(p.gameObject);
            var list=new List<BuildingPartSpawn>{core};
            for(int i=0;i<kinds.Length;i++) {
                var go=new GameObject((i+1).ToString("00")+" "+kinds[i]+" spawn"); go.transform.SetParent(core.transform.parent,false);
                go.transform.localPosition=core.transform.localPosition+new Vector3(i<5?-.3f:.3f,.05f,(i<5?i-2:i-6.5f)*.14f);
                go.transform.localScale=core.transform.localScale; var marker=go.AddComponent<BuildingPartSpawn>(); marker.Kind=kinds[i]; list.Add(marker);
            }
            layout.Spawns=list.ToArray();
            var entry=factory.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CloudGarden.Integration.DownhillEntry>()).Single();
            BuildingSceneAuthoring.CreatePreviews(layout,entry.Settings,missingOnly:true);
            BuildingSceneAuthoring.PersistResources(layout,"FactoryParts");
        }
        public static void BuildPresets()
        {
            SandboxTrackBaker.EnsureFolder(PresetFolder);
            foreach(string name in new[]{"Large front wheels","Reversed wheels","Wide crab","Narrow car","Asymmetric crab","No drill","Low drill","High drill","Combined wide high drill"}) {
                string path=PresetFolder+"/"+name+".asset"; if(AssetDatabase.LoadAssetAtPath<SandboxTestCarAsset>(path)) continue;
                var asset=ScriptableObject.CreateInstance<SandboxTestCarAsset>(); asset.Assembly=BuildVehicle(name); AssetDatabase.CreateAsset(asset,path);
            }
            AssetDatabase.SaveAssets();
        }
        public static void AssignPresets(SandboxFactoryLevel factory)
        {
            BuildPresets();
            factory.Presets=AssetDatabase.FindAssets("t:SandboxTestCarAsset",new[]{PresetFolder}).Select(g=>AssetDatabase.LoadAssetAtPath<SandboxTestCarAsset>(AssetDatabase.GUIDToAssetPath(g))).OrderBy(a=>a.name).ToArray();
            if(!factory.TestCar) factory.TestCar=factory.Presets.Single(p=>p.name=="Combined wide high drill");
            EditorUtility.SetDirty(factory);
        }
        public static AssemblySaveV1 BuildVehicle(string name)
        {
            var root=new GameObject("Temporary reference vehicle"); var world=root.AddComponent<AssemblyWorld>(); world.FloatingBuild=true;
            float scale=.4f,u=BuildingGeometry.Unit*scale;
            var parts=new List<AssemblyPart>();
            AssemblyPart Add(PartKind kind,Vector3 position,Quaternion rotation) {
                var p=DownhillParts.Create(world,kind,new Pose(position*u,rotation),scale,gridVersion:DownhillParts.DesignerGridVersion,buildingCore:kind==PartKind.DownhillCore);
                p.transform.SetParent(root.transform,true); parts.Add(p);
                foreach(var body in p.Bodies) body.Rigidbody.isKinematic=true;
                return p;
            }
            void Join(AssemblyPart source,AssemblyPart target) {
                var pairs=from a in source.Sockets from b in target.Sockets where !world.SocketOccupied(a) && !world.SocketOccupied(b) && Vector3.Dot(a.Normal,b.Normal)<-.99f
                    orderby (a.WorldPosition-b.WorldPosition).sqrMagnitude select (a,b);
                var pair=pairs.First(); if(Vector3.Distance(pair.a.WorldPosition,pair.b.WorldPosition)>.0005f) throw new InvalidOperationException(name+": unmatched sockets "+source.Kind+" "+target.Kind+" "+Vector3.Distance(pair.a.WorldPosition,pair.b.WorldPosition));
                if(!world.Connect(new ConnectionRecord {id=Guid.NewGuid().ToString("N"),a=new BodyRef(source.InstanceId,pair.a.Body.Id),b=new BodyRef(target.InstanceId,pair.b.Body.Id),
                    socketA=pair.a.Id,socketB=pair.b.Id,anchorA=pair.a.transform.localPosition,anchorB=pair.b.transform.localPosition})) throw new InvalidOperationException("Reference connection rejected.");
            }
            try {
                var core=Add(PartKind.DownhillCore,Vector3.zero,Quaternion.identity);
                bool wide=name.Contains("crab") || name.Contains("wide"); bool reversed=name=="Reversed wheels";
                var large=PartKind.DownhillLargeWheel; var small=PartKind.DownhillSmallWheel; float halfTire=.23f/.38f*.5f;
                void Wheels(AssemblyPart beam,float x,float y,float front,float rear,int side) {
                    var rot=side<0?Quaternion.Euler(0,180,0):Quaternion.identity;
                    var a=Add(reversed?small:large,new Vector3(x,y,front),rot); Join(a,beam);
                    var b=Add(reversed?large:small,new Vector3(x,y,rear),rot); Join(b,beam);
                }
                if(wide) {
                    var cross=Add(PartKind.DownhillLongBlock,new Vector3(0,-1,0),Quaternion.Euler(0,90,0)); Join(cross,core);
                    foreach(int side in new[]{-1,1}) {
                        var beam=Add(PartKind.DownhillLongBlock,new Vector3(side*2,-1,0),Quaternion.identity); Join(beam,cross);
                        bool inner=name=="Asymmetric crab" && side<0;
                        Wheels(beam,side*2+(inner?-side:side)*(.5f+halfTire),-1,1,-1,inner?-side:side);
                    }
                } else {
                    var beam=Add(PartKind.DownhillLongBlock,new Vector3(0,0,-2),Quaternion.identity); Join(beam,core);
                    foreach(int side in new[]{-1,1}) {
                        var a=Add(reversed?small:large,new Vector3(side*(.5f+halfTire),0,0),side<0?Quaternion.Euler(0,180,0):Quaternion.identity); Join(a,core);
                        var b=Add(reversed?large:small,new Vector3(side*(.5f+halfTire),0,-3),side<0?Quaternion.Euler(0,180,0):Quaternion.identity); Join(b,beam);
                    }
                }
                if(name.Contains("drill") && name!="No drill") {
                    bool high=name!="Low drill"; var support=core;
                    if(high) { support=Add(PartKind.DownhillShortBlock,new Vector3(0,1.5f,0),Quaternion.Euler(90,0,0)); Join(support,core); }
                    var position=new Vector3(0,high?2:0,.5f)-SandboxDrill.BaseCenter(BuildingGeometry.Unit,scale)/u;
                    var drill=Add(PartKind.DownhillDrill,position,Quaternion.identity); Join(drill,support);
                }
                var saves=root.AddComponent<AssemblySaveService>(); saves.World=world;
                var data=saves.Capture(core); AssemblySaveService.Validate(data); return data;
            } finally { Object.DestroyImmediate(root); }
        }
    }
}
