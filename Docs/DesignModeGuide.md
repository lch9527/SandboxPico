# Edit Design Mode Levels 1–4 in the Scene view

## Open and edit

Stop Play mode. Under **MetaHackthone > Design Mode**, choose:

1. **Level 1 - Beautiful Corner**
2. **Level 2 - Gravity Garage**
3. **Level 3 - Waterfall Valley**
4. **Level 4 - Factory Final Run**

The normal Sandbox campaign follows the same four-level order. Design Mode opens any course directly without completion requirements.
These commands open the saved sandbox scene, select the course, and frame it in the Scene view. They do not start Play mode or generate geometry.

Levels 2 and 3 are saved in `Assets/MetaHackthone/Scenes/DownhillSandbox.unity`, under **Editable building layout > Level 2 - Gravity Garage** and **Level 3 - Waterfall Valley**. The menu shows the selected level and hides the other. Each has its own building area, controls, markers, and course.

Level 1 opens `Assets/MetaHackthone/Scenes/Level1.unity`.
The **Beautiful Corner art - editable copy** hierarchy contains the copied artwork;
the original `Assets/CreekDiorama/Scenes/BeautifulCorner.unity` and its prefab remain the showcase.
The **Beautiful Corner driving path** spline supplies smooth collision along the
bridge. Its **Show Generated Visuals** setting is disabled. It stays hidden across
rebuilds while its collider and center-path assist remain active. It does not deform that art:
when reshaping the course, align the scenery and spline together.
The course has start/finish markers, bounds, no puzzles, and an independent narrow test car.
Save scene edits with **Ctrl+S**; use **MetaHackthone > Level Factory > Save Level 1 to Game** to refresh the level-picker copy. Direct Play tests the
open authoring scene, including unsaved edits.

Level 4 opens `Assets/MetaHackthone/Scenes/Level4.unity`. Edit its spline and puzzles,
click **Rebuild Road and Supports**, then use **MetaHackthone > Level Factory >
Save Level 4 to Game** to update the shared game/Design Mode snapshot.

Use Unity's Move, Rotate, and Scale tools to edit objects. Save with **Ctrl+S outside Play mode**.
The scene's **Authored Sandbox Layout** component records which level is selected for direct Play.

## Move the complete building area or course

Expand **Editable building layout > Level N - Course Name**:

- **Building Area** contains the table, support collision, part spawns, building bounds, assembly anchor, and all controls/UI.
- **Sandbox Level** contains roads, spline, scenery, puzzles, start/goal, and test bounds. Leave its **Launch animation frame** as the runtime animation child.

Select either group and use **W** to move or **E** to turn around **Y**. Select the named level parent to use **Select Building Area**, **Select Sandbox Level**, and their **Uniform Scale** fields. Scale 1 is the migrated layout; 0.5 halves it and 2 doubles it. Positive uniform scale and Y rotation are supported. Tilted, stretched, zero, and negative group scales are rejected for testing/export. You can still edit individual objects inside either group.

The **Sandbox building boundary** uses Beautiful Corner’s glowing workbench artwork in all four levels: a holographic grid, rounded neon edges, and a tabletop. It is visible in the Scene view without selecting it or enabling Gizmos; Gizmos adds its label. Use **Select Building Boundary** in the level parent Inspector to find it and edit its **Size**. The artwork automatically fits that volume and follows the Building Area transform. During a test run the boundary walls hide and the tabletop remains; returning to Build restores the walls. The artwork adds no colliders, and the existing boundary return rules are unchanged.

Building-area scaling adjusts parts, sockets, support surfaces, and controls. Course scaling adjusts the road and test-car dimensions while preserving Waterfall Valley's smaller-car ratio. Physics timing and gravity remain as configured, so changing dimensions can change driving outcomes.

**Ctrl+S** saves the editor layout. Levels 1 and 4 additionally need **Save Level 1/4 to Game** to update campaign and Design Mode picker copies. Levels 2 and 3 play directly from their shared scene. Invalid factory export leaves the previous playable copy intact. Changes made during Play are temporary.

New factory copies include both groups. **MetaHackthone > Design Mode > Group Current Level for Editing** explicitly migrates older layouts, supports Undo, and can safely be run again.

## What can be edited?

- **Level 2:** the normal gameplay downhill/jump course. Move and resize the individual **Garage road** blocks, **Garage landing**, rocks, and scenery.
- **Level 3:** use **Edit Track Curve** to reshape anchors and tangents. Road meshes, collision surfaces, and movement samples are baked together. Route samples are derived data, not independent editing handles.
- Add ordinary GameObjects or prefab instances for new scenery and obstacles. Add colliders to solid obstacles.
- Move **START**, **JUMP**, **TURN**, **LOWER**, **FINISH**, and **DRILL target** markers to change gameplay locations. Marker forward arrows indicate travel direction. Markers are parented to the corresponding road or wall so moving those objects also moves the gameplay location.
- **Safe region** and **Water hazard** objects have disabled BoxColliders used as editable volume data. Keep them disabled; select and edit their Center/Size fields. Adjust them if you substantially relocate the road.
- The drill wall and its target should move together. Keep the yellow target visual aligned with the DRILL marker.
- Each course owns its workbench, part spawn markers, bounds, and controls. Editing one does not change another.

Moving road sections can create gaps and steps. Ensure adjoining meshes still meet and that the ordered route passes through the drivable surface. These edits can intentionally change difficulty; run a test after editing.

## Test the saved layout

For the level picker, use **MetaHackthone > Developer Tools > Design > Play Design
Mode Menu**, or enter Design Mode from MainMenu in Play. It lists all four levels. Beautiful Corner is the default Sandbox selection.
Level 4 loads the published factory course, Creek deck, puzzles and its saved test
car automatically. **Load Saved Car** resets that car; Start, Retry, Back and the
Track View/Car View controls work as in the factory. Switching levels resets the
temporary construction. Level 3 (Waterfall Valley) uses the same vehicle test scale as the campaign.
Completing a Design Mode test does not unlock campaign
levels or rewards. Return to normal modes to play the campaign from Level 1.

Press Unity's **Play** button while this scene is open to test the selected course in Design Mode, or select **Editable building layout** and click **Test selected level** in its Authored Sandbox Layout Inspector.

Gameplay uses the authored objects. It does not rebuild the roads, rocks, or scenery on entering Play, resetting a construction, retrying, or advancing the campaign. Normal gameplay enters via MainMenu and progresses through Levels 1 → 2 → 3 → 4.

Stop Play before making persistent layout changes. Runtime vehicle construction and effects remain temporary. Design Mode does not write player construction saves or campaign progress.

Validation: all four selections, Beautiful Corner Start/Retry/Back, and campaign
progress isolation passed. The four-course campaign integration and 14 authoring/
export checks passed, along with 26 related Play Mode regressions. The existing
Waterfall Valley driving case still fails because the test car falls into the
water before entering its connection. See `BeautifulCornerLevel1.md` for scope
and save/export details.

## Level 3 wooden road movement

Waterfall Valley uses **Connection Area** transfers between its upper and lower physics puzzles. The connection is not a physics challenge: crossing its entrance gate always transports the connected vehicle along the visible curve, even if it arrives tilted or airborne. Falling outside the gate does not trigger a transfer.

- **Transfer Duration** defaults to 2 playback seconds. Pause and playback speed affect the animation; course scale does not. Connection length, height, gravity and friction do not change outgoing physics.
- Every connected body's pose, velocity and angular velocity is preserved relative to the entrance and rotated into the exit frame. Wheel spin, wobble and joint configuration carry over; there is no boost or stabilization.
- Collisions, puzzle time, progress and failure timers are paused during transfer. Physics and hazards resume at the destination. Reverse entry transfers back toward the upper road.
- The Scene view shows entrance and exit gates. **Gate Width** defaults to road width (zero override); **Gate Bottom/Top** default to -0.25 and 2 course units relative to the road.
- Use **Edit Track Curve** to reshape the visual connection and puzzle roads. Save outside Play mode. Retry restores all temporary physics settings.
- Clear the lower drill wall and reach the physical finish to complete the puzzle. Legacy **Whole Road** guidance retains its separate momentum and parking settings.


## Legacy tools

The older **Gravity Garage 2.5D Level Editor** is a separate pipe-course prototype, not the authoritative Gravity Garage layout. Changing its parameters or the old level-generation settings does not reshape either saved course. Use the Scene view for the authored game levels.

The initial conversion is an explicit editor-only operation and refuses to run on an already-authored scene. There is no automatic generation or refresh that can overwrite your layout.

## Independent layout validation (November 8, 2026)

The hierarchy update passed 9 targeted Edit Mode checks and 19 targeted Play Mode checks (including corrected assertions for the deliberately independent UI/spawn objects). Coverage includes save/reopen and export, invalid-scale rejection without replacing the previous game copy, all four Design Mode entries, 0.5x/2x building and course scales, 90-degree turns, repeated Start/Retry/Back, preserved construction identities and connections between scaled layouts, drill unlocking, campaign restart, camera framing, and whole-arena placement without double transforms. Existing geometry retained its world transforms during migration. A direct Play check from the Level 1 authoring scene also passed Start, Retry and Back with the road and glowing table still present and no console errors. No headset validation was performed.
