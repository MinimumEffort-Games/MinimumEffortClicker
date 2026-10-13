# Where we are

Review of the public snapshot (commit 4893ade, v0.5.0-preview.34.16) on 2026-10-13.
Goal: a proper open-source launch and a Steam store launch.
Every item below was read in the source and re-checked; line numbers refer to this commit.
Nothing has been changed yet. The build was not run here: the PC has SDK 10.0.400 and `global.json` pins 10.0.401 with roll-forward disabled.

## Progress log

- 2026-10-13: App cleanup committed (hidden library page, sequence editor, wizard, overlay editing, MVVM package removed; Help button added). Build and 297 tests green; app starts.
- 2026-10-13: Core/Engine/Windows/Storage cleanup: text, subsequence and branch steps, logical-key and Unicode input kinds, "Run together" helper sets, rehearsal, finish-loop, resume countdown and armed-plan paths removed. Profile schema is 16; schema 2–15 files are upgraded on load and the app reports how many retired actions were dropped. 245 tests green (tests for removed features deleted, one migration test added). Spam cadence bug (press equal to interval) fixed on the way. The GitHub Desktop commit "Clean-up" (e88a05f) was made mid-work and does not build on its own; the next commit completes it.

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
2. ~~No crash safety net for held input.~~ Done 2026-10-13: both unhandled-exception handlers call `DesktopRuntime.EmergencyRelease`, which stops, waits for the worker and releases owned input without taking the runtime lock; the shortcut capture handler now catches.
3. ~~Hook thread can block on the executor lock.~~ Done 2026-10-13: `HasOwnedInput`/`Owns` read the published snapshot lock-free, so the control thread never waits on the dispatch gate. Still open: detect a hook Windows silently removed (periodic tagged no-op event, or re-install on a timer) and report `Available = false`.
4. ~~Spam toggles ignore the global click cap.~~ By design (Roy, 2026-10-13): Spam is for fast clicking; the cap exists to stop Actions lagging the user's PC. Write that in the Settings tooltip and Help. Still a bug: at `ConcurrentActions.cs:58-59` a toggle whose PressDuration equals Interval runs at half cadence.
5. ~~Owner-only gates and paths baked into the public repo.~~ Done 2026-10-13: the probe needs `MEC_ISOLATED_VM=1`, a hypervisor and a standard user instead of the `GPT` account; packaging defaults to `artifacts/package`; CI reads the version from `Directory.Build.props`; AGENTS.md and CONTRIBUTING.md rewritten.
6. ~~User-facing text still says "private personal preview".~~ Done 2026-10-13: help text, both sub-project READMEs and the third-party notices rewritten; the dangling BRANDING.md links are gone.

### B. Bugs worth fixing (by area)

Windows / native

7. ~~Own injected holds recorded as physical.~~ Done 2026-10-13: owned inputs are never sampled; modifier checks use the unowned variant.
8. ~~Queued-command rethrow kills the control thread.~~ Done 2026-10-13.
9. Hook-only Stop keys do not reach the app while an elevated window is in front. 2026-10-13: the Stop All picker now warns when a non-F-key is chosen. Still open: an always-on registered F-key as a second stop route.
10. ~~Session-wide destroy hook.~~ Done 2026-10-13: the destroy hook is installed per selected target process.
11. ~~Per-move allocation in the mouse hook.~~ Done 2026-10-13.
12. ~~Cloaked windows listed as targets.~~ Done 2026-10-13.
13. ~~Capture surface left to the finalizer.~~ Done 2026-10-13.

Engine / Core

14. ~~Session claimed before the single-run guard.~~ Done 2026-10-13 (in the rewrite).
15. ~~Pause with a mouse Hold toggle aborts the session.~~ Done 2026-10-13.
16. ~~Condition waits skip the click-completion limit.~~ Done 2026-10-13.
17. ~~Null children with timing crash the loader.~~ Done 2026-10-13.
18. ~~Rejoin before release fails.~~ Gone with the helper sets.
19. ~~Unpaired PlaybackPaused.~~ Done 2026-10-13.
20. ~~Shutdown Remaining of minus 2026 years for count triggers.~~ Done 2026-10-13. A forward wall-clock jump still fires a "shut down at" time early; the 30 s countdown stays cancellable, and a wall-clock time is what the user asked for, so this stays as is.

App / shell

21. ~~Orphan hotkey bindings block keys forever.~~ Done 2026-10-13: bindings whose toggle no longer exists are pruned at load and the file is rewritten.
22. ~~Early click overwrites saved preferences.~~ Done 2026-10-13: `initialized` stays false until the preferences are loaded.
23. ~~Preview-render crash on shutdown.~~ Done 2026-10-13.
24. ~~String compares on runtime status text.~~ Done 2026-10-13: `RuntimeSnapshot` exposes `Failed`, `Recording` and `Completed`; the strings live in one place.
25. ~~Dead overlay drag/anchor options.~~ Done 2026-10-13: the options are gone; the preferences reader now ignores fields an older version wrote instead of resetting.
26. ~~Shutdown dropdown flips Loop.~~ Done 2026-10-13: Loop is only switched on when a plan is armed.
27. The "wait for settings to finish loading" tray branch (`TrayCommands.cs:40-42`) is dead because the tray icon is created after `traySettingsLoaded`. Closing in the first second exits even with close-to-tray on. Low impact; still open.

### C. Cleanup and launch prep

28. **Structure.** MainWindow is 18 partial files with about 20 ad-hoc busy flags (`quickOperation, macroFileBusy, ioBusy, bindingBusy, screenshotBusy, ...`) checked in different subsets per handler; `DeleteProfile_Click` already forgot `bindingBusy`. `PlaybackCommands.cs` and `ConditionEditor.cs` each hold partials of two classes. `ShellViewModel.cs` is an unreferenced stub and the only reason `CommunityToolkit.Mvvm` is referenced. One operation gate, one class per file, delete the stub and the package.
29. **Duplicated logic.** `ClickExecutor.SampleDuration` equals `ScheduledTiming.Range`, and the same sample check is in `ClickMotion.cs:18`; the missed-slot catch-up in `ExecuteCore` duplicates `CombinedHelpers.CompleteCycle`; the "mark Completed, RemoveAll" pair appears three times in CombinedHelpers. Dead: `WaitForClick`'s `changed` parameter, `ClickRehearsal`, `DeadlineWait.WaitUntilOrChanged`.
30. **Magic numbers.** Default keys 0x77/0x78/0x79 appear in four places; scan codes, mouse codes and Win32 style constants are literals across the UI layer. `ShortcutKeys.DefaultStart/Stop/Record`, named scan codes in Core, named constants in `ProbeNative`.
31. **Readability.** 146 lines over 200 characters, many multi-statement lines. Add an `.editorconfig`, run `dotnet format`, and enable `AnalysisLevel=latest-recommended` plus `EnforceCodeStyleInBuild`. Move the 68 `DllImport`s to `LibraryImport` (source-generated, analyzer SYSLIB1054).
32. ~~Toolchain friendliness.~~ Done 2026-10-13: `global.json` rolls forward to the latest 10.0.4xx patch.
33. **CI.** ~~Hard-coded version~~ fixed 2026-10-13. Still open: the tag run builds twice (build job, then release job); let release reuse the artifact. Add a PR check that `dotnet format --verify-no-changes` passes once 31 is done.
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
