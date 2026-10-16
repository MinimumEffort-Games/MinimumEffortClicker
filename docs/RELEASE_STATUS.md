# Release status

Version: **0.5.0-preview.35**, a public preview for Windows 11 x64 (portable ZIP) and Linux x64 (`tar.gz`, GNOME and
KDE Plasma on Wayland), macro file schema **17**. Older macro files open and are upgraded on load; nothing is reset.

Both builds come from the same source and the same version. They stay previews until more people have used them on
more hardware, displays and desktops; a 1.0 follows after that and a few updates.

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

A Steam version with Steam Cloud and Workshop sharing is prepared (see [STEAM.md](STEAM.md)). The store page is not
public yet. On Steam Deck the app runs in Desktop Mode; Game Mode is not supported.

## Signing

The preview is not code-signed, so Windows SmartScreen or Smart App Control may warn. Checksums show that the files
are unchanged; they are not a signature. Microsoft explains the trusted provider requirement for Smart App Control:
https://learn.microsoft.com/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control

SignPath Foundation offers free signing for eligible open-source projects, subject to their review and terms
(https://signpath.org/). If trusted signing is added, the app's assemblies and executables are signed and
timestamped through that provider, the signatures verified, and the manifest and checksums regenerated. Signing
credentials never go into the repository.
