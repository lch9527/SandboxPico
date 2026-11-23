# Level 5 physics polish — 2026-11-17

Goal: a balanced car built for Level 5 should finish reliably, without sliding off
the deck. Reference build: `Generated/LevelFactory/Vehicles/Level 5 drill car.asset`
(core, top block with forward drill, two side long blocks, large front / small rear
wheels), captured from the designer's Play session.

## Changes

**Start ramp (Level5.unity, track editor path).** The first two control points were
lowered: the start drop is now ~20–24° instead of 57–61°, and 0.73 m lower in world.
Control point 3 onward, rails, puzzles and hand-edited road are unchanged. The new
section is 5 deck planks (2.568 units) shorter, so the plank pattern continues
seamlessly; puzzle distances shifted by the same amount and stay in place.
Tool: `Editor/SandboxAuthoredTrackReshape.cs` — `ReshapeStart(track, points, cutAt)`
rebakes only the start with the normal track baker and splices it onto the saved
road collider (at the control point) and deck art (at a plank gap). Use it for
authored-objects tracks, where "Rebuild Road and Supports" is intentionally disabled.

**Scenery.** The start island group and START banner were lowered with the start.
`Freight terrace 0` keeps its height and was moved sideways by the existing
`CarSceneryClearance.Clear`; a round-canopy tree on the start plateau and a wind
pennant on the finish island moved 5 cm. All Level 5 scenery clearance, terrain and
road-end checks pass. No environment colliders were added.

**Drill (Runtime/SandboxPuzzleRun.cs, all factory levels).** A drill hit used to
need the tiny tip sweep to touch the target, while the drill's 1x1x2 collision box
physically stopped the car first. Any contact between the drill's "Bit collision"
box and a target now counts (margin = this step's tip travel). Non-drill parts still
cannot break targets.

**Balance stabilizer (Runtime/SandboxFactoryPathAssist.cs, all factory levels).**
`MeasureBalance` scores left/right symmetry (centre-of-mass offset vs wheel track,
and wheel-radius difference): 1 for symmetric builds, 0.28 for "Asymmetric crab".
`Stabilize`, scaled by that score, adjusts rates only, never pose:
- damps yaw spin beyond what the road's curvature requires, and roll spin
  (time constant 0.08 s, while a wheel is on the road);
- clips pitch spin above 4 rad/s, on the road or in the air.
It is skipped past 60° tilt. With limited assist, it is skipped inside puzzle sections.

Root cause of "slides right and falls": the roller kicked the car with 7–10 rad/s
of pitch, it landed in a nose-up wheelie beyond the guide's 41° tilt limit, missed
the stone, and the unlimited guide kept snapping it into the stone, pushing it
sideways off the deck.

## Results (editor, Level5.unity, SandboxPhysicsReproduction)

| Car | Before | After |
|---|---|---|
| Level 5 drill car | ~50–70% (alternating retry failures) | 10/10, then 10/10 on the saved track |
| Course test car (saved TestCar) | 0/5 | does not fit the stone (drill edge, not tip) |
| Combined wide high drill | — | 2/5; stalls past the drop bridge (pre-existing) |
| Level 4 reference car (High drill) | — | 6/6 |

**Assist mode (decided 2026-11-17).** Level 5 keeps unlimited path assist
(`LimitedPathAssist = false`), so the stabilizer also runs through its puzzles.
`FactoryPathAssistTests` now checks limited assist for Levels 2–4 only, and
`FinalRunKeepsUnlimitedAssistance` locks in Level 5's setting.

## Open items
- Pre-existing failures not caused by this change: scenery clearance for Levels 2–4,
  and `FactoryCampaignSaveTests` (the published Level 5 prefab shares the scene's
  track mesh, as it did before).
- The saved Level 5 `TestCar` asset is not the intended car; consider saving
  "Level 5 drill car" as the level's test car.
- Not tested on a headset. No APK was built.
- `DownhillSpawnPreview.Refresh` throws an index error after some Stop/Retry
  sequences in the capture tool (pre-existing); it can end long capture batches early.
