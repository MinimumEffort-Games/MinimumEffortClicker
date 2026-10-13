# Where we are

Review of the public snapshot (commit 4893ade, v0.5.0-preview.34.16) on 2026-10-13.
Goal: a proper open-source launch and a Steam store launch.
Every item below was read in the source and re-checked; line numbers refer to this commit.
Nothing has been changed yet. The build was not run here: the PC has SDK 10.0.400 and `global.json` pins 10.0.401 with roll-forward disabled.

## Progress log

- 2026-10-13: App cleanup committed (hidden library page, sequence editor, wizard, overlay editing, MVVM package removed; Help button added). Build and 297 tests green; app starts.
- 2026-10-13: Core/Engine/Windows/Storage cleanup: text, subsequence and branch steps, logical-key and Unicode input kinds, "Run together" helper sets, rehearsal, finish-loop, resume countdown and armed-plan paths removed. Profile schema is 16; schema 2–15 files are upgraded on load and the app reports how many retired actions were dropped. 245 tests green (tests for removed features deleted, one migration test added). Spam cadence bug (press equal to interval) fixed on the way. The GitHub Desktop commit "Clean-up" (e88a05f) was made mid-work and does not build on its own; the next commit completes it.

- 2026-10-13: Launch prep committed (probe gate, packaging path, CI version, SDK roll-forward, docs); packaging script verified end to end (500 files, schema 16). Bug-fix batch committed: crash safety net, lock-free ownership reads for the hook thread, owned-hold sampling, scoped destroy hook, cloaked targets, orphan bindings, early-click preference overwrite, runtime flags instead of status strings, overlay options trimmed. 245 tests green; app starts.

- 2026-10-13: Regression tests for the fixed bugs plus real-clock wait tests (254 tests). Public-repo files added: SECURITY.md, issue and PR templates, CHANGELOG.md, .editorconfig; the release workflow now publishes the artifact the build job tested instead of building twice.

- 2026-10-13: PR #1 opened (https://github.com/MinimumEffort-Games/MinimumEffortClicker/pull/1); the reworked CI passed on it. Code-quality pass committed: operation gate, named default shortcuts and modifier scan codes, one timing sampler. Formatter and analyzer rollout measured and deferred (item 31).
- 2026-10-13: First isolated-VM test session (Codex in Roy's VMware Windows 11 guest, report in `VM-Reports/2026-10-13-vmware20-1.md`): SDK, build, 254 tests, packaging (500 files, schema 16) and all four probe modes passed at 7fb4de3; Default probe idle 0% of a core, 69.5 MiB private, synthetic F9 to cleanup 1.2 ms; 100 CPS request delivered 253 clicks in 3.07 s. Manual page checks passed for Toggles, Screenshot, Settings, tray exit and post-exit input; Actions was partial because the tester's automation could not click inside the picker and its injected input is (correctly) excluded from recording. A schema 11 profile loaded with no retired actions in it. Follow-up committed: desktop shell window ("Program Manager") no longer offered as a Click target; RELEASE_STATUS test count corrected to 254. Still to confirm by hand in the VM: Set Location + Test action receipt in TestTarget, F10 recording of real mouse/keyboard, and the dropped-actions notice on a schema ≤15 file that contains a text/branch step.
- 2026-10-13: Roy's hands-on VM pass confirmed record, save, load and actions work. Fixes from his feedback: with Loop on and the list ending in a pause, the pointer now travels back to the first click during that pause instead of jumping (regression test added, 255 tests); Help rewritten in plain language with headings and spacing; toggle list cells padded, Hotkey and Toggle columns centred; F10 starts recording immediately (no 3-second countdown; the idle time before the first click or key is dropped as before).
- 2026-10-13: Steam store page set up privately (not submitted for review; Publish tab untouched) for app 5440990: developer/publisher, website/forum/manual links, Windows requirements, English interface, Utilities category, short description and About text, legal line, support URL (GitHub issues), six 1920x1080 screenshots, four store capsules and four library assets generated from the brand logo in the browser (placeholders Roy can replace), shortcut icon and 184x184 app icon. Screenshots were captured with the real app and a seeded example macro; README now shows them (docs/screenshots). Found and fixed on the way: action-row labels stayed white after switching to light mode.

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

28. **Structure.** ~~Busy flags~~ replaced by `OperationGate` 2026-10-13; ~~two-class partial files and the MVVM stub~~ gone with the cleanup. Still open, low value: many files hold a record plus its helper type (about 30 files); splitting them is mechanical and only worth doing when a file is touched anyway.
29. ~~Duplicated logic.~~ Done 2026-10-13: one `TimingSample` for executor, action timing and click motion; the helper-set duplicates are gone; `WaitForClick`'s dead parameter and `ClickRehearsal` removed.
30. ~~Magic numbers.~~ Done 2026-10-13 for the default shortcuts and modifier scan codes. Win32 style and message constants in `StatusOverlay`, `TargetPreviewWindow` and `ScreenshotSelectionWindow` are still literals with comments; name them when those files are next touched.
31. **Readability.** `.editorconfig` added 2026-10-13. Measured and deliberately not applied: `dotnet format whitespace` would rewrite about 700 lines of the compact house style, and `AnalysisLevel=latest-recommended` raises about 100 distinct warnings (mostly CA1305 culture-sensitive strings and CA1401 visible P/Invokes) that would all be errors under `TreatWarningsAsErrors`. Do either as its own commit when there is time to review the diff. `LibraryImport` migration is not worth it: the app is untrimmed, and the hooks use delegates that the source generator does not support.
32. ~~Toolchain friendliness.~~ Done 2026-10-13: `global.json` rolls forward to the latest 10.0.4xx patch.
33. **CI.** ~~Hard-coded version~~ and ~~double build~~ fixed 2026-10-13: the release job publishes the artifact the build job tested. Still open: a PR check that `dotnet format --verify-no-changes` passes once 31 is done. The new workflow has not run on GitHub yet; the first push will show whether the artifact handoff works.
34. **Docs for a public repo.** ~~Third-party notices~~, ~~SECURITY.md~~, ~~issue and PR templates~~, ~~CHANGELOG.md~~ done 2026-10-13. Still open: screenshots of the new build in the README (Roy, the old screenshots show the private-era data path), a one-paragraph "is this allowed in my game" section, and shipping the Help text as a markdown file in `docs/`.
35. ~~Tests to add.~~ Done 2026-10-13 (spam vs cap is by design, so no test; the rest are in).
36. **Steam.** Steam installs under `steamapps/common` and users expect data in `%LocalAppData%` or Steam Cloud, not beside the exe; `portable.mode` needs a non-portable sibling. Steam builds are signed by nobody by default, so SmartScreen is not an issue there, but the Microsoft Store/Smart App Control story still needs the SignPath or paid cert route for the GitHub download. Decide whether the Steam build gets Steamworks (overlay, cloud saves, achievements are all optional for a utility). Store page must state plainly that it is a desktop utility and that games with anti-cheat may ban macro use.

## Jobs only Roy can do

- Merge PR #1 into `main` when happy with it (CI is green; VM session passed).
- In the VM: Load Macro on `docs/Handoff/VM-Reports/samples/schema15-retired-steps.mecprofile` and confirm the status says 3 retired actions were removed (one Wait remains). Record, save, load and actions are already confirmed by hand.
- Screenshots for the README are done (docs/screenshots); retake them whenever the UI changes.
- Decide the next version number and tag it (`vX.Y.Z` must equal the version in `Directory.Build.props`) when the next preview should go out; the release notes already describe schema 16.
- Steam (app 5440990, page filled privately, nothing submitted): check every field; set the Planned Release Date; add at least 5 tags via "Add tags now" (opens the public store tag editor); open the Library "Placement Tool" (popup) and save a logo position so Library Assets turns green; add a support e-mail if you want one; replace the generated capsules/library art if you want nicer artwork; decide where a Steam build keeps its data (item 36). Then submit for review yourself.
- Give the hook-loss detection (item 3) and the second Stop All route (item 9) a test session in the VM once they are built.

## What is good

The input ownership model is the strongest part: one dispatch gate, releases in reverse order, only accepted downs enter `owned`, cancellation releases independently of a blocked scheduler, and the engine refuses to release a user's physical hold. Every injected event is tagged and the hooks filter injected flags, so the app never mistakes its own input for the user's. The P/Invoke layouts, SendInput normalisation under PerMonitorV2, GDI and D3D cleanup, and the dedicated pumping thread for hooks are all correct. Core and Engine have no desktop dependency and a reflection test enforces it. The compiler bounds worst-case duration and action counts before anything runs. The fake-clock test suite is deterministic and asserts exact timestamps and edge order. Storage writes are atomic with validated backups and bounded sizes. CI pins actions by SHA with least-privilege permissions, and the release ZIP carries a per-file manifest the verify script checks.
