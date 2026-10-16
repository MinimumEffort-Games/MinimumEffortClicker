# Contributing

The app is built for Windows 11 x64 (WPF) and Linux (Avalonia, GNOME and KDE Plasma) from one code base. Use the exact
SDK and package pins.

- **Windows:** run `scripts/Build.ps1` and `scripts/Test.ps1`.
- **Linux:** run `scripts/linux/test.sh Release` (builds `MinimumEffortClicker.Linux.slnx` and runs its tests);
  `scripts/linux/package.sh` makes the `tar.gz`.

Default tests must never synthesize desktop input. Keep Core/Engine independent of WPF, Avalonia and the native
adapters, and keep the Windows solution, lock files and behaviour unchanged by Linux-only work. Preserve stopped
startup, explicit start, foreground validation, an independent Stop All and owned-input cleanup. Loading or
importing a macro, or restored focus, must never begin playback.

Real-input probes run only in isolation: on Windows `scripts/Run-M0Local.ps1 -IsolatedVm` inside an isolated virtual
machine, as a standard user, with `MEC_ISOLATED_VM=1` set in that guest and F9 as the stop route; on Linux
`scripts/linux/run-headless-probe.sh` (GNOME) or `scripts/linux/run-headless-kwin-probe.sh` (KDE), which start a
private headless desktop. Never run them on a normal desktop or in ordinary CI.

Avoid new runtime services, drivers, injection and background-game automation; admin rights only from a button the
user presses, through the system's own password window. App-owned notices use AppDialog; system file pickers stay
native.

Describe the concrete change and how you checked it in your PR, and say honestly what was not checked. Do not
include local Data, screenshots or recordings of people, credentials, generated build output, or assets without
redistribution rights. Dependency and runtime changes need lock-file and licence-inventory review.
