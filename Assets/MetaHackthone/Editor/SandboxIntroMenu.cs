using UnityEditor;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    // Review helpers for the first-entry intro. The intro itself is all runtime code: see SandboxFirstEntryIntro.
    public static class SandboxIntroMenu
    {
        [MenuItem("MetaHackthone/Cinematic/Reset First Entry Intro")]
        public static void ResetSeen() { PlayerPrefs.DeleteKey(SandboxFirstEntryIntro.SeenKey); PlayerPrefs.Save(); Debug.Log("First-entry intro will play on the next campaign entry to Level 1."); }

        [MenuItem("MetaHackthone/Cinematic/Play Intro Now")]
        public static void PlayNow()
        {
            var workshop=Object.FindFirstObjectByType<WorkshopBootstrap>();
            if(!Application.isPlaying || !workshop) { Debug.LogWarning("Enter Play mode in the campaign's Level 1 (DownhillSandbox) first."); return; }
            ResetSeen(); SandboxFirstEntryIntro.Suppressed=false; SandboxFirstEntryIntro.TryBegin(workshop);
            if(!SandboxFirstEntryIntro.Playing) Debug.LogWarning("The intro only plays on an untouched Level 1 in campaign mode.");
        }
        [MenuItem("MetaHackthone/Cinematic/Play Intro Now",true)] static bool CanPlayNow() => Application.isPlaying;
    }
}
