# Third-party notices

Minimum Effort Clicker is MIT licensed (see `LICENSE`). It ships with the components below, which keep their own
terms. The Windows portable ZIP and the Linux package carry these notices in their `licenses` folder and record the
exact runtime version in `build-manifest.json`.

## Windows app

| Component | Version | Licence | Use |
|---|---|---|---|
| .NET / Windows Desktop runtime | 10.0.x (self-contained in the ZIP) | MIT plus bundled third-party notices | Runs the app; notices under `licenses/dotnet/` |
| .NET SDK | 10.0.401 | MIT plus bundled third-party notices | Build tooling only, not shipped |
| Microsoft.Windows.SDK.NET.Ref | 10.0.22000.87 | Microsoft Windows SDK licence | Windows API projections at build time |
| Microsoft.NET.Test.Sdk, MSTest | 18.10.1, 4.5.1 | MIT | Tests only, not shipped |

The Windows app has no other NuGet dependencies. It uses only built-in Windows and .NET facilities for its
features: Windows Forms `NotifyIcon` for the tray icon, GDI and WPF imaging for screenshots and Desktop-mode
colour waits, Windows Graphics Capture for window colour waits, and `SendInput` for playback.

## Linux app (preview)

| Component | Version | Licence | Use |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent | 12.1.3 | MIT | Linux app UI |
| Avalonia.Controls.DataGrid | 12.1.2 | MIT | Linux app UI |
| SkiaSharp / HarfBuzzSharp (Avalonia dependencies) | 3.119.4 / 8.3.1.3 | MIT | Linux app rendering |
| MicroCom.Runtime (Avalonia dependency) | 0.11.6 | MIT | Avalonia internals |
| Avalonia.Angle.Windows.Natives (Avalonia dependency) | 2.1.27548.20260419 | BSD-3-Clause, notice copied | Restored with Avalonia; not used on Linux |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Linux D-Bus (GNOME helper, KWin, logind, notifications) |

On KDE Plasma the Linux app talks to KWin directly. It loads the system's `libwayland-client` (MIT, part of the
desktop, not shipped) and restates the request names and argument types of KWin's `org_kde_kwin_fake_input`
protocol (plasma-wayland-protocols, `fake-input.xml`, © 2015 Martin Gräßlin, LGPL-2.1-or-later) in
`src/MinimumEffortClicker.Linux/Kde/FakeInput.cs` so it can speak it; no protocol file or KDE code is copied.

These packages are used only by the Linux projects (`MinimumEffortClicker.Linux.slnx`); the Windows app's
dependency graph is unchanged by them. Avalonia's build-time usage statistics are switched off by the Linux app
project and `scripts/linux/common.sh` (`AVALONIA_TELEMETRY_OPTOUT=1`).

## Inventory and telemetry

`licenses/nuget-inventory.json` lists all 39 resolved NuGet packages of both apps and their tests, with licence
expression, repository commit and package hash; copied notice files sit under `licenses/nuget/`. All declare MIT
except the ANGLE package above. The Application Insights and Testing Extensions Telemetry packages are transitive
test-tooling dependencies only; they are not in either app's runtime graph, and the build scripts set
`DOTNET_CLI_TELEMETRY_OPTOUT=1` and `TESTINGPLATFORM_TELEMETRY_OPTOUT=1`. The applications contain no telemetry or
network client. No installer, bundled font, paid library or online service is included.

## Brand assets

The Minimum Effort logo and icon under `assets/brand` are the owner's brand assets. The MIT licence covers the
source code and does not grant rights to the Minimum Effort name or logo. Historical pixel artwork with unclear
redistribution rights is excluded from this repository and from every build.

## After a dependency change

Restore both solutions, review the lock-file diff, run `scripts/Update-LicenseInventory.ps1` (it reads every
project under `src` and `tests`; `scripts/linux/update-license-inventory.py` writes the same format on Linux), and
check this file and the inventory before release.
