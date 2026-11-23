# Terrain contacts, bridge supports and Quest 3 verification — 2026-11-17

## Artwork

- Updated the five authored scenes and their runtime campaign prefabs. Existing road splines, vehicle physics and progression were retained.
- Closed the main island turf/bedrock seam with a cap fitted to the actual bedrock boundary and a short turf skirt. Using the stone vertices preserves the slope and avoids the previous double green edge.
- Raised partially buried grass, ferns and flowers onto the visible ground. Removed raised clumps when their meshes would obstruct a driving lane; rebuilt the affected static meadow batches.
- Replaced suspended road foundations with world-vertical timber piles, knee braces, cross bearers and partially submerged stone footings. Added timber seats and metal fittings at road ends.
- Lifting gantry columns stop at the stationary bridge deck, with metal shoes and cross seats. Ordinary road trestles provide the supports below. Upper/lower road crossings use the correct deck elevation.
- Added narrow, softly animated ivory foam and ripples along static water/opaque-mesh intersections. The baked ribbon meshes use one material per water surface. No camera depth prepass or opaque-texture copy was enabled. Moving objects are not dynamically outlined by this authored foam pass.
- Reoriented Level 5's distant islands and cloud bank to its actual Player Start. The former backdrop was approximately 94 degrees left of the starting direction. The initial road-bounds water expansion was rejected during visual review: it extended outside the display tray and sat below its inner floor. The final water polygon follows the convex inner floor outline, sits 6 mm above its highest point, and remains below the tray rim. Its stone edge, supports and foam were regenerated at that height.

## Editor verification

- EditMode: 20/20 relevant environment and scenery-motion checks passed after the gantry and fifth-course changes. The subsequent turf-only adjustment was visually checked against the bedrock boundary.
- PlayMode: 3/3 passed, covering the real bridge release/reset animation and repeated reference-car runs through Levels 4 and 5 (five runs each).
- The correct Level 5 drill reference car is `Generated/LevelFactory/Vehicles/Level 5 drill car.asset`; the legacy default factory test car is a different fixture.
- Visual evidence: `Logs/ArtPolish-20261117/Contact-Turf-Verified-4.png`, `Contact-Player-Final-5.png`, and `L5-Player-Before.png`. The player review uses the published Player Start, a 75-degree vertical field of view and the pitch named by the capture; it is an editor composition check, not a headset capture.
- Pre-edit scene/prefab backups are in `Logs/ContactPolish-20261117/Before/`.

## Quest performance capture

The development-only `QuestEnvironmentPerf` runner is explicitly enabled by the Android launch extra `metahackthone.environment_perf=true`. Normal launches retain the normal game flow. It warms up each level, samples 20 seconds in build mode and 30 seconds with actual reference-car physics, then advances to the next level. It records FPS, frame durations, display refresh, ASW state, focus, user presence and eye texture dimensions. Raw OVR CPU/GPU values are labelled raw rather than assuming their units.

The APK includes a copy of the correct fifth-course car at `Resources/EnvironmentPerfLevel5.asset`. Recreate this resource from the reference car before rebuilding the diagnostic if it has been removed from the project.

Device: connected Quest 3, Android 14. The independent development app `com.metahackathon2026.metahackthone.environmentperf` was installed alongside the existing app; the existing app and its data were preserved. Project package settings were restored after building.

The first attempt captured only about 19 seconds of Level 1 build mode with headset presence/focus confirmed: roughly 65–81 FPS on a 90 Hz display. This is **not a completed five-level benchmark**. The reference-car loading step stopped progression; the user also reported pressing Start manually. The user then explicitly stopped performance work. The diagnostic app and capture were stopped, and no new headset run was started.

Evidence: `Logs/ContactPolish-20261117/Quest3-first-attempt.csv`. The installed APK predates the subsequent floating scenery / continuous water repairs. Do not use its visuals to judge the current project changes. Future testing must first address reference-car loading and manual Start interaction.


## Subsequent placement review

- Restored the lowered Level 1 terrace from its original limestone/moss meshes and applied a monotonic vertical compression. The former smoothstep subtraction folded lower rock faces. The landing authoring path now uses the same monotonic mapping.
- Grounded editable botanical sources against visible terrain, excluding the inactive pre-edit caps, and rebuilt their render batches. Canopy anchors use the highest visible surface; low meadow plants retain slope-aware placement. Mesh replacement explicitly refreshes GPU geometry buffers as well as serialized mesh data.
- Flattened and welded the imported river sheets, removing degenerate/duplicate triangles. Ripple relief stays in shading, so disconnected imported faces no longer separate vertically. The noise itself uses continuous spatial coordinates.
- Retimed clouds, river, waterfall, impact and spray with an unscaled environment clock. Small car physics time scales no longer freeze environmental animation. Cloud body/shell share the same gentle parameters.
- Repositioned both waterfall sheets in Levels 1–4 against real banks, checking the entire drop against limestone, the spring against road clearance, and visibility from the published player start. Roads may cross over a spring, but at least three of five samples along the falling sheet must remain visible. Trimmed meadow/ivy intersecting the falling-water volume and moved the impact/spray to the actual water height.
- Moved Level 2's start-island pennant about 12 cm within the platform to clear the driving corridor.
- Corrected the clearance audit to test static mesh triangles rather than empty space inside a combined renderer's bounding box. GPU foliage checks conservatively expand triangle tests by the maximum shader wind displacement; rotating props retain swept bounds.
- Player-side and elevated reviews were inspected for all five courses. Level 5 evidence: `Logs/ArtPolish-20261117/Audit-Level5-Contained.png`. Level 2–4 evidence: `Audit-Final-LevelN.png` and `Audit-Top-LevelN.png` in the same folder. These are Editor captures, not Quest captures; editor-only overlays are not presentation assets.
- Level 1's Save to Game command checks the active scene path and cannot export another level as Level 1. No cross-level overwrite was found after the reported accidental click.


Final verification for the placement pass: 33/33 EditMode checks passed (job `e2940cb57b5d4b419c5983ac73fc4685`), including all five road-clearance cases, bridge/road end support, barrel material batches, grounded trees, continuous water geometry and Level 5 tray containment. The earlier environment clock / real bridge PlayMode run passed 3/3 (`f53ebce08be34546b1470eeb099a4181`). No headset performance run or APK update followed the user's stop request.

Final saved-scene previews: `Logs/ArtPolish-20261117/Placement-Verified-Level1.png` through `Placement-Verified-Level5.png`. These hide the building interface only for the preview, restore its state afterwards, and reload Level 1 without saving preview changes. Source scenes and campaign prefabs both contain the placement repairs.

## Tray seal and manual capture revision

The user subsequently requested a new performance build after another art correction. This supersedes the earlier stopped test; the new capture never starts or retries the car automatically.

- Fitted all five water sheets to the eight actual steel rim segments, 4 mm below the lowest rim cap in mesh-local coordinates. Added an opaque inner basin wall reaching below the surface. The lining is outside the display-base LOD group. This replaces the earlier floor-hull method, which still left an edge gap.
- Adjusted waterfall bottoms, impact spray, contact foam and road footings to the corrected water elevation. Kept rock-fissure sources without upper water pools.
- Grounded the decorative barrels/crates on visible terrain and searched for placements clear of waterfall-source rocks and driving lanes. Rebuilt their batches with the existing wood/metal materials. Did not change tree or bush height.
- Replaced Player Start camera-preview Standard materials with `PlayerViewMarker` (URP/Lit, slate blue), including the authoring path so new markers cannot regress. The marker remains EditorOnly and is disabled by SandboxPlayerStart at runtime.
- Inspected all five saved scenes from Player Start and a low tray-side angle: `Logs/ArtPolish-20261117/TraySeal-LevelN-Player.png` and `TraySeal-LevelN-Edge.png`.
- 19/19 EnvironmentPolish EditMode tests passed, including five rim/lining/marker checks (job 26ecedfbf1a641e7973e205cd3060bfd).

Manual diagnostic flow: cold launch with `metahackthone.environment_perf=true`; select LEVEL 1-5 using the existing VR ray/poke buttons, inspect, press START yourself, QUIT to the level menu, FINISH TEST to close the capture. Each session writes timestamped samples.csv, events.csv and metadata.txt under the diagnostic app's EnvironmentPerf directory. Records actual state/run number, frame percentiles, focus/presence, native OVR CPU/GPU metrics, viewport/resolution/refresh/ASW, Unity main/render-thread durations, draw/SetPass/triangle counts and memory/allocation counters. Unsupported counters remain empty. Main/render thread wall time includes waits; use Perfetto for attribution. Editor flow measurements are not Quest FPS results.

Further verification: 18/18 SceneryClearance EditMode tests passed (job 1b5ade0050ee438ab42f21a77bd6f10b). The manual VR-menu PlayMode integration test passed through all five levels: no auto-start, explicit START reaches Running, QUIT restores the visible level menu, FINISH writes samples from all five (job db2834c740a546558c439f9c49f346a1). Runtime LOD0 and LOD1 captures for each level were inspected (`RuntimeTray-LevelN-LOD0/1.png`); the inner wall stays active outside either LOD and spans the water height. These are desktop runtime checks, not a substitute for the upcoming headset measurements.

New Android build succeeded and was installed over the diagnostic app only (`com.metahackathon2026.metahackthone.environmentperf`); original application identity was restored in PlayerSettings. APK: `Builds/MetaHackthone-ManualPerf.apk`, SHA256 `9F94815DF7CFCB08B7E9E74B41BD19B8342700B8B707B069D575BB6692FA41AE`. Headset launch was requested with the manual-capture extra; Horizon OS reports a system dialog blocking the launch. Awaiting user dismissal/wear before collecting valid Quest performance samples.

## Completed Quest 3 manual playtest (local date 2026-11-17)

The user wore the Quest 3 and manually selected all five levels, pressed START, returned with QUIT, and pressed FINISH TEST. The capture ends with DONE. Six driving attempts succeeded: Level 1 twice; Levels 2-5 once each. Session duration 397 seconds; 374 CSV windows saved. The original application was not replaced. No automated car starts/retries were used in the headset session.

These measurements precede the focus/celebration revision below; they are not FPS results for the revised artwork.

Settings observed: Vulkan, 90 Hz panel, ASW metric 0, eye textures 2496 x 2208, eye scale 1.2545, viewport scale 1.0. This is a Development build, with intermittent Perfetto tracing, not a release-build performance certification. The user's freely changing view is part of these measurements.

FPS below is total frames / summed window duration. Excluded the first eight seconds after each level became ready, unworn/unfocused windows, loading, countdown, transitions, success/failure screens and menu. Each window must contain at least ten frames. These are short playtest averages, not sustained benchmarks.

| Level | Build/observation FPS (valid seconds) | Driving FPS (valid seconds) |
|---|---:|---:|
| 1 | 74.0 (53.0 s) | 66.9 (4.2 s, two runs) |
| 2 | 65.9 (73.5 s) | 64.6 (4.6 s) |
| 3 | 66.1 (7.4 s) | 60.5 (5.5 s) |
| 4 | 66.0 (8.1 s) | 59.9 (3.5 s) |
| 5 | No post-warmup build window | 77.5 (5.4 s) |

Level 5 was started before the observation warmup ended; its later driving samples are valid. Do not fill the missing observation result with success-screen FPS. The 90 Hz budget is 11.11 ms; these app-frame averages do not maintain it.

### Confirmed cost and attribution limits

- Levels 1-4 observation GPU utilization averages approximately 94.9%, 96.8%, 97.1%, 97.4%; Level 4 driving averages 97.9%. GPU headroom is limited. Level 5 driving averages 76.9%.
- Unity-reported observation triangle counts for Levels 1-4 are about 612k/646k/678k/668k per frame, compared with about 219k while driving Level 5. Levels 3/4 show about 370 draw calls while observing and 342/357 while driving. This supports prioritizing scene geometry/rendering investigation; it does not isolate a particular tree, water shader or cloud effect.
- Level 1's available CPU marker window covers 6.005 seconds / 442 LateUpdate markers. Average `PreLateUpdate.ScriptRunBehaviourLateUpdate` is 6.252 ms, rendering submission (`PostLateUpdate.FinishFrameRendering`) 2.500 ms, regular script Update 1.425 ms; physics about 0.494 ms per physics-step marker. The largest identified CPU phase is LateUpdate. Its script methods are not individually named in this capture, so no specific component can yet be blamed.
- The Level 2 GPU trace has device-wide counters: fragment shading time about 61.9%, vertex shading about 37.7%, vertex-fetch stalls about 31.8%, texture-fetch stalls about 11.3%. These counters include compositor/system work and have no attributable application colour passes; they are supporting clues only, not additive per-effect cost percentages.
- The trace analyzer incorrectly defaulted to 72 Hz because its refresh counter was missing. This report uses the explicit 90 Hz recorded by the application. Its GPU time of 0 means unavailable, not zero GPU work. Unity's `Render Thread` recorder is also unavailable and remains empty; main-thread wall duration includes waits.
- To identify the exact biggest component/effect, the next measurement should add named markers around relevant LateUpdate components and perform controlled same-view A/B visibility tests for foliage, clouds and water. Current evidence justifies both CPU script and GPU rendering work; it does not justify declaring water alone the culprit.

Raw data: `Logs/ArtFixes-20261117/Quest3-Manual/{samples.csv,events.csv,metadata.txt,summary.json}`. Trace analyses: `Quest3-Level1-Trace.json`, `Quest3-Level2-GPU-Trace.json`, `Quest3-Levels3To5-CPU-Trace.json` in the parent folder. The latter spans returning from Level 3 and loading/playing Level 5, so it is not a steady Level 3 benchmark. Perfetto originals remain in `C:/Users/ruizh/AppData/Roaming/odh/traces/manual-art-perf-*.pftrace`.

## Focus, water and victory revision (2026-11-17)

- MainMenu and Workshop now share a session-only initial head pose. Returning while looking toward QUIT, including a Single-mode menu scene reload, preserves the original menu/course heading. Explicit play-area placement still takes precedence. Authored side-on PlayerStart viewing angles were not changed.
- Replaced grid/value-noise foam masks on Living Water with continuous, derivative-filtered harmonic streaks. Retained float world position and phase calculations. Contact foam bands are slower and filtered. No new depth texture, opaque texture or screen-space blur pass was introduced. Desktop shader checks passed; Quest appearance requires a new worn-headset check.
- All five scene and runtime prefab environments now contain a single curved sky veil behind the scenery, 96 triangles, one unlit draw. Its alpha gradient and feathered edges reduce passthrough contrast through background gaps while leaving some room visibility. This is translucent atmospheric masking, not a camera-image blur. It is anchored to the scene rather than the head.
- Level 5 has 48 additional supported flower/grass plants across its two terraces, excluding road clearance and the crowd area. Editable sources are hidden and combined into six material batches (46,218 added triangles). This is an artwork addition, not a measured performance improvement.
- Success starts five staggered firework bursts and falling/tumbling coloured confetti across the course. Two unlit particle batches, capped at 160 sparks + 96 paper pieces, no particle lights/shadows/collision/trails. Reward transfer allocation no longer cancels the celebration. Reset/exit/pause clears it; replay works. Crowd jump heights and overhead arm poses are larger; standalone diagnostic wins now trigger cheering even without the campaign car-return sequence.
- Verification: 25 environment checks and 10 crowd checks passed (the planting check was corrected to select visible support below each plant where terraces overlap in XZ). PlayMode checks passed for three repeated left-facing menu reloads and stable course placement, celebration replay/cleanup and transfer coexistence, and ordinary VFX transfer budget/pause cleanup. Four modified/new shaders reported no compilation messages.
- Desktop previews: `Logs/ArtPolish-20261117/Victory-2seconds.png`, plus the revised Level 1 and Level 5 tray/player previews. Android package target: `Builds/MetaHackthone-FocusPerf.apk`. The earlier FPS table must not be presented as a measurement of this revision.

Build/install outcome: Android Development build succeeded with 0 errors; SHA256 `7475D1CFE72F8014DB20A15041E2C3D9933BD9D1C30FF15AB24E6835AFAE665D`. Installed over `com.metahackathon2026.metahackthone.environmentperf` on Quest 3 `2G0YC1ZG42020Y`; package last-update time confirmed. Restored original product name and application identifier in PlayerSettings. Sent launch with the manual-test intent extra. Also inspected refreshed Level 2, 3 and 4 PlayerStart previews, completing all five views for this revision. No new worn-headset FPS result has been collected.
Headset launch is currently held at the Horizon OS profile lock screen (confirmed by ui_dump); the diagnostic process is not running yet. User must select/unlock their profile before headset validation. Installation is confirmed, gameplay launch is not.
