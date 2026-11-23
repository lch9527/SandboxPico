# Sandbox Level Factory and Puzzle Editor

Open **MetaHackthone > Level Factory > Open Final Run Sample** for the combined
course, or **Create Level from Factory** to make an independent blank level.
The template is `Assets/MetaHackthone/Scenes/LevelFactory.unity`. It retains the
existing workbench, building parts, desktop adapter, and Meta hand interaction.

## Designer workflow

1. Select the scene's `SandboxFactoryLevel` component and press **Add Track**.
   **Edit Track Spline** activates Unity's spline tools. Move points and tangents;
   the cyan width handles edit the width at individual control points.
2. Open **Puzzle Palette**, assign the track, choose a puzzle, and click its road
   in Scene View. Select the puzzle parent to drag it along the track. Dimensions
   can be edited with cyan handles or in the Inspector. Roller rotation uses
   Unity's Rotate tool. Cutouts follow the track's orientation.
3. Puzzle placement stores distance along the track, lateral/vertical offsets,
   and relative rotation. Editing the spline updates the placement. Distances
   are absolute course units: shortening a track past a puzzle reports an error
   instead of silently moving it. Overlapping road cutouts are rejected.
4. Select the level to move its yellow **START** handle. **Bind Start** keeps it
   attached to the spline. Turn that option off to position the START transform
   freely. Move/resize the **GOAL** and **TEST BOUNDS** boxes in Scene View.
5. Choose **Test vehicle preset**, then **Test Level**. This saves/bakes the scene,
   opens its desktop test, loads the chosen assembly, and starts the run.
   **Build Vehicle** opens the workbench instead. **Save Test Car** saves the
   connected assembly as an asset and retains its assignment after Play Mode.
6. **Retry** restores the vehicle and all mechanisms. **Back to Build** preserves
   your assembly for editing. Stop Play Mode to edit the track and puzzle inputs.
7. Save the scene normally. **Save Level As** creates independent settings,
   track meshes, and a copy of the selected test car.

Unity Duplicate, Delete, Undo and Redo operate on puzzle parents. The original
road is regenerated when a cutout is deleted or disabled. Generated children
are disposable output; edit the parent parameters rather than those children.

New **Roller** puzzles use the red Goal vermilion material. New **Drop Bridge**
puzzles use that same red material for the drill target and **Target Size = 0.5**
(uniform target scale). The target has its own **Target Material** setting, so
the bridge, supports and chain retain their materials. These settings are also
applied to the existing puzzles in all campaign levels and the Final Run sample.
Rebuilding retains the authored target size and material.

## Left and right rails

Select **TrackGenerator** in Level 5, then find **Rails** in its Inspector.
**Left Rail** and **Right Rail** each have **Enabled**, **Height**, and
**Thickness** controls. Left/right follow the spline direction from START
toward GOAL. **Enable rails** switches both sides off or on while preserving
their individual settings. Existing tracks keep their previous rail dimensions
until a side is edited.

Click **Rebuild Road and Supports** after editing. The generator creates separate
**Left Rail** and **Right Rail** objects and matching colliders; disabled sides
are omitted. Edit the track settings rather than these generated objects.
**Ctrl+S** saves the scene, and **MetaHackthone > Level Factory > Save Level 5
to Game** updates the playable copy. These controls also work on other factory
tracks.

Level 5 uses **Section length (cm) = 30**. Each side contains numbered pieces
under **Generated track > Left Rail / Right Rail**. Length is measured along the
track centerline at its authored scene scale; the final piece is trimmed and
puzzle openings remain clear. Pieces meet without intentional gaps.

In **Individual rail sections**, choose a **Section** number to edit that
section's left and right **Enabled**, **Height**, and **Thickness** independently.
**Highlight section** locates it in the scene. **Use side defaults** removes that
section's overrides. Rebuild, then save to the game as above. Section numbers
are measured from the spline start, so changing the section length changes the
road interval affected by each numbered override. Set length to **0** for
continuous rails. Adjust the saved controls instead of modifying generated
child transforms, which are replaced on rebuild.

## Desktop building controls

- Hold left mouse on a loose part to drag on the workbench's horizontal plane.
- While holding: **Q/E roll**, **W/S pitch**, **A/D yaw**, in 90-degree steps.
- While holding: mouse wheel moves toward/away from the camera and rebases the
  drag plane. Release to use the existing socket attachment system.
- Right drag orbits the camera; middle drag pans in factory levels. Without a
  held part, the wheel zooms. Track View frames the course.
- Car View frames the current test vehicle for close inspection.
- Escape cancels a hold. The blue Core remains fixed on the workbench.

The key pairs use the workbench axes. The same shared face-center sockets,
connection rules, saved assembly format, and miniature vehicle physics are
used by desktop and VR.

## Puzzle parameters

**Roller Obstacle:** width, diameter, surface height, position and rotation.
This is a fixed physical cylinder with a matching cylindrical mesh collider.
Wheel size and approach momentum determine whether a car crosses it.

**Crab Bridge:** length, center-hole width, left-lane width, right-lane width,
and lateral offset. It changes the track's baked road mesh AND collider. Two
continuous lanes remain; there is no center collision, hidden floor or helper
force. A transverse beam with longitudinal side beams can support wheels over
the lanes while its Core spans the hole. Narrow/asymmetric builds receive no
automatic correction. Lane widths are independent of the normal road width.
Match the two outer lane edges to the adjoining road edges to avoid abrupt
guardrail steps. An asymmetric build may catch a beam on a lane and scrape or
stall instead of falling completely; the outcome comes from contact physics.

**Drill Drop Bridge:** gap/deck length, width, raised angle, hinge height/offset, mass,
damping, angular limits, target height/size/approach distance, support side,
required drill contacts, and collision settings. Place it on a straight or
gently sloping approach; a rigid rectangular deck cannot follow a tight bend.
The overhead target's position is sampled from the approach track. The support
post stays outside the driving corridor. A configurable-joint tether holds the
deck raised; actual drill-tip overlap/sweep releases it. A Rigidbody and hinge
then lower it under gravity onto a physical stop. No success animation drives
the deck. By default, the deck ignores the road lip at its hinge and the rear
support; it still collides with vehicles and the landing stop. These defaults
avoid an overlapping hinge seam forcing the deck upward.
The yellow pivot handle moves the hinge and landing stop together. Keep the
deck covering the cutout. Required contacts counts physics frames of drill-tip
contact, rather than separate swings. Chain links are visual; the single tether
joint carries the load, avoiding a costly chain of tiny rigid bodies on Quest.

All dimensions are **course units**, scaled uniformly by the track root.
Tune against the selected car, particularly target clearance, lane spacing,
roller diameter and remaining downhill energy after impacts. The reference
illustration does not specify physically tested dimensions.
The sample uses a gentle descending turn and a smooth road physics material
so the existing fixed-axle car can follow its guardrails through real contact.
Its final road width is 2.2 units: a 1.1-unit hole and two 0.55-unit lanes.

## Debugging and extension

Enable **Debug Physics** on the level for speed/spin readouts and Scene gizmos:
yellow centers of mass, cyan velocity, magenta angular velocity and red contact
normals. Runtime contact probes expose the last surface touched on each body.
These diagnostics apply no forces. Factory runs use actual vehicle dynamics;
they do not attach the campaign's guided spline ride.

To add another puzzle, derive from `SandboxPuzzle`, implement `BuildGeometry`
and optional validation/reset/road-band behavior. Its type appears in the
palette automatically. Alternatively, save a configured puzzle parent as a
prefab and use the palette's **Custom puzzle prefab** field. Dynamic puzzle
state must be restored in `ResetPuzzle`; authoring inputs belong on the parent.
Keep the project's shared socket layout for any new vehicle parts.

## Quest 3

Use the same saved factory scene for Quest. Keep the project's existing Android
OpenXR/Meta setup and include that scene in a test build. The editor's desktop
mode is a preview choice, not a replacement for the hand interaction rig. On
Quest, use the existing workbench start, return, retry and assembly controls.
Saving a vehicle asset is an editor operation.

Automated tests and device smoke-test results are recorded separately in
`PuzzleEditorValidation.md`; a device launch does not validate hand ergonomics.

## Five-course Sandbox campaign

Design Mode and the campaign use: Beautiful Corner, Wheel Size Challenge, Crab Bridge, Drill Drop Bridge, Factory Final Run. Open Levels 2–4 through the Design Mode menu and edit their spline and puzzle parents with the existing factory tools. Final Run is now Level 5.

Ctrl+S saves the authoring scene. Use **MetaHackthone > Level Factory > Save Level N to Game** to validate, bake, and replace that course’s playable prefab. Published copies are separate from authoring meshes.

Level 1 begins with four small wheels; Level 2 adds two large wheels; Level 3 has three long blocks total; Level 4 adds the drill. Earlier unlocked parts remain available. Campaign victory preserves the assembled construction through Levels 1–4. Final Run keeps its authored palette.

New Levels 2–4 opt into Limited Path Assist: supported approaches receive heading/centering assistance, while obstacle sections retain physical tilt and contact behavior. Levels 1 and 5 retain their prior assist. Tune puzzle dimensions in course units against the saved reference vehicles.
