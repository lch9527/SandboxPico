using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    // Opt-in, editor-only capture of the real launch/Retry path. Does not save scenes or assets.
    [InitializeOnLoad]
    public static class SandboxPhysicsReproduction
    {
        static readonly PropertyInfo Comparing = typeof(DownhillController).GetProperty("GarageComparing");
        static DownhillController level;
        static SandboxTestCarAsset temporaryCar;
        static AssemblyBody[] bodies;
        static Joint[] joints;
        static StringBuilder csv;
        static string label;
        static double started, runningAt;
        static float duration, stepOverride, previousStep, previousCountdown, previousGo;
        static bool previousAssist, previousLimited;
        static int repetition, repetitions, lastFrame;
        static float lastMotionTime;
        static Vector3 lastPosition;
        public static string Status { get; private set; } = "Idle";

        static SandboxPhysicsReproduction()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += state => { if(state==PlayModeStateChange.ExitingPlayMode) Stop(); };
        }

        public static string Start(string name, string car = "Compact mixed wheels", int assist = -1,
            float fixedStep = 0, int repeats = 1, float seconds = 30)
        {
            if(!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            if(level) throw new InvalidOperationException("A physics capture is already running.");
            level = Object.FindFirstObjectByType<WorkshopBootstrap>().Downhill;
            if(!level.FactoryLevel) { level = null; throw new InvalidOperationException("Open a factory course."); }
            previousAssist = level.FactoryLevel.CenterPathAssist;
            previousLimited = level.FactoryLevel.LimitedPathAssist;
            previousCountdown = level.Settings.CountdownStep; previousGo = level.Settings.GoDuration;
            previousStep = Time.fixedDeltaTime;
            label = name; repetitions = repeats; repetition = 0; duration = seconds; stepOverride = fixedStep;
            try {
                level.RestoreBuildImmediately();
                if(assist >= 0) { level.FactoryLevel.CenterPathAssist = assist != 0; level.FactoryLevel.LimitedPathAssist = assist == 1; }
                level.Settings.CountdownStep = .01f; level.Settings.GoDuration = .01f;
                SandboxTestCarAsset asset;
                if(car == "Compact mixed wheels") {
                    temporaryCar = ScriptableObject.CreateInstance<SandboxTestCarAsset>();
                    temporaryCar.Assembly = CompactCar(); asset = temporaryCar;
                } else if(car == "Course test car") asset = level.FactoryLevel.TestCar;
                else asset = AssetDatabase.LoadAssetAtPath<SandboxTestCarAsset>(SandboxFactorySamples.PresetFolder + "/" + car + ".asset");
                if(!asset || !level.TryLoadFactoryCar(asset)) throw new InvalidOperationException("Could not load " + car);
                Directory.CreateDirectory("Logs/SandboxPhysics");
                File.WriteAllText("Logs/SandboxPhysics/" + name + "-car.json", JsonUtility.ToJson(level.Workshop.Saves.Capture(level.Core), true));
                Begin(false);
                EditorApplication.update += Tick;
                return Status;
            } catch { Stop(); throw; }
        }

        static void Begin(bool retry)
        {
            Comparing.SetValue(level, false);
            csv = new StringBuilder("time,state,x,y,z,vx,vy,vz,spin,dt,frameMs,guide,jointError,minUp,energy,contact\n");
            bodies = null; joints = null; runningAt = -1; lastFrame = -1;
            started = EditorApplication.timeSinceStartup;
            bool accepted = retry ? level.TryRetry() : level.TryStart();
            if(!accepted) throw new InvalidOperationException("Launch/Retry rejected: " + level.Workshop.Session.Status);
            // Reuse the existing comparison guard to keep focus changes from cancelling diagnostic runs.
            Comparing.SetValue(level, true);
            Status = label + " attempt " + (repetition + 1) + "/" + repetitions;
        }

        static void Tick()
        {
            try {
                if(!EditorApplication.isPlaying || !level) { Stop(); return; }
                if(Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                double now = EditorApplication.timeSinceStartup;
                var core = level.SimulationCore ? level.SimulationCore.MainBody.Rigidbody : null;
                if(core && bodies == null) {
                    bodies = level.SimulationWorld.Parts.Values.SelectMany(p => p.Bodies).ToArray();
                    joints = level.SimulationWorld.GetComponentsInChildren<Joint>();
                    foreach(var b in bodies) if(!b.GetComponent<SandboxPuzzleContact>()) b.gameObject.AddComponent<SandboxPuzzleContact>();
                }
                if(level.State == DownhillState.Running) {
                    if(runningAt < 0) { runningAt = now; lastMotionTime = Time.unscaledTime; lastPosition = core.position; }
                    if(stepOverride > 0) Time.fixedDeltaTime = stepOverride;
                    if(Vector3.Distance(lastPosition,core.position) > .01f) { lastPosition = core.position; lastMotionTime = Time.unscaledTime; }
                }
                var p = core ? core.position : Vector3.zero; var v = core ? core.linearVelocity : Vector3.zero;
                float error = 0, minUp = 1, energy = 0;
                if(joints != null) foreach(var j in joints) if(j && j.connectedBody)
                    error = Mathf.Max(error,Vector3.Distance(j.transform.TransformPoint(j.anchor),j.connectedBody.transform.TransformPoint(j.connectedAnchor)));
                if(bodies != null) foreach(var b in bodies) {
                    var rb = b.Rigidbody;
                    if(!DownhillParts.IsWheel(b.Part.Kind)) minUp = Mathf.Min(minUp,Vector3.Dot(rb.rotation * Vector3.up,Vector3.up));
                    var spin = Quaternion.Inverse(rb.rotation * rb.inertiaTensorRotation) * rb.angularVelocity;
                    energy += rb.mass * (.5f * rb.linearVelocity.sqrMagnitude - Vector3.Dot(Physics.gravity,rb.worldCenterOfMass))
                        + .5f * Vector3.Dot(Vector3.Scale(rb.inertiaTensor,spin),spin);
                }
                string contact = bodies == null ? "" : string.Join("|", bodies.Select(b=>b.GetComponent<SandboxPuzzleContact>().LastSurface).Where(s=>!string.IsNullOrEmpty(s)).Distinct());
                var guide = level.SimulationWorld ? level.SimulationWorld.GetComponent<SandboxFactoryPathAssist>() : null;
                csv.AppendLine(string.Join(",",new object[]{now-started,level.State,p.x,p.y,p.z,v.x,v.y,v.z,
                    core?core.angularVelocity.magnitude:0,Time.fixedDeltaTime,Time.unscaledDeltaTime*1000,guide && guide.Guiding,error,minUp,energy,contact}
                    .Select(o=>Convert.ToString(o,CultureInfo.InvariantCulture))));
                bool finished = level.State == DownhillState.Success || level.State == DownhillState.Failed;
                bool stalled = runningAt >= 0 && Time.unscaledTime - lastMotionTime > 3;
                if(finished || stalled || (runningAt >= 0 && now-runningAt >= duration) || now-started > duration+15) {
                    string outcome = finished ? level.State.ToString() : stalled ? "Stalled" : "Capture limit";
                    File.WriteAllText($"Logs/SandboxPhysics/{label}-{repetition+1:D2}.csv",csv.ToString());
                    File.AppendAllText("Logs/SandboxPhysics/results.txt",$"{label} attempt {repetition+1}: {outcome}; position={p:F5}; dt={Time.fixedDeltaTime:R}\n");
                    if(++repetition < repetitions && level.SimulationCore) Begin(true);
                    else { Status = label + ": " + outcome; Stop(); }
                }
            } catch(Exception e) { Status = "ERROR: " + e; Debug.LogException(e); Stop(); }
        }

        public static void Stop()
        {
            EditorApplication.update -= Tick;
            if(!level) return;
            try {
                Comparing.SetValue(level,false);
                if(level.FactoryLevel) { level.FactoryLevel.CenterPathAssist = previousAssist; level.FactoryLevel.LimitedPathAssist = previousLimited; }
                level.Settings.CountdownStep = previousCountdown; level.Settings.GoDuration = previousGo;
                level.RestoreBuildImmediately();
            } finally {
                Time.fixedDeltaTime = previousStep;
                if(temporaryCar) Object.DestroyImmediate(temporaryCar);
                temporaryCar = null; level = null;
            }
        }

        // Core above a single 1x3 spine, large wheels at +Z and small wheels at -Z.
        public static AssemblySaveV1 CompactCar()
        {
            var root = new GameObject("Compact reproduction car");
            var world = root.AddComponent<AssemblyWorld>(); world.FloatingBuild = true;
            const float scale = .4f; float u = BuildingGeometry.Unit * scale;
            AssemblyPart Add(PartKind kind, Vector3 position, Quaternion rotation) {
                var part = DownhillParts.Create(world,kind,new Pose(position*u,rotation),scale,
                    gridVersion:DownhillParts.DesignerGridVersion,buildingCore:kind==PartKind.DownhillCore);
                part.transform.SetParent(root.transform,true);
                foreach(var b in part.Bodies) b.Rigidbody.isKinematic = true;
                return part;
            }
            void Join(AssemblyPart a, AssemblyPart b) {
                var pair = (from x in a.Sockets from y in b.Sockets
                    where !world.SocketOccupied(x) && !world.SocketOccupied(y) && Vector3.Dot(x.Normal,y.Normal)<-.99f
                    orderby (x.WorldPosition-y.WorldPosition).sqrMagnitude select (x,y)).First();
                if(Vector3.Distance(pair.x.WorldPosition,pair.y.WorldPosition)>.0005f) throw new InvalidOperationException("Unmatched reproduction sockets");
                if(!world.Connect(new ConnectionRecord {id=Guid.NewGuid().ToString("N"),a=new BodyRef(a.InstanceId,pair.x.Body.Id),b=new BodyRef(b.InstanceId,pair.y.Body.Id),
                    socketA=pair.x.Id,socketB=pair.y.Id,anchorA=pair.x.transform.localPosition,anchorB=pair.y.transform.localPosition})) throw new InvalidOperationException("Connection rejected");
            }
            try {
                var core = Add(PartKind.DownhillCore,Vector3.up,Quaternion.identity);
                var spine = Add(PartKind.DownhillLongBlock,Vector3.zero,Quaternion.identity); Join(spine,core);
                foreach(int side in new[]{-1,1}) foreach(int end in new[]{-1,1}) {
                    var wheel = Add(end>0?PartKind.DownhillLargeWheel:PartKind.DownhillSmallWheel,
                        new Vector3(side*(.5f+.23f/.38f*.5f),0,end),side<0?Quaternion.Euler(0,180,0):Quaternion.identity);
                    Join(wheel,spine);
                }
                var saves = root.AddComponent<AssemblySaveService>(); saves.World = world;
                var save = saves.Capture(core); AssemblySaveService.Validate(save); return save;
            } finally { Object.DestroyImmediate(root); }
        }
    }
}
