# Release status

Version: **1.0.2**, the first full release (1.0.0, 2026-10-24) with its fix and security updates, for Windows 11 x64 (portable ZIP) and Linux x64 (`tar.gz`, GNOME and
KDE Plasma on Wayland), macro file schema **17**. Older macro files open and are upgraded on load; nothing is reset.

Both builds come from the same source and the same version, on GitHub and on Steam. Settings and Help show the
version and the build (for example "1.0.0 (617701c)"). Problems found after release are fixed in updates; Bug report in
the app sends a report in one press.

## What is checked for every release

- The GitHub Actions workflow builds and tests both builds from the tagged commit: `scripts/Build-Public.ps1` on
  Windows and `scripts/linux/package.sh` on Linux, with locked package restore. Every default test uses fake input,
  time and windows; none sends real input.
- Each package carries `build-manifest.json` with the source commit, the runtime and a SHA-256 for every file, and
  the archive's SHA-256 is published next to it.
- Real input is checked separately and by hand: on Windows in an isolated virtual machine
  (`scripts/Run-M0Local.ps1 -IsolatedVm`), on Linux in a private headless GNOME or KDE session
  (`scripts/linux/run-headless-probe.sh`, `scripts/linux/run-headless-kwin-probe.sh`), and in the app itself.

Not yet covered: long sessions of many hours, more monitor and display-scaling combinations, suspend and session
switches during runs, accessibility tools, and fractional scaling on a real Linux desktop. No real shutdown is run as
a test.

## Steam

The Steam version adds Steam Cloud and Workshop sharing on Windows (see [STEAM.md](STEAM.md)); on Linux and Steam
Deck, Steam installs and updates the same Linux app. Steam Deck support is limited: the app runs in Desktop Mode (not Game Mode), and recording sees only an
external mouse or keyboard, not the Deck's own controls (see [LINUX.md](LINUX.md#steam-deck-limited-support)).

## Signing

The app is not code-signed, so Windows SmartScreen or Smart App Control may warn. Checksums show that the files
are unchanged; they are not a signature. Microsoft explains the trusted provider requirement for Smart App Control:
https://learn.microsoft.com/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control

SignPath Foundation offers free signing for eligible open-source projects, subject to their review and terms
(https://signpath.org/). If trusted signing is added, the app's assemblies and executables are signed and
timestamped through that provider, the signatures verified, and the manifest and checksums regenerated. Signing
credentials never go into the repository.
