# Minimum Effort Clicker

A free, open-source mouse and keyboard automation app for **Windows 11 x64**, with a
**Linux preview** for GNOME (see [Linux](docs/LINUX.md)).
Repeat clicks and keys, create hold/spam toggles, record editable action lists,
and save screenshots. Runs locally and offline; no account or subscription is
needed to use the app.

**Preview 0.5.0-preview.34.16 unsigned.** Broader hardware/display testing and
eight-hour full-app endurance acceptance remain unfinished. This is a preview,
not a promise of universal compatibility or a warning-free Windows download.

## Download and run

Open this repository's **Releases** page and download the Windows x64 portable
ZIP from the preview release. GitHub's automatic "Source code" archives contain
source, not the runnable app. If no release is listed yet, the download is not
published.

1. Extract the complete app ZIP into a new writable folder.
2. Open `MinimumEffortClicker.App.exe`. The .NET runtime is included.
3. Choose your target and review the actions before starting.
4. **F8** starts/stops Actions, **F9** stops all Actions/toggles/recording, and
   **F10** starts/stops recording by default. Check Settings if you change keys.

Playback starts stopped. Loading/importing and restored focus never start it.
Checked toggles are available for their shortcut, not automatically running.
Use Stop All before exiting; forced process termination cannot guarantee input
cleanup. Closing may minimize to the notification area; use the tray **Exit**
command or disable that behavior in Settings.

Portable settings and macros live in `Data` beside the app. Do not run from inside
the ZIP. Keep the full runtime folder together. For upgrades, keep the old folder
and its Data, export reviewed macros, and import into a fresh extraction. Current
storage schema is **15**; older versions may reject newer files.

## Linux preview

The Linux build (`MinimumEffortClicker-<version>-linux-x64.tar.gz`) has the same pages and controls,
reads the same macro files and needs GNOME (tested on Ubuntu 26.04, Wayland). Extract it, run
`./install.sh`, then log out and back in once so GNOME loads the small ME Clicker helper extension.
Recording and hotkeys in every app also need a one-time `sudo` input-access rule. Full steps,
what the helper can do and current limits are in [docs/LINUX.md](docs/LINUX.md).

## Trust and limitations

This preview has **no publisher Authenticode signature**. Windows SmartScreen or
Smart App Control may warn or block it. Do not disable Windows protections to run
the app. SHA-256 checksums and an internal file manifest detect changed bytes;
they are not a signing certificate or a safety guarantee. Trusted signing is
planned separately; see [release status](docs/RELEASE_STATUS.md).

## Build from source

Requires Windows 11 x64 and the pinned **.NET SDK 10.0.401**. SDK setup and first
package restore need network access; the app itself runs offline.

```powershell
.\scripts\Setup-Sdk.ps1 # if the exact SDK is not installed
.\scripts\Build.ps1
.\scripts\Test.ps1
.\scripts\Build-Public.ps1 -Destination "$env:USERPROFILE\Desktop\Clicker\My Preview" -Zip
```

Default tests use fake inputs/time/targets and send **no desktop input**. Native
probe tools are separate opt-in tests for an isolated interactive VM, with a stop
route; do not run them as ordinary CI tests. TestTarget is a harmless receiver for
manual testing and is also included in the portable package.

Core/Engine contain the domain and scheduler without WPF dependencies. App is
WPF, Windows handles native integration, Storage handles bounded versioned JSON,
and Vision handles explicit local capture/conditions. Dependencies and lock files
are pinned. Release builds are self-contained and untrimmed.

The Linux build shares Core, Engine and Storage. `MinimumEffortClicker.Linux` holds its native
adapters (GNOME helper over D-Bus, evdev, logind) and `MinimumEffortClicker.Linux.App` is the
Avalonia UI; they build from `MinimumEffortClicker.Linux.slnx` with `scripts/linux/*.sh` and
never change the Windows solution. See [docs/LINUX.md](docs/LINUX.md#build-from-source).

## License and contributions

Application source is licensed under [MIT](LICENSE). Dependencies retain their
own terms: see [third-party notices](THIRD_PARTY_NOTICES.md) and `licenses/`.
MIT does not grant rights to the Minimum Effort trademarks or imply endorsement
of forks. Historical pixel artwork/font with unknown redistribution rights is
excluded from this public repository and release.

Bug reports and pull requests are welcome. Include the version, Windows version,
steps and expected/actual behavior. Do not post private recordings, screenshots,
credentials, or personal data in issues. See [contributing](CONTRIBUTING.md).
