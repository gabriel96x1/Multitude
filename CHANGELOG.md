# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package uses
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-10-26

First version, extracted from the game project it was built in.

### Added

- One editor window per prefab asset: hierarchy, isolated preview and component inspector, all
  independent between windows.
- Isolated sessions: a prefab is loaded into its own preview scene and stays clean until **Apply**;
  **Revert** reloads it from disk.
- External change protection: Apply compares the file on disk against the snapshot taken when the
  window opened and offers Overwrite, Keep editing or Reload from disk instead of overwriting silently.
- Dirty close handling with Apply and close, Cancel (the window comes back with the session intact) and
  Discard and close.
- Rotating preview: orbit, pan, zoom, frame to prefab or to a single object, selection highlight.
- Particle systems and VFX play in edit mode with Play/Pause, Restart, Loop and a speed slider.
- Resizable panes: draggable separators for the hierarchy width and the inspector height, remembered
  per window, double click to reset.
- EditMode tests for validation, session states, apply/revert/conflict, preview host and VFX playback.
