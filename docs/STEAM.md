# Steam build

The Steam build is the Windows app with two extra files beside the exe: `steam.mode`, which switches the Workshop on,
and Valve's `steam_api64.dll`. The portable ZIP from GitHub has neither and never loads Steam code; it goes online only for Send report in Bug report.

The Steam build goes online only through the Steam client: when the user shares a macro, and when Steam downloads
macros the user subscribed to.

## What it adds

- **Steam Cloud** with Steam Auto-Cloud. There is no cloud code in the app. The Steam build has no `portable.mode`, so
  its data lives in `%LocalAppData%\MinimumEffortClicker`, and Steam syncs the files listed below when the app starts
  and closes.
- **Workshop**:
  - *Share on Workshop* (under Save Macro) saves the macro, then uploads it. `workshop.json` remembers the item, so
    sharing the macro again updates the same item.
  - *Browse Workshop* opens the Workshop page in the Steam client. The Steam overlay cannot draw over a desktop app.
  - Subscribed macros are copied into `Macros\Workshop`, so Load Macro shows them. Each copy is named
    `<name> - <item id>.mecprofile`. Copies are updated when the author updates the item and removed when the user
    unsubscribes.
  - Loading a Workshop macro says it was made by someone else. Save Macro then writes the user's own copy.
  - Workshop files go through the normal importer. It refuses anything the app cannot build itself, for example a
    Windows-key press.

## Safety rules for Workshop macros

1. A Workshop macro that presses a **system shortcut** (`Core/SystemShortcuts`) is never copied into
   `Macros\Workshop`. If it was copied earlier, that copy is removed. The user gets a notice once per session; the log
   records every refusal. The shortcuts are Ctrl+Esc, Ctrl+Shift+Esc, Alt+F4, Ctrl+Alt+T, Alt+F2, Alt+Space,
   Ctrl+Alt+Delete, Ctrl+Alt+F-keys and Alt+Print Screen. The check follows the key timeline, so a combination spread
   over separate actions is caught too.
2. Sharing a macro with a system shortcut is refused before anything is uploaded.
3. `workshop.json` records the id of every action that came from the Workshop. An action keeps that mark when it is
   edited, saved into the user's own macro or duplicated. A list whose Workshop actions press keys does not start, and
   Test Selected Action does not run, while Desktop is the Click target. With a window target, keys reach only that
   window, and the run pauses when anything else comes to the front, so a Workshop macro cannot type into a command
   line it opened. They never go to a terminal or command line either (`Core/TerminalWindows`: cmd, PowerShell, Windows
   Terminal and other terminals by program or window class, and the Linux terminals for when the Workshop reaches
   Linux): with one as Click target the list does not start, because there a few letters and Enter would run a command.
4. The user's own macros are not limited.

Code: `src/MinimumEffortClicker.Steam` (Steamworks.NET 2024.8.0, the only project that talks to Steam). Behind it,
`IWorkshopClient`, `WorkshopPublisher`, `WorkshopMacros` and `WorkshopShares` live in Storage, with tests.
`src/MinimumEffortClicker.App/SteamMode.cs` loads the Steam project only when `steam.mode` is present.

## Steamworks site setup (app 5440990)

**Steam Cloud**: App Admin > Cloud.

1. Byte quota per user: 200 MB. Number of files per user: 2000.
2. Enable Auto-Cloud. Add these root paths, with OS = **[Cross-Platform]** for each (Steam recommends it even for a Windows-only app, so Linux and Steam Deck can be added later without losing synced files):

   | Root | Subdirectory | Pattern | Recursive |
   |---|---|---|---|
   | WinAppDataLocal | `MinimumEffortClicker` | `preferences.json` | No |
   | WinAppDataLocal | `MinimumEffortClicker` | `profiles.json` | No |
   | WinAppDataLocal | `MinimumEffortClicker` | `workshop.json` | No |
   | WinAppDataLocal | `MinimumEffortClicker/Macros` | `*.mecprofile` | No |

3. Publish the change on the Steamworks site.

These files are deliberately left out:

- `bindings.json`: per-toggle shortcuts are machine-local by design.
- `log.txt`.
- `Macros/Workshop`: the copies are rebuilt from your subscriptions on each PC.

**Workshop**: App Admin > Workshop > General.

1. Enable ISteamUGC for file transfer.
2. Publish the change.

The first time an account shares, Steam may keep the item hidden until that account accepts the Workshop agreement on
the item's page. The app opens that page by itself.

## Build, test locally, upload

1. Download the **Steamworks SDK 1.60** from partner.steamgames.com (it must match Steamworks.NET 2024.8.0) and
   unpack it outside the repository. Never commit any file from it.
2. Build the package:
   `scripts/Build-Steam.ps1 -SteamworksSdk <unpacked folder>\sdk`.
   It writes `artifacts/package/MinimumEffortClicker-<version>-steam`.
3. Test locally:
   - Steam must be running and signed in to an account that owns the app.
   - Put a `steam_appid.txt` containing `5440990` beside the exe and start the exe directly.
   - Delete that file again afterwards; the upload script refuses a folder that has it.
4. Upload:
   `scripts/Upload-Steam.ps1 -SteamCmd <sdk>\tools\ContentBuilder\builder\steamcmd.exe -Account <account> -Build <package folder> -DepotId <depot id>`.
   - SteamCMD asks for the password and Steam Guard code itself.
   - The depot id is on the Steamworks site under SteamPipe > Depots.
   - The script never sets a build live; you do that under SteamPipe > Builds.

### Linux (and Steam Deck)

The Linux Steam build is the Linux app without `portable.mode`, so data lives in `~/.local/share/MinimumEffortClicker`.
It has no Workshop code: Steam installs, starts and updates it.

1. Build it **on Linux** (a Windows checkout has CRLF shell scripts, and Windows drops the executable bit):
   `scripts/linux/package.sh --steam <fresh folder>`. It writes the folder and `<folder>.zip` for Web Uploads; the zip
   entries keep their Linux file modes.
2. Steamworks, once: SteamPipe > Depots, add a depot for Linux + SteamOS. Installation > General, add a launch option:
   executable `MinimumEffortClicker`, operating system Linux + SteamOS, 64-bit.
3. Upload the zip to the Linux depot with SteamPipe > Web Uploads, press Commit after the upload bar, and set the
   build live under SteamPipe > Builds.
4. Steam Cloud on Linux needs a root override (Cloud > Root overrides: WinAppDataLocal on Linux becomes
   LinuxXdgDataHome). Decide first whether Windows settings should follow to Linux: the preferences hold Windows paths
   such as the screenshot folder.
