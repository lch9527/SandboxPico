# HandFleet gesture campaign

The HANDFLEET main-menu entry now opens the gesture campaign. The existing shared player rig and presentation restoration are retained. The previous Classic mission, settings, art, serialized enemy IDs, and score records remain available as legacy assets.

## Controls

Calibrate in a relaxed fingers-forward pose. Calibration centers the ship in the flight volume. Move the controlling hand to steer; aim along the fingers. Choose either hand in the paused console.

- Fingers outward: attack. Fire only when an enemy is acquired within the 8-degree aiming cone; retain the target to 12 degrees. Shots are immediate, 20 per second, with visible tracers. Intervening enemies intercept shots.
- Back of hand outward: shield. Absorb projectiles for energy. Three seconds of continuous use breaks the shield; four seconds of cooldown follows. Overload does not damage the hull. Ramming and the flight boundary bypass the shield.
- Palm outward: ultimate. Requires more than 80 energy, drains 25 per second, and stops when the gesture changes or energy reaches zero. The beam destroys ordinary enemies and intersecting projectiles; Gatekeeper receives 160 DPS. Laser kills do not create energy drops.
- Desktop: mouse movement steers, wheel changes depth, right-drag aims, 1/2/3 select attack/defense/ultimate, P or Escape pauses, and left-click selects console controls.

Normal kills drop ten energy, collected by touching the drifting pickup. Shield absorption awards at most ten energy per second. Three unblocked hits end a run. Crossing the visible spherical flight boundary during combat ends it immediately. Practice is nonlethal and starts with 90 energy to demonstrate the laser; combat starts at zero energy.

Fusion is absent from the gesture campaign. Pinching selects menus and does not fire the laser. Pausing, focus loss, missing hands, and input discontinuities suspend combat; recovery rebases the hand and requires a newly recognized gesture.

## Campaign and tuning

`Assets/HandFleet/Data/GestureCampaign.asset` contains a 25-second practice, the document's four ordered level group lists, five-second rests, and Gatekeeper. Combat groups use full starting-health costs against 2.5/5/7.5/10 seconds of baseline DPS. The next group is admitted below 25% remaining budget; individual spawns wait for capacity and are never skipped. There are 164 authored enemies across the four levels, plus three practice enemies and Gatekeeper. Carrier followers are additional bounded summons.

The new families are Sentry, Grenadier, Sniper, Frigate, Battleship, Carrier, and Follower. Their starting health corresponds to 0.5/1.5/1/2/4/6/0.25 seconds at 20 DPS. Carriers release three followers, with limits of six per carrier and twelve globally. Enemies' existing bullets persist after destruction.

`GestureFlightSettings.asset` owns aiming, firing, energy, shielding, spawn pacing, and flight tuning. `GestureArt.asset` references separate enemy prefab variants and retains the original player and boss art. Runtime presentation adds the enclosing chamber, damage glow, speed-colored projectiles, target marker, ship HUD, rear-threat cues, and lethal-boundary warning. Current defaults reserve 512 hostile projectile slots, 32 enemy slots, and 64 energy-drop slots.

Records use `HandFleet.Gesture.v1.Best`; previous Classic records are unchanged. The old **Author Classic assets** command intentionally restores Classic authoring and should not be used to rebuild this campaign.

## Validation and remaining acceptance

On November 8, 2026, the upgraded assembly passed 28 EditMode tests, six HandFleet PlayMode tests, and seven shared-menu PlayMode regressions. These cover gesture dwell, targeting, shield overload/absorption, energy threshold, laser interception, persistent bullets, fatal boundaries, spawn-budget deferral, full campaign/retry, scene art, shared rig ownership, and menu transitions. Desktop renders are saved under `Logs/HandFleetGesture`.

The November 8 Android ARM64 IL2CPP development build succeeded with zero errors (37 warnings) and was installed over the existing app on USB Quest 3 `2G0YC5ZG5V04ZW`, preserving app data. The APK is `Builds/HandFleetGesture-Quest.apk` (132,406,747 bytes). Launch logs confirm XR initialization and rendered frames; no Unity exceptions or fatal errors were found in the captured launch log.

The build required temporarily embedding the existing Pico SDK under `Packages/com.unity.xr.openxr.picoxr` to repair its script associations, and initializing its null platform app ID to an empty string. The original SDK files and manifest were preserved; the temporary copy is archived under `Library/HandFleetQuestSdk`. The original external package location is restored after building. Configuration backups and launch evidence are under `Logs/HandFleetGesture`. Repeat that package-staging workaround if the external SDK again loses its script associations.

Real-hand gesture reliability, palm-normal behavior on both hands, stereo comfort, arm fatigue, and sustained Quest CPU/GPU performance remain unverified. The intended refresh target is 90 Hz, not a measured result. The development smoke-test launch extra remains `metahackthone.handfleet_smoke_test=true`; its input/workload now targets the gesture campaign and never saves synthetic scores.

## PICO build

**MetaHackthone > HandFleet > Build PICO APK** builds HandFleet alone for PICO 4 / 4 Ultra as `Builds/HandFleet-Pico.apk` (package `com.metahackathon2026.handfleet`, name HandFleet, 90 Hz). **Build PICO APK and run** also installs it on every connected adb device that reports a PICO manufacturer (an attached Quest is left alone) and launches it; **Install last PICO APK and run** skips the build. A development variant is also in the menu. With the editor closed: `Unity.exe -batchmode -quit -projectPath . -buildTarget Android -executeMethod HandFleet.Editor.HandFleetPicoBuild.BuildFromCommandLine` (add `-development` and/or `-install`).

The builder wraps the shared `PicoBuild.Prepare` / `Restore`: the PICO OpenXR features and the portable UnityXR player go in for the build and the Quest configuration, package id, product name and open scenes come back afterwards, even when the build fails. The shared build list is not changed. The active build target must be Android. In this APK the console's MAIN MENU button reads HOLD TO QUIT (a one-second pinch hold), because there is no main menu. In the full game's PICO build HandFleet runs from MainMenu as before.

PICO specifics in the integration: the hand reader skips the UnityXR rig's static prefab hand, and HandFleet pauses PICO see-through while its opaque chamber is shown. Meta's MRUK package starts a per-frame updater (`MRUKGlobalContext`) in every build; on PICO it found no OVRPlugin and threw every frame, so `MetaOnlyRuntimeGuard` switches it off at startup wherever OVRPlugin cannot load.

## Flight console (XR)

The console faces the eye from about 1 m away, its panel centre about 11 degrees under the eye line (`HandFleetEntry.ConsolePose`), placed from the head pose at launch. Visible controls flow in one row (arcade: PALETTE, FLASHES, VOLUME); the exit is on the title row, top right. Hint text wraps to the panel. VOLUME is a slider: pinch it and sweep sideways (5% steps with a preview sound), release to save. Console clicks are ignored while a hand is within 25 cm of the parked ship, so a grab pinch cannot press a button behind it. A pinch on one hand no longer waits for the other hand to open. Every press logs `HANDFLEET_BUTTON <label>`.
