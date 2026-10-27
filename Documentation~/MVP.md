# Multitude — MVP

Edit several prefab assets at the same time, each one in its own window, without touching the scene
you have open and without going through Unity's prefab stage.

## Install

Multitude is an editor-only package embedded in this project:

```
Packages/com.gabs.multitude/
```

Unity imports it automatically. Open the window from `Tools > Multitude > Open Selected Prefabs`, or
right click a prefab in the Project window and choose `Assets > Multitude > Open Prefab in Multitude`.

## Usage

- **One window per prefab.** Open as many as you want; each keeps its own hierarchy, selection and
  camera. They can be docked or floated and survive a script reload (the unsaved edits do not).
- **Preview:** left drag orbits, right drag (or `Alt` + left drag) pans, the wheel zooms, `F` frames
  the prefab, `Frame` frames everything. Double click an object in the hierarchy to frame that object.
- **Editing:** select an object in the hierarchy and edit its components in the inspector, exactly as
  in the normal inspector (undo and redo work). Changes stay inside the window.
- **Apply** writes the prefab asset. **Revert** throws the changes away and reloads from disk.
- **Conflict:** if the prefab file changed on disk after you opened it, Apply refuses to overwrite and
  offers Overwrite / Keep editing / Reload from disk.
- **Closing a dirty window** asks Apply and close / Cancel / Discard and close. Cancel reopens the
  window with the session intact (Unity cannot veto a window close, so the window comes back instead).

### Particle systems and visual effects

The preview plays particle systems in edit mode: `Play` / `Pause`, `Restart`, `Loop` and a speed
slider between 0.1x and 2x. The systems are advanced by hand (`ParticleSystem.Simulate`), so nothing
depends on entering play mode. The controls only appear when the prefab has particle systems.

## How the preview stays isolated

Two rules are enforced by the implementation:

1. **Nothing of yours reaches the preview.** The window never reads the active scene, the scene view
   or the prefab stage; it only renders the session's own copy of the prefab.
2. **Nothing of Multitude reaches your scenes.** Preview copies live in the editor's hidden world with
   `HideFlags.HideAndDontSave`, on a layer reserved for them, and with `Renderer.forceRenderingOff`
   enabled, which removes them from the scene view and the game view. They are switched on only while
   the preview renders its own frame, and left over copies are destroyed when the editor reloads
   scripts.

Unity does not render preview scenes in this project's render pipeline, which is why the preview draws
a copy instead of the session content directly.

## Limits (MVP)

- Ordinary prefabs and prefab variants. Model prefabs and missing assets are rejected with a message.
- Prefabs only: scenes and ScriptableObjects are out of scope.
- Custom inspectors that assume the prefab stage or the global selection may draw incompletely; the
  window reports it instead of breaking.
- Unsaved edits do not survive a script recompile or a domain reload; the window reloads the prefab
  from disk.
- The preview updates ~0.35 s after an edit (it rebuilds the copy it renders), and while a session has
  unapplied changes the window repaints a few times per second to keep up.
- Editing a prefab asset on disk while the editor is in play mode can leave stale instances; leave play
  mode before applying external edits.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| The window says the preview is not available | The prefab no longer loads. Press `Revert` or reopen the window. |
| Nothing appears in the preview | The prefab has no renderers or colliders. A pure particle prefab needs some time to emit; press `Restart`. |
| An inspector shows a warning instead of its fields | That custom editor needs the prefab stage; edit through the fields that did draw. |
| Apply reports a conflict | The file changed on disk. Choose Overwrite only if you mean it, or Reload from disk. |
