# Minimum Effort Clicker 0.5.0-preview.35

A free, open-source mouse and keyboard automation app, now as a portable **Windows 11 x64** app and a **Linux**
package for GNOME and KDE Plasma, with the same pages and the same version on both.

## Download

- **Windows:** download `MinimumEffortClicker-0.5.0-preview.35-win-x64.zip`, extract the whole folder and open
  `MinimumEffortClicker.App.exe`. The .NET runtime is included.
- **Linux:** download `MinimumEffortClicker-0.5.0-preview.35-linux-x64.tar.gz`, extract it and run `./install.sh`.
  On GNOME, log out and back in once so the small helper extension loads; on KDE Plasma it works straight away.
  For recording and hotkeys in every app, press **Turn on** when the app asks (your password, once). On a Steam Deck
  the app runs in Desktop Mode. Details: [docs/LINUX.md](https://github.com/MinimumEffort-Games/MinimumEffortClicker/blob/main/docs/LINUX.md).

The automatic "Source code" archives are for building, not for running the app. Each package has a `.sha256` file
next to it and a `build-manifest.json` inside with the source commit and a hash for every file.

## What's new since 0.5.0-preview.34.16

- **Linux build:** GNOME (tested on Ubuntu 26.04) and KDE Plasma 6 on Wayland, reading the same macro files as
  Windows.
- **Wait until a colour,** set up right in the Create an Action panel. Select an area on a snapshot (on any monitor
  with Desktop as the target), take the colour with the Eyedropper, and use Ping scan area to see where it looks.
  Test Selected Action outlines the area in green or red. A wait keeps looking for as long as it takes.
- **Scroll actions,** and the mouse wheel is recorded.
- **Repeating / Once** per action: with Loop on, Once actions run only in the first loop.
- **The clicker comes back to the front** after a run, a test, Ping or recording that needed the target.
- **Remembered choices:** Loop, the Minimize and Record boxes, and the toggles you left Active come back at the next
  start (armed only; nothing runs until you press a key).
- **Emergency stop choice:** by default only Stop All (F9) stops the clicker, and your own clicks never end a run.
  "Stop on my input" stops everything as soon as you touch a key or button the clicker uses.
- A Help button, clearer Add Action and Save Changes, an overlay with bring-back and quit buttons, and many smaller
  fixes. The full list is in [CHANGELOG.md](https://github.com/MinimumEffort-Games/MinimumEffortClicker/blob/main/CHANGELOG.md).

## Good to know

- Nothing runs until you start it. **F8** starts and stops Actions, **F9** stops everything, **F10** records; change
  them in Settings. Use Stop All before closing.
- Saved macros from earlier versions open and are upgraded (macro schema 17); nothing is reset. Keep each version in
  its own folder and copy the old `Data` folder next to the new one.
- **Not code-signed:** Windows may show a SmartScreen warning the first time. Do not turn off Windows protections;
  check the SHA-256 instead.
- Some games forbid macros. Check a game's rules before using a clicker in it.
- This is a preview: please report anything that does not work as the Help text says, on GitHub Issues.
