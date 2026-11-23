# SnapCar — Sandbox-only build

This copy of the MetaHackthone project contains only the **Sandbox (Workbench Build)** mode:
build a car on the compact bench, then run the downhill / factory course campaign.

- Boot scene: `Assets/MetaHackthone/Scenes/MainMenu.unity`. The menu shows only **WORKBENCH BUILD** and
  **Place / Move Play Area**.
- Build list: `MainMenu` → `DownhillSandbox`. Nothing else is shipped.
- Removed: HandFleet, Cloud Garden (garden game), Shadow Workshop / Shape of Memory, FireRescue / Emberdeep,
  `_Recovery`, sample scenes.
- Still present, because the shared `PhysicalAssembly.Runtime` assembly compiles them together with the sandbox:
  Car Workshop and Narrow Gap code, and their scenes (`Workshop.unity`, `NarrowGap.unity`). These scenes are
  **not** in the build. Several play-mode tests use `Workshop` as a test harness (CarLoadTests, DesktopTests,
  PhysicsTests, MenuPinchTests, BuildingScene*Tests, NarrowGapTests, DesignModeTests). To run them, temporarily
  add those scenes to Build Settings.
- Copied without a Plastic workspace (`.plastic`), so it is not connected to source control.
- Product name and Android id are unchanged from the main project (`com.metahackathon2026.metahackthone`). An
  install from this copy replaces the full game on a headset.

The original full-project README is kept as `README.FullProject.md`.
