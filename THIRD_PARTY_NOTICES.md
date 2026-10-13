# Third-party notices

Minimum Effort Clicker is MIT licensed (see `LICENSE`). It ships with the components below, which keep their own
terms. The portable ZIP carries these notices in its `licenses` folder and records the exact runtime version in
`build-manifest.json`.

| Component | Version | Licence | Use |
|---|---|---|---|
| .NET / Windows Desktop runtime | 10.0.x (self-contained in the ZIP) | MIT plus bundled third-party notices | Runs the app; notices under `licenses/dotnet/` |
| .NET SDK | 10.0.401 | MIT plus bundled third-party notices | Build tooling only, not shipped |
| Microsoft.Windows.SDK.NET.Ref | 10.0.22000.87 | Microsoft Windows SDK licence | Windows API projections at build time |
| Microsoft.NET.Test.Sdk, MSTest | 18.10.1, 4.5.1 | MIT | Tests only, not shipped |

The app itself has no other NuGet dependencies. `licenses/nuget-inventory.json` lists every resolved package,
including the transitive test-tooling packages, with its licence expression, repository commit and package hash;
copied notice files sit under `licenses/nuget/`. The Application Insights and Testing Extensions Telemetry
packages are transitive test-tooling dependencies only; they are not in the app's runtime graph, and the build
scripts set `DOTNET_CLI_TELEMETRY_OPTOUT=1` and `TESTINGPLATFORM_TELEMETRY_OPTOUT=1`. The application contains no
telemetry or network client.

The app uses only built-in Windows and .NET facilities for its features: Windows Forms `NotifyIcon` for the tray
icon, GDI and WPF imaging for screenshots, Windows Graphics Capture for Wait Until conditions, and `SendInput` for
playback. No installer, bundled font, paid library or online service is included.

The Minimum Effort logo and icon under `assets/brand` are the owner's brand assets. The MIT licence covers the
source code and does not grant rights to the Minimum Effort name or logo. Historical pixel artwork with unclear
redistribution rights is excluded from this repository and from every build.

After a dependency change: restore, review the lock-file diff, run `scripts/Update-LicenseInventory.ps1`, and
check this file and the inventory before release.
