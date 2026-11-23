using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAssembly
{
    [DisallowMultipleComponent]
    public sealed class SandboxAudioController : MonoBehaviour
    {
        public SandboxSoundBank Bank;
        public event Action<SandboxSoundCue> CuePlayed;
        public int PlayedCount { get; private set; }
        public int ActiveOneShots { get { int count = 0; foreach (var voice in voices) if (voice.Source.isPlaying) count++; return count; } }
        public float RollingVolume => rolling ? rolling.volume : 0;
        public bool VehicleRunning => level && level.isActiveAndEnabled && level.State == DownhillState.Running
            && !paused && Time.timeScale > 0 && !AudioListener.pause && (!level.Run25D || !level.Run25D.Paused);
        public Vector3 CourseUp => level && level.Course ? level.Course.SandboxFrame.up : Vector3.up;
        sealed class Voice { public AudioSource Source; public SandboxSoundCue Cue; public Transform Follow; }
        readonly List<Voice> voices = new();
        readonly List<SandboxBodyAudio> bodies = new();
        readonly Dictionary<(SandboxSoundCue, int), float> cooldowns = new();
        readonly Dictionary<SandboxSoundCue, int> lastClips = new();
        readonly System.Random random = new(7319); // Audio variation must not consume gameplay's random stream.
        DownhillController level;
        AssemblyWorld world, simulation;
        AudioSource rolling, rattle, drill, water, travel;
        Transform ambienceRoot, waterAnchor, transferFrom, transferTo;
        Vector3 transferStart, transferEnd;
        float transferAge, gatherSeconds, travelSeconds, nextImpact;
        bool transferring, travelling, paused, subscribed;

        public void Initialize(DownhillController owner)
        {
            Unsubscribe(); StopAll();
            level = owner; world = owner.Workshop.World;
            if (!Bank) Bank = owner.Settings.SoundBank ? owner.Settings.SoundBank : Resources.Load<SandboxSoundBank>("SandboxSoundBank");
            if (!Bank) { Debug.LogWarning("Sandbox audio bank missing; run Tools > Sandbox audio > Build sound bank."); return; }
            if (voices.Count == 0)
            {
                for (int i = 0; i < Mathf.Clamp(Bank.OneShotVoices, 1, 16); i++) voices.Add(new Voice { Source = Source("Sandbox cue " + i) });
                rolling = Source("Wheel rumble"); rattle = Source("Axle rattle"); drill = Source("Drill texture");
                water = Source("Creek ambience"); travel = Source("Transfer movement");
            }
            Subscribe();
        }
        AudioSource Source(string label)
        {
            var child = new GameObject(label); child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.dopplerLevel = 0; source.rolloffMode = AudioRolloffMode.Linear;
            return source;
        }
        void Subscribe()
        {
            if (subscribed || !world || !Bank) return;
            if(!level.Feedback) return;
            level.Feedback.Presented += Presented; level.Feedback.TransferBegan += TransferBegan;
            level.Feedback.Cleared += StopAll; subscribed = true;
        }
        void Unsubscribe()
        {
            if (!subscribed) return;
            if(level && level.Feedback) { level.Feedback.Presented -= Presented; level.Feedback.TransferBegan -= TransferBegan; level.Feedback.Cleared -= StopAll; }
            subscribed = false;
        }
        void OnEnable() { Subscribe(); }
        void OnDisable() { Unsubscribe(); StopAll(); }
        void OnDestroy() { Unsubscribe(); }
        void OnApplicationPause(bool value) { paused = value; if (value) StopAll(); }
        void Presented(SandboxFeedbackEvent feedback)
        {
            if(feedback.Cue==SandboxSoundCue.Arrive) CancelTransfer();
            if(!feedback.Silent) Play(feedback.Cue,feedback.Position,feedback.Intensity,feedback.Source?feedback.Source.GetInstanceID():0);
        }
        void TransferBegan(Transform from,Transform to,float gathering,float movement) => BeginTransfer(from,to,gathering,movement,false);
        void Configure(AudioSource source, SandboxSoundEntry entry, bool loop)
        {
            source.outputAudioMixerGroup = Bank.Group(entry.Bus); source.loop = loop;
            source.spatialBlend = entry.SpatialBlend; source.spatialize = entry.SpatialBlend > .5f;
            source.minDistance = Mathf.Max(.01f, entry.MinDistance); source.maxDistance = Mathf.Max(source.minDistance + .01f, entry.MaxDistance);
            source.priority = entry.Bus == SandboxAudioBus.Ambience ? 200 : entry.Bus == SandboxAudioBus.Vehicle ? 160 : 80;
        }
        AudioClip Pick(SandboxSoundEntry entry)
        {
            if (entry.Clips == null || entry.Clips.Length == 0) return null;
            int count = entry.Clips.Length, index = random.Next(count);
            if (count > 1 && lastClips.TryGetValue(entry.Cue, out var previous) && index == previous) index = (index + 1 + random.Next(count - 1)) % count;
            for (int i = 0; i < count && !entry.Clips[index]; i++) index = (index + 1) % count;
            lastClips[entry.Cue] = index; return entry.Clips[index];
        }
        public bool Play(SandboxSoundCue cue, Vector3 point, float gain = 1, int emitter = 0, Transform follow = null)
        {
            if (!Bank || !isActiveAndEnabled || paused || Time.timeScale <= 0 || AudioListener.pause || level && !level.isActiveAndEnabled) return false;
            var entry = Bank.Find(cue); if (entry == null || gain <= 0) return false;
            var key = (cue, emitter); float now = Time.unscaledTime;
            if (cooldowns.TryGetValue(key, out float next) && now < next) return false;
            int concurrent = 0; Voice available = null;
            foreach (var voice in voices)
            {
                if (!voice.Source.isPlaying) { if (available == null) available = voice; }
                else if (voice.Cue == cue) concurrent++;
            }
            if (concurrent >= Mathf.Max(1, entry.MaxVoices)) return false;
            // Important feedback (and a water splash, which ends the run) may replace a quiet vehicle impact, never another interaction/reward.
            if (available == null && (entry.Bus == SandboxAudioBus.Interactions || entry.Bus == SandboxAudioBus.Rewards || cue == SandboxSoundCue.Splash))
                foreach (var voice in voices) if (voice.Cue != SandboxSoundCue.Splash && Bank.Find(voice.Cue)?.Bus == SandboxAudioBus.Vehicle) { available = voice; break; }
            if (available == null) return false;
            var clip = Pick(entry); if (!clip) return false;
            cooldowns[key] = now + Mathf.Max(0, entry.Cooldown);
            available.Source.Stop(); Configure(available.Source, entry, false);
            available.Cue = cue; available.Follow = follow; available.Source.transform.position = point;
            available.Source.clip = clip; available.Source.volume = Mathf.Clamp01(entry.Volume * gain);
            available.Source.pitch = 1 + ((float)random.NextDouble() * 2 - 1) * entry.PitchVariation;
            available.Source.Play(); PlayedCount++; CuePlayed?.Invoke(cue); return true;
        }
        public void Impact(SandboxSurfaceMaterial material, Vector3 point, float speed, int emitter)
        {
            if (!VehicleRunning || !Bank || speed < Bank.MinimumImpactSpeed || Time.unscaledTime < nextImpact) return;
            var cue = material == SandboxSurfaceMaterial.Rubber ? SandboxSoundCue.RubberImpact
                : material == SandboxSurfaceMaterial.Metal ? SandboxSoundCue.MetalImpact : SandboxSoundCue.WoodImpact;
            float gain = Mathf.Lerp(.18f, 1, Mathf.InverseLerp(Bank.MinimumImpactSpeed, Mathf.Max(Bank.MinimumImpactSpeed + .01f, Bank.FullImpactSpeed), speed));
            if (Play(cue, point, gain, emitter)) nextImpact = Time.unscaledTime + .045f;
        }
        public void BindSimulation()
        {
            if(level && level.Feedback) level.Feedback.BindSimulation();
            if (!level || simulation == level.SimulationWorld) return;
            simulation = level.SimulationWorld; bodies.Clear();
            if (!simulation) return;
            foreach (var part in simulation.Parts.Values) foreach (var body in part.Bodies)
            {
                var probe = body.GetComponent<SandboxBodyAudio>(); if (!probe) probe = body.gameObject.AddComponent<SandboxBodyAudio>();
                probe.Initialize(this, body); bodies.Add(probe);
            }
        }
        void Loop(AudioSource source, SandboxSoundCue cue, float amount, float pitch, Vector3 point, float delta)
        {
            if (!source) return;
            var entry = Bank.Find(cue); if (entry == null) return;
            float target = Mathf.Clamp01(amount * entry.Volume);
            if (target > .0001f && !source.isPlaying)
            {
                var clip = Pick(entry); if (!clip) return;
                Configure(source, entry, true); source.clip = clip; source.volume = 0; source.Play();
            }
            source.transform.position = point; source.pitch = Mathf.Clamp(pitch, .5f, 1.6f);
            source.volume = Mathf.MoveTowards(source.volume, target, delta * Mathf.Max(.1f, entry.Volume) * 8);
            if (target == 0 && source.volume <= .0001f) source.Stop();
        }
        public void BeginTransfer(Transform from, Transform to, float gathering, float movement, bool playGather=true)
        {
            StopVehicle(); CancelTransfer(); if (!from || !to) return;
            transferFrom = from; transferTo = to; transferStart = from.position; transferEnd = to.position;
            gatherSeconds = Mathf.Max(0, gathering); travelSeconds = Mathf.Max(.01f, movement); transferAge = 0;
            transferring = true; travelling = false; if(playGather) Play(SandboxSoundCue.Gather, transferStart);
        }
        public void CompleteTransfer(Vector3 point)
        {
            bool active = transferring; CancelTransfer();
            if (active) Play(SandboxSoundCue.Arrive, point);
        }
        void CancelTransfer() { transferring = travelling = false; transferFrom = transferTo = null; if (travel) { travel.Stop(); travel.volume = 0; } }
        public void StopVehicle()
        {
            StopSource(rolling); StopSource(rattle); StopSource(drill);
            foreach (var body in bodies) if (body) body.ResetContact();
        }
        static void StopSource(AudioSource source) { if (source) { source.Stop(); source.volume = 0; } }
        public void StopAll()
        {
            foreach (var voice in voices) { if (voice.Source) voice.Source.Stop(); voice.Follow = null; }
            StopVehicle(); CancelTransfer(); if (water) { water.Stop(); water.volume = 0; }
            cooldowns.Clear(); nextImpact = 0;
        }
        void RefreshAmbience()
        {
            Transform root = level.FactoryLevel ? level.FactoryLevel.transform : level.Course.SandboxFrame;
            if (ambienceRoot == root) return;
            ambienceRoot = root; waterAnchor = null;
            if (water) { water.Stop(); water.volume = 0; }
            float largest = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                foreach (var material in renderer.sharedMaterials)
                    if (material && material.shader && material.shader.name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 && renderer.bounds.size.sqrMagnitude > largest)
                    { waterAnchor = renderer.transform; largest = renderer.bounds.size.sqrMagnitude; }
        }
        void Update()
        {
            if (!Bank || !level || !level.Course) return;
            if (!level.isActiveAndEnabled || paused || Time.timeScale <= 0 || AudioListener.pause) { StopAll(); return; }
            float dt = Time.unscaledDeltaTime;
            foreach (var voice in voices) if (voice.Source.isPlaying && voice.Follow) voice.Source.transform.position = voice.Follow.position;
            BindSimulation(); RefreshAmbience();
            bool reward = level.Reward && level.Reward.Preview && level.Reward.State != SandboxRewardState.Claimed;
            float ambience = reward ? Bank.RewardAmbienceMultiplier : 1;
            if (waterAnchor) Loop(water, SandboxSoundCue.Water, ambience, 1, waterAnchor.position, dt);
            float speed = 0; int supported = 0; Vector3 center = Vector3.zero; Transform spinning = null;
            if (VehicleRunning) foreach (var probe in bodies)
            {
                if (!probe || !probe.Body || !probe.gameObject.activeInHierarchy) continue;
                var rb = probe.Body.Rigidbody;
                if (probe.Wheel && probe.Grounded && !rb.isKinematic)
                { speed += Vector3.ProjectOnPlane(rb.linearVelocity, CourseUp).magnitude; center += rb.position; supported++; }
                if (probe.Drill && probe.Drill.IsSpinning && !rb.isKinematic) spinning = probe.transform;
            }
            if (supported > 0) { speed /= supported; center /= supported; }
            else center = level.SimulationCore ? level.SimulationCore.transform.position : transform.position;
            float intensity = speed < .025f ? 0 : Mathf.Clamp01(speed / Mathf.Max(.01f, Bank.FullRollingSpeed));
            Loop(rolling, SandboxSoundCue.Rolling, intensity, .75f + intensity * .5f, center, dt);
            Loop(rattle, SandboxSoundCue.Rattle, intensity * intensity, .8f + intensity * .4f, center, dt);
            Loop(drill, SandboxSoundCue.Drill, spinning ? 1 : 0, 1, spinning ? spinning.position : center, dt);
            if (transferring)
            {
                transferAge += dt;
                if (transferFrom) transferStart = transferFrom.position;
                if (transferTo) transferEnd = transferTo.position;
                if (transferAge >= gatherSeconds && !travelling)
                {
                    travelling = true;
                    var entry = Bank.Find(SandboxSoundCue.Travel);
                    if (entry != null) { Configure(travel, entry, false); travel.clip = Pick(entry); travel.volume = entry.Volume; travel.pitch = 1; if (travel.clip) travel.Play(); }
                }
                travel.transform.position = Vector3.Lerp(transferStart, transferEnd, Mathf.SmoothStep(0, 1, (transferAge - gatherSeconds) / travelSeconds));
            }
        }
    }
}
