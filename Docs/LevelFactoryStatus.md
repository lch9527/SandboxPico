# Level factory status — 2026-11-08

Factory tracks now offer **Deck Style → Creek Deck**, with
`Deck_Straight_080` from CreekModuleGallery as the **Deck Module**. New factory
tracks use this style by default. Select the track, edit its spline or widths,
then click **Rebuild Road and Supports**. Choose **Plain** for the previous
solid deck. Ctrl+S persists both the decorative meshes and the smooth driving
collider; **Save Level 4 to Game** also updates the campaign copy.

The gallery module's timber, copper fasteners, materials and UVs follow the
spline. Modules repeat from the start at their original aspect ratio relative
to Default Width; per-point widths bend/stretch the cross-section. The final
module is trimmed. Puzzle openings clip the artwork and cap the exposed cuts.
The source prefab is unchanged. The art has no colliders: road physics, rails,
supports, puzzle positions and car guidance retain their existing behavior.
The desktop track camera frames the road collider even when its renderer is
hidden. Invalid source settings retain the last valid baked road.

Level4 and its campaign prefab use the new deck. Its spline, road collider,
goal and puzzle transforms were compared before/after migration and retained.
The empty factory template creates tracks from the updated generator prefab.
FactoryFinalRun, BeautifulCorner and the other campaign levels were not
migrated; the four-level reorder remains a separate task.

Deck verification: 35 Edit Mode cases passed across deck geometry, authoring,
puzzles, smoothing, path assistance and campaign export. The five deck cases
were rerun after removing internal caps, and the two export cases were rerun
after publishing the final meshes. Scene View previews checked the full
course and curved planks. Campaign Play Mode passed Start, Retry, Back,
starting again, goal completion and reset. An earlier run returned to Build
during launch; focusing Game View before the rerun resolved it (the runtime
returns to Build on focus loss). No headset performance or full-course driving
completion test was performed. Level4's decorative deck contains six material
meshes, 162,028 vertices and 70,422 triangles; headset profiling remains pending.

Pre-check-in validation on November 8: 63 factory/HandFleet Edit Mode cases
passed. The combined campaign/HandFleet Play Mode run passed 10 of 13 cases,
including FactoryCampaignTests and all six HandFleet cases. Three older
SandboxCampaignTests cases failed: CompletionUnlocksNextLevelAndDrill
(Next Level UI visibility), WorkbenchCarriesConstructionAndKeepsSecondCourseOnRetry
(expects the former Course25D), and LegalDesignerVehicleCompletesBothCourses
(expects Success after the current victory flow has returned to Build).
These broader regressions remain unresolved; this is not a clean full-suite pass.

`FactoryFinalRun` has been saved as **Level 4** in
`Assets/MetaHackthone/Scenes/Level4.unity`. Continue authoring in that scene.
Use **MetaHackthone > Level Factory > Save Level 4 to Game** after editing:
it validates and saves the scene, exports the course and parts palette to
`Generated/Campaign/Level4.prefab`, and registers it on the Sandbox settings.
Ctrl+S saves the authoring scene; the menu command also updates the game copy.
The original `FactoryFinalRun.unity` remains available.

Normal Sandbox progression is **Level 1 → Level 2 → Level 4** until Level 3
exists. Complete Level 2, then use **Next Level**. Level 4 starts with its
authored factory palette, including the extra blocks and drill; it does not
automatically load the designer's test car. Opening `Level4.unity` directly
still provides the factory test-car controls. The runtime course is a prefab
referenced by the existing game scene, so it requires no extra scene in Build
Settings. This saves the project; it does not rebuild or install a headset APK.

Level 4 verification: the saved-road/reference checks and session progression
checks passed in Edit Mode. Play Mode checks passed for progression, palette,
Start, Retry, Back, starting again, goal detection, victory return, and campaign
reset. Goal detection was tested by moving the full vehicle into the authored
goal; this is not a full driving-completion or headset test. The test fixture
uses synchronous scene replacement to avoid a Unity 6000.3 native deadlock
between asynchronous scene loading and deferred mesh-collider destruction.

The Start camera regression is repaired. `FactoryFinalRun` contained eight
obsolete generated puzzle copies, including a bridge saved over a billion
units from the road. Framing all puzzle renderers moved the desktop camera
away from the level. Rebuild now removes obsolete generated children, including
those belonging to disabled puzzles; the test camera frames the baked road.
Back to Build restores the workbench view. The saved spline is unchanged.

All 28 factory authoring, puzzle, path-assist, and smoothing Edit Mode tests
passed, including three new regression cases that failed before the fix.
Start, Retry, Back to Build, and starting again were checked in desktop Play
Mode. These checks do not constitute headset or full-course completion testing.

The factory template and integrated Puzzle Editor replace the incomplete
2026-11-06 checkpoint. Both `LevelFactory.unity` and `FactoryFinalRun.unity`
are saved under `Assets/MetaHackthone/Scenes`.

Use **MetaHackthone > Level Factory > Create Level from Factory** to author a
blank level, or **Open Final Run Sample** to inspect the combined course.
The existing workbench, shared sockets, saved vehicles, spline tools and
Meta hand interaction are reused. The palette adds Roller, Crab Bridge and
Drill Drop Bridge, with Scene handles and persisted road mesh/collider cuts.

See [PuzzleEditor.md](PuzzleEditor.md) for setup, desktop controls, editing,
playtesting and extension, and [PuzzleEditorValidation.md](PuzzleEditorValidation.md)
for the recorded checks and device-test limits.

The project retains its existing local PICO package dependency at
`file:../../../Tools/PICO-Unity-OpenXR-SDK`. Other workstations need that package
at the corresponding relative path. The Quest build uses the existing Meta
OpenXR configuration.
