using System;
using UnityEngine;

namespace PhysicalAssembly
{
    public readonly struct SandboxFeedbackEvent
    {
        public readonly SandboxSoundCue Cue;
        public readonly Vector3 Position, Normal;
        public readonly float Intensity, Size;
        public readonly SandboxSurfaceMaterial Material;
        public readonly UnityEngine.Object Source;
        public readonly bool Silent;
        public SandboxFeedbackEvent(SandboxSoundCue cue, Vector3 position, Vector3 normal, float intensity,
            float size, SandboxSurfaceMaterial material, UnityEngine.Object source, bool silent = false)
        { Cue=cue; Position=position; Normal=normal.sqrMagnitude>.001f?normal.normalized:Vector3.up;
          Intensity=intensity; Size=size; Material=material; Source=source; Silent=silent; }
    }

    // Gameplay owns these events. Neither audio voice availability nor the VFX budget gates them.
    [DisallowMultipleComponent]
    public sealed class SandboxPresentation : MonoBehaviour
    {
        public event Action<SandboxFeedbackEvent> Presented;
        public event Action Cleared;
        public event Action<Transform,Transform,float,float> TransferBegan;
        public DownhillController Level { get; private set; }
        public Vector3 Up => Level && Level.Course ? Level.Course.SandboxFrame.up : Vector3.up;
        public bool Running => Available && Level.State==DownhillState.Running && (!Level.Run25D || !Level.Run25D.Paused);
        bool Available => isActiveAndEnabled && Level && Level.isActiveAndEnabled && !paused && Time.timeScale>0;
        AssemblyWorld world, simulation;
        bool subscribed, paused, transferring;
        float nextImpact, nextDrill;
        public void Initialize(DownhillController level) { Unsubscribe(); Level=level; world=level.Workshop.World; Subscribe(); }
        void Subscribe()
        {
            if(subscribed || !world) return;
            world.InteractionPresented+=Interaction; WorkshopButton.Activated+=Button; subscribed=true;
        }
        void Unsubscribe()
        {
            if(!subscribed) return;
            if(world) world.InteractionPresented-=Interaction;
            WorkshopButton.Activated-=Button; subscribed=false;
        }
        void OnEnable() => Subscribe();
        void OnDisable() { Unsubscribe(); Clear(); }
        void OnApplicationPause(bool value) { paused=value; if(value) Clear(); }
        void Update() { if(!Available || Level.Run25D && Level.Run25D.Paused) { Clear(); return; } BindSimulation(); }
        void Interaction(AssemblyInteractionKind kind, AssemblyPart part, Vector3 point, Vector3 normal)
        {
            if(!Level.IsBuilding || !world.EditingEnabled) return;
            var cue=kind switch { AssemblyInteractionKind.Attach=>SandboxSoundCue.Attach,
                AssemblyInteractionKind.Detach=>SandboxSoundCue.Detach, AssemblyInteractionKind.Reject=>SandboxSoundCue.Reject, _=>GrabCue(part) };
            Emit(cue,point,part,normal);
        }
        // Wooden blocks and the core keep the wooden grab; wheels and the drill sound like what they are.
        public static SandboxSoundCue GrabCue(AssemblyPart part) => !part ? SandboxSoundCue.Grab : part.Kind switch {
            PartKind.DownhillSmallWheel or PartKind.DownhillLargeWheel or PartKind.Wheel => SandboxSoundCue.GrabWheel,
            PartKind.DownhillDrill => SandboxSoundCue.GrabDrill, _ => SandboxSoundCue.Grab };
        public static bool IsGrab(SandboxSoundCue cue) => cue==SandboxSoundCue.Grab || cue==SandboxSoundCue.GrabWheel || cue==SandboxSoundCue.GrabDrill;
        void Button(WorkshopButton button)
        {
            if(Level && Level.Course && button && (button.transform.IsChildOf(Level.Course.transform) || button.transform.IsChildOf(Level.transform)))
                Emit(SandboxSoundCue.Button,button.transform.position,button);
        }
        public static float PartSize(AssemblyPart part)
        {
            // Actual renderer bounds include miniature simulation and authored layout scale.
            if(!part) return .12f;
            float size=0;
            foreach(var renderer in part.VisualRenderers) if(renderer) size=Mathf.Max(size,renderer.bounds.size.magnitude);
            return Mathf.Clamp(size,.015f,.6f);
        }
        public void Emit(SandboxSoundCue cue, Vector3 point, UnityEngine.Object source=null, Vector3 normal=default,
            float intensity=1, SandboxSurfaceMaterial material=SandboxSurfaceMaterial.Wood, float size=0, bool silent=false)
        {
            if(!Available) return;
            if(size<=0) size=PartSize(source as AssemblyPart);
            Presented?.Invoke(new SandboxFeedbackEvent(cue,point,normal==default?Up:normal,intensity,size,material,source,silent));
        }
        public void Impact(SandboxSurfaceMaterial material,Vector3 point,Vector3 normal,float speed,AssemblyPart part)
        {
            var bank=Level && Level.Audio?Level.Audio.Bank:null;
            float minimum=bank?bank.MinimumImpactSpeed:.12f, full=bank?bank.FullImpactSpeed:1.5f;
            if(!Running || speed<minimum || Time.unscaledTime<nextImpact) return;
            nextImpact=Time.unscaledTime+.045f;
            var cue=material==SandboxSurfaceMaterial.Rubber?SandboxSoundCue.RubberImpact:material==SandboxSurfaceMaterial.Metal?SandboxSoundCue.MetalImpact:SandboxSoundCue.WoodImpact;
            Emit(cue,point,part,normal,Mathf.Lerp(.18f,1,Mathf.InverseLerp(minimum,Mathf.Max(minimum+.01f,full),speed)),material);
        }
        public void DrillContact(AssemblyPart part,Vector3 point,Vector3 normal)
        {
            if(!Running || Time.unscaledTime<nextDrill) return;
            nextDrill=Time.unscaledTime+.08f;
            Emit(SandboxSoundCue.Drill,point,part,normal,1, SandboxSurfaceMaterial.Metal,0,true);
        }
        public void BindSimulation()
        {
            if(!Level || simulation==Level.SimulationWorld) return;
            simulation=Level.SimulationWorld;
            if(!simulation) return;
            foreach(var part in simulation.Parts.Values) foreach(var body in part.Bodies) {
                var probe=body.GetComponent<SandboxBodyAudio>(); if(!probe) probe=body.gameObject.AddComponent<SandboxBodyAudio>();
                probe.Initialize(Level.Audio,body); probe.Presentation=this;
            }
        }
        public void BeginTransfer(Transform from,Transform to,float gathering,float movement)
        {
            Clear(); if(!Available || !from || !to) return;
            transferring=true; TransferBegan?.Invoke(from,to,gathering,movement); Emit(SandboxSoundCue.Gather,from.position);
        }
        public void CompleteTransfer(Vector3 point,AssemblyPart part=null)
        {
            if(!transferring) return;
            transferring=false; Emit(SandboxSoundCue.Arrive,point,part);
        }
        public void Clear() { transferring=false; nextImpact=nextDrill=0; Cleared?.Invoke(); }
        public static SandboxPresentation For(AssemblyPart part) => part && part.MainBody ? part.MainBody.GetComponent<SandboxBodyAudio>()?.Presentation : null;
    }
}
