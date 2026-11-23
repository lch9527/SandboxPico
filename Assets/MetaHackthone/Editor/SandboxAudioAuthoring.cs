using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace PhysicalAssembly.Editor
{
    public static class SandboxAudioAuthoring
    {
        const string Clips = "Assets/MetaHackthone/Audio/Sandbox/";
        const string BankPath = "Assets/MetaHackthone/Resources/SandboxSoundBank.asset";
        const string MixerPath = "Assets/MetaHackthone/Audio/Sandbox/Sandbox.mixer";
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [MenuItem("Tools/Sandbox audio/Build sound bank")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(Clips, "*.wav"))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings; importer.forceToMono = true;
                importer.loadInBackground = false; importer.SaveAndReimport();
            }
            var bank = AssetDatabase.LoadAssetAtPath<SandboxSoundBank>(BankPath);
            if (!bank) { bank = ScriptableObject.CreateInstance<SandboxSoundBank>(); AssetDatabase.CreateAsset(bank, BankPath); }
            // Preserve hand-tuned entries and replacements when rebuilding.
            var entries = bank.Sounds.ToList();
            foreach (SandboxSoundCue cue in Enum.GetValues(typeof(SandboxSoundCue)))
            {
                if (entries.Any(entry => entry != null && entry.Cue == cue)) continue;
                var entry = new SandboxSoundEntry { Cue = cue };
                entry.Clips = Directory.GetFiles(Clips, cue + "*.wav").OrderBy(path => path)
                    .Select(path => AssetDatabase.LoadAssetAtPath<AudioClip>(path.Replace('\\', '/'))).ToArray();
                if (entry.Clips.Length == 0 || entry.Clips.Any(clip => !clip)) throw new InvalidOperationException("Missing audio: " + cue);
                entry.Bus = cue >= SandboxSoundCue.WoodImpact && cue <= SandboxSoundCue.Drill ? SandboxAudioBus.Vehicle
                    : cue == SandboxSoundCue.Water ? SandboxAudioBus.Ambience
                    : cue >= SandboxSoundCue.Success && cue <= SandboxSoundCue.Claim ? SandboxAudioBus.Rewards : SandboxAudioBus.Interactions;
                entry.Volume = cue switch
                {
                    SandboxSoundCue.Grab => .12f, SandboxSoundCue.Attach => .42f, SandboxSoundCue.Detach => .23f,
                    SandboxSoundCue.Reject => .16f, SandboxSoundCue.Button => .17f, SandboxSoundCue.WheelTick => .14f,
                    SandboxSoundCue.Rolling => .2f, SandboxSoundCue.Rattle => .075f, SandboxSoundCue.Drill => .13f,
                    SandboxSoundCue.Water => .09f, SandboxSoundCue.Success => .32f, SandboxSoundCue.Failure => .18f,
                    SandboxSoundCue.WoodImpact => .35f, SandboxSoundCue.RubberImpact => .28f, SandboxSoundCue.MetalImpact => .25f,
                    SandboxSoundCue.GrabWheel => .16f, SandboxSoundCue.GrabDrill => .16f, SandboxSoundCue.DrillBreak => .5f,
                    _ => .24f
                };
                entry.Cooldown = cue == SandboxSoundCue.WheelTick ? .035f : cue == SandboxSoundCue.Reject ? .25f : .08f;
                entry.MaxVoices = entry.Bus == SandboxAudioBus.Vehicle ? 4 : cue == SandboxSoundCue.Attach ? 3 : 2;
                if (cue == SandboxSoundCue.Success || cue == SandboxSoundCue.Failure || cue == SandboxSoundCue.Countdown || cue == SandboxSoundCue.Go)
                    entry.SpatialBlend = .25f;
                if (cue >= SandboxSoundCue.Countdown && cue <= SandboxSoundCue.Claim) entry.PitchVariation = 0;
                if (cue == SandboxSoundCue.Water) { entry.MinDistance = 1; entry.MaxDistance = 5; }
                entries.Add(entry);
            }
            bank.Sounds = entries.ToArray();
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (!mixer) mixer = CreateMixer();
            bank.Interactions = mixer.FindMatchingGroups("Interactions").Single();
            bank.Vehicle = mixer.FindMatchingGroups("Vehicle").Single();
            bank.Ambience = mixer.FindMatchingGroups("Ambience").Single();
            bank.Rewards = mixer.FindMatchingGroups("Rewards").Single();
            EditorUtility.SetDirty(bank); AssetDatabase.SaveAssets();
            Debug.Log("SANDBOX_AUDIO_BANK_READY " + bank.Sounds.Length + " cues");
        }

        // Unity exposes mixer creation only through its editor controller, not the runtime AudioMixer API.
        static AudioMixer CreateMixer()
        {
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var controllerType = assembly.GetType("UnityEditor.Audio.AudioMixerController", true);
            var effectType = assembly.GetType("UnityEditor.Audio.AudioMixerEffectController", true);
            var mixer = (AudioMixer)controllerType.GetMethod("CreateMixerControllerAtPath", Flags).Invoke(null, new object[] { MixerPath });
            var master = controllerType.GetProperty("masterGroup", Flags).GetValue(mixer);
            var snapshot = controllerType.GetProperty("TargetSnapshot", Flags).GetValue(mixer);
            master.GetType().GetMethod("SetValueForVolume", Flags).Invoke(master, new[] { (object)mixer, snapshot, -6f });
            foreach (var name in new[] { "Interactions", "Vehicle", "Ambience", "Rewards" })
            {
                var group = (AudioMixerGroup)controllerType.GetMethod("CreateNewGroup", Flags).Invoke(mixer, new object[] { name, false });
                controllerType.GetMethod("AddChildToParent", Flags).Invoke(mixer, new[] { (object)group, master });
                if (!AssetDatabase.Contains(group)) AssetDatabase.AddObjectToAsset(group, mixer);
                var effectsProperty = group.GetType().GetProperty("effects", Flags);
                var effects = (Array)effectsProperty.GetValue(group);
                if (effects.Length == 0)
                {
                    var effect = (UnityEngine.Object)Activator.CreateInstance(effectType, Flags, null, new object[] { "Attenuation" }, null);
                    AssetDatabase.AddObjectToAsset(effect, mixer);
                    var array = Array.CreateInstance(effectType, 1); array.SetValue(effect, 0); effectsProperty.SetValue(group, array);
                }
                EditorUtility.SetDirty(group);
            }
            EditorUtility.SetDirty(mixer); AssetDatabase.SaveAssets(); return mixer;
        }
    }
}
