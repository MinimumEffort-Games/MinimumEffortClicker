# Minimum Effort Clicker 0.5.0-preview.36

A free, open-source mouse and keyboard automation app, now as a portable **Windows 11 x64** app and a **Linux**
package for GNOME and KDE Plasma, with the same pages and the same version on both.

## Download

- **Windows:** download `MinimumEffortClicker-0.5.0-preview.36-win-x64.zip`, extract the whole folder and open
  `MinimumEffortClicker.App.exe`. The .NET runtime is included.
- **Linux:** download `MinimumEffortClicker-0.5.0-preview.36-linux-x64.tar.gz`, extract it and run `./install.sh`.
  On GNOME, log out and back in once so the small helper extension loads; on KDE Plasma it works straight away.
  For recording and hotkeys in every app, press **Turn on** when the app asks (your password, once). On a Steam Deck
  the app runs in Desktop Mode. Details: [docs/LINUX.md](https://github.com/MinimumEffort-Games/MinimumEffortClicker/blob/main/docs/LINUX.md).

The automatic "Source code" archives are for building, not for running the app. Each package has a `.sha256` file
next to it and a `build-manifest.json` inside with the source commit and a hash for every file.

## What's new since 0.5.0-preview.35

- **Controllers:** Xbox, PlayStation, Switch and Steam Deck controllers (including the Deck's back buttons) on Windows
  and Linux. A starts and stops Actions, B stops everything, Y records and X takes a screenshot, in any window; change
  them under Settings → Controller.
- **Controller to Key page:** turn any controller button into a key or mouse button, held for as long as you hold it.
- **Toggles by controller:** a toggle can be switched by a controller button, a key or mouse button, or both.
- **A new look:** pictures for keys, controller buttons and actions, a mouse picture and a controller picture to pick
  buttons on, a new app icon, and buttons instead of checkboxes and dropdowns. Everything can be set with the mouse;
  number fields follow the mouse wheel.
- **Overlay where you want it:** pin the status overlay to any spot along the edge of the target window or the screen
  (Settings → Overlay Anchor Point), or unlock it with its padlock and drag it. It follows the window.
- **F7** takes a screenshot by default.
- Many smaller fixes. The full list is in [CHANGELOG.md](https://github.com/MinimumEffort-Games/MinimumEffortClicker/blob/main/CHANGELOG.md).

## Good to know

- Nothing runs until you start it. **F8** starts and stops Actions, **F9** stops everything, **F10** records; change
  them in Settings. Use Stop All before closing.
- Saved macros from earlier versions open and are upgraded (macro schema 17); nothing is reset. Keep each version in
  its own folder and copy the old `Data` folder next to the new one.
- **Not code-signed:** Windows may show a SmartScreen warning the first time. Do not turn off Windows protections;
  check the SHA-256 instead.
- Some games forbid macros. Check a game's rules before using a clicker in it.
- This is a preview: please report anything that does not work as the Help text says, on GitHub Issues.
