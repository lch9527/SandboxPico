# Beautiful Corner Sandbox Level 1

## Straight tutorial road — November 9, 2026

Level 1 now uses a straight wooden course: a short upper start platform, a smooth downhill ramp, and a flat run to GOAL. It has no puzzles. The existing factory spline editor generates both the Creek Deck artwork (`Deck_Straight_080`) and the smooth driving collider. **Show Generated Visuals** is enabled. The former sagging bridge and duplicate station platforms have been replaced in this Sandbox copy; the original BeautifulCorner showcase is unchanged.

The default car uses the existing core, long block, short block and four small wheels. It launches on the downhill approach and rolls under gravity. No push force or motor was added. Level 1 uses the existing limited path assist, which centers supported cars and corrects heading while leaving pitch and vertical motion to physics. The standard road physics material is retained.

The Beautiful Corner environment and glowing building area keep their authored transforms. Track, START/GOAL banner meshes, anchors and goal trigger are grouped under **Sandbox Level > Launch animation frame > Track**, so the course can move independently of **Environment**. Select **Tutorial straight track** inside that group to edit its spline.

## Authoring and saving

Open **MetaHackthone > Design Mode > Level 1 - Beautiful Corner** to edit `Assets/MetaHackthone/Scenes/Level1.unity`.

Move spline points, then click **Rebuild Road and Supports**. The wooden deck follows the spline automatically. The launch pose follows **Start Binding**. When changing the endpoints, also align the START/GOAL artwork and goal trigger inside Track.

**Ctrl+S** saves the authoring scene. **MetaHackthone > Level Factory > Save Level 1 to Game** validates and exports the playable copy to `Assets/MetaHackthone/Generated/Campaign/Level1.prefab`. The campaign and Design Mode picker use that exported copy. Invalid track/puzzle placement prevents export and leaves the previous valid copy available. Direct Play from Level1 uses the open authoring scene.

## Campaign context

The five-course order is Beautiful Corner, Wheel Size Challenge, Crab Bridge, Drill Drop Bridge, and Factory Final Run. Level 1 completion unlocks big wheels; Level 2 unlocks the additional rectangles for three total; Level 3 unlocks the drill. Factory Final Run remains Level 5. Design Mode offers direct access to all five courses. Normal campaign play starts with loose parts; Design Mode can load the default test car.

The glowing workbench remains under Building Area, separate from the level. Its existing grabbing, centered face sockets, snapping, rotation and Start controls are unchanged.

## Validation

Desktop Play Mode checks use the actual default car and normal goal detection, with five consecutive launches and resets. The unattended test suppresses editor-focus cancellation through the existing comparison guard; gameplay focus handling is unchanged. Export tests check collision, visible deck art, independent scene/export meshes, and rejection of invalid edits. No headset validation was performed for this track change.

Final validation: 2/2 Play Mode tests passed (five physical default-car completions, plus Design Mode Start/Retry/Return), job `62414f721172482db0cdbea8150e4e73`. Both export/reopen Edit Mode tests passed, job `8c9253f6d4304805a607fe9bb6a206ec`. The Unity console reported no errors.
