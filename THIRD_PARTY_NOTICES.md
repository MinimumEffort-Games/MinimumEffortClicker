# Third-party dependency inventory

R19 adds screenshot macro scheduling using existing .NET/WPF/Win32 components from R18.
No new package, native library, asset or service; SDK/package pins and licence inventory unchanged.

Recorded 2026-10-08 from restored, exact-version NuGet package metadata and the verified SDK archive.
The portable package includes these dependency notices and its actual runtime version in build-manifest.json.

| Component | Version | Declared licence | Use |
|---|---|---|---|
| .NET SDK | 10.0.401 | MIT plus bundled third-party notices | Build tooling; not shipped as part of the app |
| .NET / Windows Desktop runtime | 10.0.12 | MIT plus bundled third-party notices | Development apps and self-contained isolated probe bundle |
| Microsoft.NET.Test.Sdk | 18.10.1 | MIT | Tests only |
| MSTest.TestAdapter / MSTest.TestFramework | 4.5.1 | MIT | Tests only |

`licenses/nuget-inventory.json` lists all **12** unique resolved NuGet packages, including transitive
test tooling, their declared licence expressions, exact repository commits, package hashes and
available copied notices. All current package metadata declares MIT. Detailed bundled notices are
retained under `licenses/nuget/`; .NET's full archive notices are under `licenses/dotnet/`.
Where a package contains no root notice file, the inventory retains its upstream licence URL and commit.

The Application Insights and Testing Extensions Telemetry packages are transitive **test-tooling**
dependencies. They are absent from the app runtime dependency graph. The provided scripts set both
`DOTNET_CLI_TELEMETRY_OPTOUT=1` and `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` for their process.
See [Microsoft's test-platform telemetry documentation](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-telemetry).
Application code contains no telemetry or network client.

After dependency changes, restore intentionally, review lock-file differences, run
`scripts/Update-LicenseInventory.ps1`, and verify the inventory and notices before release.
Portable publishing must also inventory the actual bundled runtime and carry its notices.
No installer, NSIS plugin, bundled font, paid library or service has been added.
The owner's original website logo and favicon are preserved under `assets/brand/source`;
their origin and hashes are recorded in `docs/BRANDING.md`. They are owner brand assets,
not newly licensed third-party artwork. The isolated probe ZIP includes the .NET licence/notices.

Application source and included owner artwork are MIT licensed; trademark rights and third-party dependency terms remain separate.

R17 uses the built-in Windows Forms NotifyIcon and context menu within the WPF application.
These are covered by the existing pinned .NET Windows Desktop runtime and its bundled notices;
no new NuGet package or third-party UI library is introduced. WPF remains the main interface.

R18 uses Windows GDI and existing WPF PNG/clipboard APIs for on-demand screenshots.
No added package, bundled binary or third-party artwork; existing Windows Desktop notices apply.

R11 uses the owner-supplied transparent logo and an ICO format conversion for the application
icon. Its source, authorization and hashes are recorded in docs/BRANDING.md. The original is
retained unchanged; this introduces no third-party runtime asset or dependency.

Historical pixel assets are excluded from this public snapshot and binary package.

