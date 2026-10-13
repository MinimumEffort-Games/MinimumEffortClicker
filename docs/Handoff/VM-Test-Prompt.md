# Prompt for an assistant inside the isolated test VM

Paste the text below into the assistant running in the VM after cloning the repository there.
The VM must be a disposable Windows 11 guest with nothing valuable in it: the probe sends real keyboard and mouse input.

---

You are testing Minimum Effort Clicker, a Windows auto-clicker, inside an isolated VMware guest that exists only for this.
The repository is cloned in the current folder on branch `cleanup/match-ui`. Read `AGENTS.md` and `docs/Handoff/Where-We-Are.md` first.

Rules

- Stay on the branch `cleanup/match-ui`. Do not merge, rebase, force-push or touch `main`.
- Make no code changes in this session. Your job is to run the tests, run the app, and write down exactly what happened. If something fails, report it; do not fix it.
- The real-input probe only runs here, as a standard (non-administrator) user, with the environment variable `MEC_ISOLATED_VM` set to `1` in this shell. F9 stops all probe input at any time.
- Never run the probe or the app with elevation, and never disable Windows Defender or SmartScreen.

Steps, in order. Run each from the repository root in PowerShell 7 and keep the full console output of every step.

1. `.\scripts\Setup-Sdk.ps1` (downloads the pinned .NET SDK into `.tools`; needs network once).
2. `.\scripts\Build.ps1` then `.\scripts\Test.ps1`. Expected: build succeeds with 0 warnings, 254 tests pass (9 TestTarget, 111 Engine, 134 Core).
3. `.\scripts\Build-Public.ps1 -Zip`. Expected: a ZIP under `artifacts\package` and a JSON verification block ending in `"Passed": true` with `"Schema": 16`.
4. `.\scripts\Build-M0Probe.ps1` to build the probe bundle.
5. `$env:MEC_ISOLATED_VM = '1'`, then run each probe mode and wait for it to finish; each writes `probe-result.json` under `artifacts\m0-local\<run id>\`:
   - `.\scripts\Run-M0Local.ps1 -IsolatedVm` (basic input, timer and capture checks)
   - `.\scripts\Run-M0Local.ps1 -IsolatedVm -Helpers` (the long regression: clicks, keys, toggles, conditions, focus loss, stop routes)
   - `.\scripts\Run-M0Local.ps1 -IsolatedVm -Recovery` (target closed, minimized, replaced)
   - `.\scripts\Run-M0Local.ps1 -IsolatedVm -Performance` (CPU and handle counts at 1, 10, 20 and 100 clicks per second)
   Click the probe's banner window once when it asks for focus. Do not touch the keyboard or mouse while a probe runs except to press F9 if something goes wrong.
6. Manual app check with the packaged build. Extract the ZIP from step 3 into a new folder, start `MinimumEffortClicker.App.exe`, then `MinimumEffortClicker.TestTarget.exe` from the same folder, and go through the Help text (Help button in the header) page by page:
   - Actions: choose the TestTarget window as Click target, add a left click at a set location, Test action, then Start; confirm the TestTarget counts the clicks and that F9 stops. Record a few clicks and keys with F10, Save Macro, Clear editor, Load Macro.
   - Toggles: make a Hold toggle for W with a shortcut, check it, press the shortcut inside TestTarget twice; make a Spam toggle and confirm it taps fast and stops cleanly.
   - Screenshot: full screen and area capture to a folder; Open last screenshot.
   - Settings: change Stop All to a letter key and confirm the warning about administrator windows appears; Reset keybinds; dark and light appearance; close the window with minimise-to-tray on and exit from the tray icon.
   After exiting, confirm no key or mouse button is stuck down (type in Notepad, move the mouse).
7. Upgrade check: load `docsHandoffVM-Reportssamplesschema15-retired-steps.mecprofile` through Actions / Load Macro and report the status line; expected: 3 retired actions removed and one Wait action left. Also load any `.mecprofile` from an older version if one exists in the VM.

Report, as one Markdown file saved at `docs\Handoff\VM-Reports\<yyyy-mm-dd>-<vm-name>.md` and committed on this branch (commit only that file; do not commit `artifacts`):

- Windows build number, display resolution and scaling, VM CPU and RAM.
- For each step: pass or fail, the exact command, and the last 30 lines of output or the full error.
- For each probe: the `Stage`, `Passed`, `Error` and `Checks` fields from `probe-result.json`, plus the file's `environment.json`.
- For the manual check: one line per bullet with what you saw, and a screenshot of each of the four pages saved next to the report as PNG (no personal data visible).
- Anything that surprised you, even if it did not fail.

Do not paste any secrets, account names or files from outside the repository into the report.
