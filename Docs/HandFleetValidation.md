# HandFleet recovery validation - November 7, 2026

## Source and integration

Recovered HandFleet Classic from Unity Version Control shelves 9 and 10, created November 4, 2026. All 251 files common to these shelves were byte-identical. The original shelves remain available.

Restored `Assets/HandFleet` with its original GUIDs, art, audio, scenes, authoring tools and tests. Adapted the shelf's shared navigation changes onto main changeset 80, preserving the current Sandbox campaign reset/resume behavior and current parameterless `EnterSandbox()` API. Unrelated shelved launchers and developer-agent settings were not imported.

The main menu now offers **HANDFLEET** beside **WORKBENCH BUILD**. HandFleet is appended to the game-mode enum and enabled build scenes; MainMenu remains first. Explicit shadow-scene classification prevents HandFleet from being treated as a shadow puzzle. The inspection scene stays outside the player build.

## Current automated evidence

Unity 6000.3.21f1 compiled the restored assemblies successfully.

| Check | Result | Local evidence |
|---|---|---|
| HandFleet EditMode suite | 20 passed, 0 failed | `Logs/HandFleetRecovery/EditTests.xml` |
| HandFleet PlayMode suite | 6 passed, 0 failed | `Logs/HandFleetRecovery/PlayTests.xml` |
| Main-menu PlayMode regression suite | 7 passed, 0 failed | `Logs/HandFleetRecovery/PlayTests.xml` |

PlayMode tests ran with graphics enabled and Android selected as the build target. They verify scene art and supported materials, one shared player and audio listener, direct entry, repeated navigation, ray and poke selection through the actual HANDFLEET button, return through HandFleet's MAIN MENU control, scene unloading, camera/passthrough restoration, all seven combat encounters, retry, and tracking-loss recovery. Existing modes still round-trip; Workshop and Sandbox constructions survive menu navigation.

The first run exposed two outdated test assumptions: HandFleet's half-second entry input guard must expire before selecting return, and the current Sandbox example has different part counts from the older example. The return check now waits for that guard; the Sandbox check compares actual part and connection identities before and after navigation. All 13 PlayMode tests passed on the final run.

## Limits

No new APK was built or installed during this recovery. No headset interaction or performance measurement was performed. Real hand tracking, palm-contact fusion, stereo comfort, fatigue, and sustained headset performance still require wearer testing. Older shelf validation described an earlier prototype and is not evidence for this integration.
