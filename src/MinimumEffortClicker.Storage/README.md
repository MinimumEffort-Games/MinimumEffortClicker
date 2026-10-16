# Storage

Bounded, versioned JSON on disk. Nothing here touches input or the UI.

- `HelperProfiles`: the local toggle library (`profiles.json` with `.bak` and `.corrupt-*` siblings) and single
  `.mecprofile` macro files. Writes are atomic (temporary file, flush, replace). Files from profile schema 2 to the
  current version are upgraded on load; retired actions are counted so the app can tell the user.
- `MacroFiles`: safe file names for the Save/Load workflow in the `Macros` folder.
- `Preferences`, `LocalBindings`, `ScreenshotSettings`: machine-local settings and shortcut assignments. Restoring a
  binding never enables anything; the toggles left active at close are remembered in `preferences.json` and armed
  again at start (armed only: nothing is pressed until their shortcut is).
- `ScreenshotFiles`: commits a complete PNG under a unique name, never overwriting.

Every reader checks size limits and required fields before deserialising, and unknown fields are rejected.
