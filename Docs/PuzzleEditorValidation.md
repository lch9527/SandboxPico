# Puzzle Editor validation — 2026-11-07

Project: Unity 6000.3.21f1, `MetaHackthone`, Android build target.
Entry scene: `Assets/MetaHackthone/Scenes/FactoryFinalRun.unity`.

## Automated checks

| Check | Result |
| --- | --- |
| Edit Mode: FactoryPuzzleTests, FactoryAuthoringTests, Level2BezierTrackTests, AuthoredSandboxTests | 28/28 passed |
| Play Mode: DesktopTests, DesignerSandboxTests, MetaGrabInputTests, GrabLifetimeTests, DownhillWheelTests, DownhillInteractionTests, BuildingSceneRuntimeTests, CarLoadTests | 75/75 passed |
| Combined course using Combined wide high drill preset | 5/5 reached Goal upright |
| Integrated Retry reset | 10/10 restored vehicle, target, chain and raised deck |

Raw results are in `Logs/PuzzleEditor/EditModeResults.json`,
`PlayModeResults.json` and `CombinedCourseRuns.txt` (local validation artifacts).
Edit Mode job: `e5ba8e05468f4c379fa1bbc94654e4b2`.
Play Mode job: `0a0354cde4a84ef7b314f20029015658`.

Editor checks cover blank-template creation, independent settings/car assets,
first-track start binding, spline edits, duplicate/delete, parameter Undo/Redo,
scene save/reopen, persistent meshes and mechanism references. Crab checks
raycast five positions through the actual center hole and both lanes; deletion
restores the original road. Overlapping cutouts and invalid spans are rejected.

Bridge tests simulate its actual hinge/tether/gravity at scales 1 and 0.21,
including a translated presentation and ten resets per scale. Low drill sweeps
and ordinary body contact cannot release it. The saved wide, narrow and
asymmetric assemblies are restored through the existing save/socket system
and simulated five times each. Wide builds retain tire support; narrow builds
lose support. An asymmetric build can catch/scrape on its beam rather than
fall completely, so the test checks missing tire support instead of imposing
an artificial failure outcome.

Desktop input tests send mouse/keyboard events, verify roll, pitch, yaw and
socket attachment, and retain the existing camera/focus checks. Drag-plane
tests cover scroll depth retention across frames. Legacy sandbox tests were
updated to the existing physics-tick wheel motion, confirmed-open release
after tracking loss, animated attachment timing, and palm-menu action layout.
The VR input implementation was preserved.

## Combined course and resets

The recorded five runs use the saved sample: downhill, Roller, high drill
target and falling bridge, descending hairpin, open-center Crab Bridge, Goal.
Each bridge released through actual drill contact. Finish up-vector dots were
0.99719–0.99735. The first launch used normal playback; subsequent retries used
the existing faster playback control with the physics step unchanged.
No guide force or puzzle-specific success rule was used.

Ten integrated Retry cycles preserved 14 vehicle rigid bodies and 9 connections.
Each reset restored the active target, raised kinematic deck and exactly two
bridge joints, with Released false. Returning to Build retained all 10 parts
and 9 connections, and the captured assembly passed save validation.

The sample's road friction and gentle turn are deliberate physical tuning for
the existing passive, fixed-axle vehicle. Different layouts and cars still need
designer playtesting; there is no guarantee that arbitrary dimensions work.

## Quest 3

Development APK build succeeded in 170.7 seconds with zero errors and four
warnings. Artifact: `Builds/FactoryQuestTest.apk`, 197,156,694 bytes.
SHA-256: `7A2A377E304D84FBA891DB12AA3E95935CFB425234A155C4FB2F73B67C3B7326`.
The build contains the saved Final Run scene and uses the existing Meta OpenXR
configuration. It does not change the project's normal build-scene list.

Installed with data-preserving replacement and launched on Quest 3
`2G0YC5ZG5V04ZW`, Android 14, through explicit target `192.168.8.106:5555`.
The updated package is `com.metahackathon2026.metahackthone`.
Its process remained present and the captured log reported
`METAHACKTHONE_READY parts=10 mode=Build` and
`METAHACKTHONE_INPUT hands=18 rays=4 pokes=4 buttons=11`.
Device screenshots show the workbench, assembled vehicle, track and controls
rendering. See `Logs/PuzzleEditor/QuestLogcat.txt`, `QuestSmoke.png` and
`QuestAwake.png`. Passthrough is black in the captured image; the physical
headset viewpoint only shows part of the course.

The four build warnings concern an unconfigured platform app ID, two missing
scripts in the existing PICO hand-prefab resources, and the PICO platform
library's Android 15 16 KB alignment. Quest is running Android 14.
The device log also reports unavailable OVR mixed-reality capture support,
unsupported boundary-visibility extension, and initial action-set-not-attached
queries. No managed exception or application crash was found in this capture.
These SDK diagnostics remain visible; this smoke test does not certify all
SDK features or Android 15 compatibility.

Hands-on hand tracking, grip comfort, room placement and a full headset-driven
completion of the course still require a person wearing the Quest. Desktop
physics completion and automated VR input checks are recorded separately above.

## Five-course update — 2026-11-09

The replacement Wheel Size and Crab Bridge reference vehicles each completed five consecutive physical runs. The new Drill Drop Bridge high-drill vehicle completed five consecutive runs after fixing a hanging support collider that caught the drill after release. Its opt-in support release resets on Retry; Final Run keeps its prior behavior. Reversed wheels, narrow car, no drill, and low drill failed physically in the negative-case test.

The five-course campaign regression passed: ordered progression, cumulative part quantities, connection preservation, and final completion only at Factory Final Run. The initial campaign migration preserved Level 1 and Final Run geometry. Subsequent requested edits replaced Level 1 with the straight tutorial spline, grouped tracks separately from scenery, added independent 30 cm rail sections to Level 5, and standardized rollers and drill targets to red with target size 0.5. Final Run remains the fifth course.

Authored tuning: roller diameter 0.20 course units; crab center opening 1.10 with 0.55 lanes; drop bridge length 3, width 2.8, target building layer 3. All new starts use distance 2.5 on a descending approach. Level 4 uses a dedicated low-friction road material and releases the hanging target support.

Validation is Unity Editor desktop physics, not headset validation.
