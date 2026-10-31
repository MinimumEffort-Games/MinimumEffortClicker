# Minimum Effort Clicker 1.0.2

A free, open-source mouse and keyboard automation app for **Windows 11 x64** and **Linux** (GNOME and KDE Plasma,
Steam Deck in Desktop Mode), with the same pages and the same version on both. Also on Steam, with Steam Cloud and
Workshop sharing on Windows.

## What changed in 1.0.2

- Security update: the bundled .NET runtime is now 10.0.14 with Microsoft's October fixes. No package in the app has a
  known advisory.
- A screenshot folder chosen on the other system reads as the default, so Steam Cloud can carry settings between a PC
  and a Steam Deck.

## What changed in 1.0.1

- Steam: closing the clicker with Steam's Stop button no longer fills the log with errors or offers a bug report at the
  next start.
- The Actions page no longer stays greyed out after saving a toggle.
- Steam Deck: the first-start setup window fits the screen, and the back buttons work beside a game under Steam.
- Linux: copying a screenshot to the clipboard no longer crashes the app.

## Download

- **Windows:** download `MinimumEffortClicker-1.0.2-win-x64.zip`, extract the whole folder and open
  `MinimumEffortClicker.App.exe`. The .NET runtime is included.
- **Linux:** download `MinimumEffortClicker-1.0.2-linux-x64.tar.gz`, extract it and run `./install.sh`. On first start
  the clicker sets up its desktop helper in one press (on GNOME, log out and back in once). Recording is optional: press
  **Turn on** when you want it (your password, once). Details: [docs/LINUX.md](https://github.com/MinimumEffort-Games/MinimumEffortClicker/blob/main/docs/LINUX.md).

The automatic "Source code" archives are for building, not for running the app. Each package has a `.sha256` file
next to it and a `build-manifest.json` inside with the source commit and a hash for every file.

## What's new since 0.5.0-preview.36

- **Bug report in one press:** describe what happened, choose what to include, press **Send report**. No mail app or
  account needed. Nothing is sent before you press it.
- **Look recording** for first- and third-person games: camera movement is recorded and played back.
- **× times** per click or key press, **Pause / Unpause** (F7 and a controller button), **Keep on top**, and the
  Click target from last time comes back at the next start.
- **Help** is a full manual with clickable contents; Settings and Help show the version and build.
- **Toggles** say why when they cannot switch on ("No Click Target Selected").
- **Linux:** a one-press first-start setup, works on Steam and the Steam Deck, and recording is clearly optional.
- **Steam Deck:** the back buttons are the default controller shortcuts; L4 starts and stops.
- Many smaller fixes. The full list is in [CHANGELOG.md](https://github.com/MinimumEffort-Games/MinimumEffortClicker/blob/main/CHANGELOG.md).

## Good to know

- Nothing runs until you start it. **F8** starts and stops Actions, **F6** stops everything, **F7** pauses, **F9** takes
  a screenshot and **F10** records; change them in Settings.
- Saved macros from earlier versions open and are upgraded (macro schema 17); nothing is reset. Keep each version in
  its own folder and copy the old `Data` folder next to the new one.
- **Not code-signed:** Windows may show a SmartScreen warning the first time. Do not turn off Windows protections;
  check the SHA-256 instead.
- Some games forbid macros. Check a game's rules before using a clicker in it.
- Found a bug? Press **Bug report** next to Help, or e-mail bugs@minimumeffort.eu.
