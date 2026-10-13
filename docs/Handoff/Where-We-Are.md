# Where we are

Review of the public snapshot (commit 4893ade, v0.5.0-preview.34.16) on 2026-10-13.
Goal: a proper open-source launch and a Steam store launch.
Every item below was read in the source and re-checked; line numbers refer to this commit.
Nothing has been changed yet. The build was not run here: the PC has SDK 10.0.400 and `global.json` pins 10.0.401 with roll-forward disabled.

## Roy's open list

Struck items are done. Never resurrect them.

### A. Must fix before any launch

1. **Code must match the four pages (Roy, 2026-10-13).** Actions, Toggles, Screenshot and Settings are the whole product. The library page, wizard and sequence editor were removed from the UI on purpose; their logic is still in the code and must go. Cleanup list, in delete order:
   - **App, hidden XAML:** `ProfilesPage` (`MainWindow.xaml:175-215`) and `WizardPage` (`:293-301`) and every handler only they reference: LoadProfile, DeleteProfile, Import, Export, NewSequence, EditMacro, RunCompatible, DuplicateMacro, DeleteMacro, MacroDoubleClick, CaptureMacroKey, ClearMacroKey, Availability_Changed, GuideQuick/Record/Vision.
   - **App, unwired handlers:** EditQuick, Help, Diagnostics, OpenTestTarget, UnlockOverlay/LockOverlay, DeleteLibrary, ExitApplication, Wizard, ScreenshotSettings, StopActions, ApplyHotkeys. (Decide first whether Help, Diagnostics and tray Exit should get a button instead; see decisions.)
   - **App, whole files:** `SequenceEditor.xaml` + `.xaml.cs`, `FlowCommands.cs`, `SubsequenceCommands.cs`, `RepetitionCommands.cs`, `TimingWindow.xaml` + `.xaml.cs`, `SetupWizard.cs`, `ShellViewModel.cs`, the SequenceEditor half of `PlaybackCommands.cs` and of `ConditionEditor.cs` (the `ConditionEditor` window itself stays: the Actions page opens it from `QuickBuilder.cs:220`), most of `ProfileCommands.cs`. Remove the `CommunityToolkit.Mvvm` package once `ShellViewModel` is gone.
   - **Core:** `StepKind.Subsequence`, `StepKind.ConditionalFlow`, `StepKind.Text`, `ConditionalFlow`, `FlowKind`, `CompiledFlow`, `SequenceLibrary` subsequence bodies, `CompatibleHelpers`. The Actions page only creates Input, Wait, TimedGroup, Condition and Screenshot steps.
   - **Engine:** `HelperSetControl`, `CombinedHelpers` (Run together), `ClickRehearsal`, `DeadlineWait.WaitUntilOrChanged`, the `changed` parameter of `WaitForClick`, and the flow/subsequence branches of `SequenceCompiler` and `ClickExecutor`.
   - **Windows:** the helper-set join paths in `DesktopRuntime` (`MacroTriggered` busy-join, `ValidateJoiningSequence`), `SequenceConditions` parts that only served flow.
   - **Storage:** `HelperProfiles` library (`profiles.json`, `.bak`, `.corrupt-*`), `DeleteAllProfileData`, `MacroFiles.EnsureLibraryFiles`; keep the single `.mecprofile` save/load and `LocalBindings`. Bump the profile schema to 16 with a migration that drops Subsequence, Flow and Text steps and tells the user what was dropped. Saves are never reset.
   - **Tests and tools:** delete the tests for everything above; keep `WindowsProbe` only if the VM probe survives item 5.
   - **Help text:** rewrite `HelpCommands` content to describe only the four pages (or ship it as `docs/Help.md`).
   Single-file Save and Load on the Actions tab, the Condition editor, the colour and region pickers, the target preview, the overlay, toggles, screenshots and all Settings stay.
2. **No crash safety net for held input.** No `DispatcherUnhandledException` or `AppDomain.UnhandledException` anywhere. Several `async void` handlers have no catch (`ShortcutCommands.cs:12-62` CaptureShortcut, `PlaybackCommands.cs:50-54` ResumePlayback). If one throws, the process exits, `MainWindow.Closed` never runs, `runtime.Dispose()` is skipped and any key or button the clicker is holding stays down. Fix: register both handlers in `App.OnStartup`, best-effort `executor.RetryCleanup()` then an AppDialog; wrap the two bare handlers.
3. **Hook thread can block on the executor lock while SendInput is in flight.** `ClickExecutor.cs:175,261,460` call `sink.TrySend` inside `lock (gate)`; `HasOwnedInput`/`Owns` (`:217-218`) take the same gate; `DesktopRuntime.Toggle` (`:429`) and `MacroTriggered` (`:455`) run on the control/hook thread and call them. SendInput waits for the LL hooks, the hooks wait for the control thread, the control thread waits for the gate. Windows breaks the wait after the hook timeout and silently unhooks; `Available` still says true, so Stop All by key, physical-priority and recording all die with no indication. Fix: never block the control thread on `gate` (lock-free flags for the hook thread, or send outside the lock), and detect hook loss.
4. ~~Spam toggles ignore the global click cap.~~ By design (Roy, 2026-10-13): Spam is for fast clicking; the cap exists to stop Actions lagging the user's PC. Write that in the Settings tooltip and Help. Still a bug: at `ConcurrentActions.cs:58-59` a toggle whose PressDuration equals Interval runs at half cadence.
5. **Owner-only gates and paths baked into the public repo.**
   - `tools/WindowsProbe/Program.cs:22-26`: refuses to run unless BIOS says VMware and the Windows user is literally `GPT`. Replace with an explicit opt-in (env var or `--i-am-in-an-isolated-vm` plus a VM check), documented.
   - `scripts/Build-Public.ps1:4-7` and `.github/workflows/release.yml:24`: packaging only works under `Desktop/Clicker`. Make the destination a parameter with a sensible default.
   - `AGENTS.md` last line and `CONTRIBUTING.md`: "Keep delivered builds under Desktop/Clicker" is a private-PC rule, not a contributor rule.
6. **User-facing text still says "private personal preview".** `HelpCommands.cs:31` (diagnostics header), `:86` (About), and `:83` says "Schema 8 upgrades older imports" while the app is at schema 15. `src/MinimumEffortClicker.Storage/README.md` says no storage schema exists; `src/MinimumEffortClicker.Windows/README.md` says the app shell has no automation controls. `THIRD_PARTY_NOTICES.md:34,47` link to `docs/BRANDING.md`, which is not in the repo.

### B. Bugs worth fixing (by area)

Windows / native

7. `DesktopControl.SampleBeforeRun` (`:124-132`) records the app's own injected holds as physical (GetAsyncKeyState sees injected state). Then `DesktopRuntime.cs:843` refuses the Up as "user's physical hold" and the key stays down until the user taps it. Skip controls the executor owns. Related: `:471` uses `ModifiersDown` instead of `UnownedModifiersDown`, so a second toggle cannot start while a modifier toggle is held.
8. `DesktopControl.cs:199` rethrows after `TrySetException`, so any exception in a queued command kills the control thread (hooks and hotkeys gone, "restart the application"). Drop the `throw`.
9. Hook-only Stop keys (anything but F1-F11, and all mouse buttons) do not reach the app while an elevated window is in front (medium-IL hook, asInvoker manifest). Keep a RegisterHotKey F-key as an always-on second stop route and warn in the picker.
10. `DesktopControl.cs:234-239` installs a session-wide EVENT_OBJECT_DESTROY WinEvent hook; every destroyed object in every app lands on the thread that must service LL hooks in time. Scope it to the selected target's process.
11. `DesktopRuntime.cs:139-142` allocates an enumerator on every mouse move inside the LL hook (`activeControls.Any(...)`). Keep a volatile bool.
12. `ListTargets` (`DesktopRuntime.cs:147-161`) lists DWM-cloaked windows (suspended UWP frames) as targets. Filter with `DWMWA_CLOAKED`.
13. `WindowRegionCapture.cs:93` never disposes the `IDirect3DSurface` RCW per frame (GC cleans it, latency nit).

Engine / Core

14. `ConcurrentActions.cs:81` sets `actionSession` before the single-run guard; a rejected second `Execute(session)` then clears the active run's session state in `finally` (`:128`) and toggles stop being serviced. Move the assignment inside the guarded lambda; add the overlap test the plain overload already has.
15. Pause with a mouse-button Hold toggle active ends the whole session as "Interaction interrupted" (`ClickExecutor.cs:153-157` treats any owned mouse button as a partial click). Exclude Hold-toggle inputs.
16. Condition waits skip the click-completion limit (`ClickExecutor.cs:373` checks loops and time but not `TotalCompletedClicks`), so a shutdown-by-clicks plan can poll a Wait Until for up to 40 minutes after reaching its count.
17. `SequenceDefinition.cs:226` runs timing validation before the null-child check at `:277`; a profile with `"children": [null]` and a timing block throws NullReferenceException, which the Storage loaders do not catch, so opening the file crashes instead of reporting "invalid profile".
18. `HelperSetControl.cs:31-32` preflights a rejoin while the stopping member is still in `members`, so a quick off/on on the same trigger fails with a duplicate-identity error.
19. `PlaybackPaused(true)` has no matching `false` when cancelled during `WaitForResume` (`ClickExecutor.cs:165-168`). Harmless today because PowerSession is disposed at run end, but the contract is asymmetric.
20. `ShutdownSchedule.Update` (`:44-45`) reports `Remaining` of about minus 2026 years for loop/click triggers, and a forward wall-clock jump fires a SystemTime trigger early.

App / shell

21. Deleting a macro or folder removes its hotkey binding in memory only (`MainWindow.xaml.cs:552`, `ProfileCommands.cs:261`); the orphan reloads at `:101` and blocks that key for toggles forever with no UI to clear it. Save after removal, or prune unknown MacroIds at load.
22. `initialized` is true in the constructor (`MainWindow.xaml.cs:75`) before preferences load at `:103`; the header "Show overlay" box is outside the disabled `Pages`, so an early click writes XAML defaults over `preferences.json`.
23. `Closed` (`MainWindow.xaml.cs:164-175`) dereferences `runtime` unguarded; in `--render-preview` mode runtime is null, so the screenshot tool crashes on shutdown. `Window_Closing` has the guard.
24. Shell state is driven by string compares on runtime status text ("Could not continue", "Not started", `StartsWith("Recording")`, `== "Completed"`) in `MainWindow.xaml.cs:201,215,230`, `StatusOverlay.cs:115,137`, `QuickTestingRecording.cs:54`. A wording change breaks window restore and overlay colour with no compile error. Put an enum on `RuntimeSnapshot`.
25. Overlay drag, saved X/Y and Anchor-to-target are dead: `PositionOverlay` (`:246-257`) re-pins to the target corner every refresh, `StatusOverlay.Place()` is never called, the Anchor box is collapsed, Unlock is unwired. Wire it or delete the options.
26. Selecting a shutdown mode in the Settings dropdown flips the Actions-tab Loop box (`PowerCommands.cs:40,85`) before anything is armed. Only require Loop at arm time.
27. The "wait for settings to finish loading" tray branch (`TrayCommands.cs:40-42`) is dead because the tray icon is created after `traySettingsLoaded`. Closing in the first second exits even with close-to-tray on.

### C. Cleanup and launch prep

28. **Structure.** MainWindow is 18 partial files with about 20 ad-hoc busy flags (`quickOperation, macroFileBusy, ioBusy, bindingBusy, screenshotBusy, ...`) checked in different subsets per handler; `DeleteProfile_Click` already forgot `bindingBusy`. `PlaybackCommands.cs` and `ConditionEditor.cs` each hold partials of two classes. `ShellViewModel.cs` is an unreferenced stub and the only reason `CommunityToolkit.Mvvm` is referenced. One operation gate, one class per file, delete the stub and the package.
29. **Duplicated logic.** `ClickExecutor.SampleDuration` equals `ScheduledTiming.Range`, and the same sample check is in `ClickMotion.cs:18`; the missed-slot catch-up in `ExecuteCore` duplicates `CombinedHelpers.CompleteCycle`; the "mark Completed, RemoveAll" pair appears three times in CombinedHelpers. Dead: `WaitForClick`'s `changed` parameter, `ClickRehearsal`, `DeadlineWait.WaitUntilOrChanged`.
30. **Magic numbers.** Default keys 0x77/0x78/0x79 appear in four places; scan codes, mouse codes and Win32 style constants are literals across the UI layer. `ShortcutKeys.DefaultStart/Stop/Record`, named scan codes in Core, named constants in `ProbeNative`.
31. **Readability.** 146 lines over 200 characters, many multi-statement lines. Add an `.editorconfig`, run `dotnet format`, and enable `AnalysisLevel=latest-recommended` plus `EnforceCodeStyleInBuild`. Move the 68 `DllImport`s to `LibraryImport` (source-generated, analyzer SYSLIB1054).
32. **Toolchain friendliness.** `global.json` `rollForward: disable` on a patch pin blocks anyone without exactly 10.0.401 (this PC has 10.0.400). `latestPatch` keeps reproducibility (lock files do the real pinning) and stops first-build failures.
33. **CI.** `release.yml:24` hard-codes `0.5.0-preview.34.16`, which `Directory.Build.props` already owns; read it like the release job does. The tag run builds twice (build job, then release job); let release reuse the artifact. Add a PR check that `dotnet format --verify-no-changes` passes once 31 is done.
34. **Docs for a public repo.** `THIRD_PARTY_NOTICES.md` is a changelog of R11/R17/R18/R19 paragraphs; rewrite as current state. Add `SECURITY.md` (how to report), issue and PR templates, a short `CHANGELOG.md`, screenshots in the README, and a one-paragraph "is this allowed in my game" section. Ship the Help text as a markdown file in `docs/` and load it, so it is readable on GitHub and editable without a rebuild.
35. **Tests to add.** Mouse Repeat toggle vs ClickRateLimit (4); `Execute(session)` overlap (14); Pause with mouse Hold toggle (15); click limit then condition (16); null children with timing (17); rejoin before release (18); anything at all for `DeadlineWait` and `StopwatchClock`, which currently have zero tests.
36. **Steam.** Steam installs under `steamapps/common` and users expect data in `%LocalAppData%` or Steam Cloud, not beside the exe; `portable.mode` needs a non-portable sibling. Steam builds are signed by nobody by default, so SmartScreen is not an issue there, but the Microsoft Store/Smart App Control story still needs the SignPath or paid cert route for the GitHub download. Decide whether the Steam build gets Steamworks (overlay, cloud saves, achievements are all optional for a utility). Store page must state plainly that it is a desktop utility and that games with anti-cheat may ban macro use.

## In-editor jobs only Roy can do

- Decide item 1: restore the library/editor tabs, or cut them for the first public release.
- Decide item 5: who else should be able to run the VM probe, and under what gate.
- Install .NET SDK 10.0.401 (or approve relaxing `global.json`) so the suite can run on this PC.
- Confirm `docs/BRANDING.md` should be added (brand asset provenance) or the references removed.
- Steam: create the app page and pick the Steamworks scope.

## What is good

The input ownership model is the strongest part: one dispatch gate, releases in reverse order, only accepted downs enter `owned`, cancellation releases independently of a blocked scheduler, and the engine refuses to release a user's physical hold. Every injected event is tagged and the hooks filter injected flags, so the app never mistakes its own input for the user's. The P/Invoke layouts, SendInput normalisation under PerMonitorV2, GDI and D3D cleanup, and the dedicated pumping thread for hooks are all correct. Core and Engine have no desktop dependency and a reflection test enforces it. The compiler bounds worst-case duration and action counts before anything runs. The fake-clock test suite is deterministic and asserts exact timestamps and edge order. Storage writes are atomic with validated backups and bounded sizes. CI pins actions by SHA with least-privilege permissions, and the release ZIP carries a per-file manifest the verify script checks.
