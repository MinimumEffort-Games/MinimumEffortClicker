# Linux catch-up (for the Claude session in the Ubuntu VM)

Paste this to the session in the VM:

> Update `main` (`git fetch && git checkout main && git pull --ff-only`), create a branch `linux-catch-up`, and read
> `AGENTS.md`, `docs/Handoff/Where-We-Are.md` and this file. Then bring the Linux app level with the Windows app, one
> item at a time, building with `scripts/linux/build.sh` and testing with `scripts/linux/test.sh` after each, and
> commit each item on that branch with a message that says what changed for the player, then push it and open a
> pull request into `main` (main is protected; Roy merges). Do not touch the
> Windows projects. Report what you could and could not verify under GNOME.

## Where things stand (2026-10-13)

The Linux preview and a day of Windows work are now both on `main` (PR #1). The Windows work was done on top of the
original code. To make Linux build again, these features were removed from the Linux code because the shared code no
longer has them and no Linux screen reaches them: run-together helper sets, logical and Unicode keys, finish-loop,
resume countdown, check-only waits, overlay placement (anchor/drag) and subsequences. The Linux solution builds with
0 warnings and its tests pass.

The rule for both apps: the four pages (Actions, Toggles, Screenshot, Settings) are the whole product. Code no page
can reach is deleted, not kept.

## To do on Linux, in this order

1. **Auto-resume when the target window comes back.** The shared engine now pauses instead of ending a run when the
   target leaves the front, if the runtime sets `ClickExecutor.PauseOnTargetLoss`. The Windows runtime
   (`src/MinimumEffortClicker.Windows/DesktopRuntime.cs`: `PauseForTargetLoss`, `AutoResumeTick`, the
   `PlaybackPaused` callback, `TryResume(automatic)`) sets it for window targets and resumes once the window has
   been in front for 0.5 s. Port that to `src/MinimumEffortClicker.Linux/DesktopRuntime.cs`. Also wrap the window
   capture in the condition observer so a failed capture reports "unavailable" instead of throwing (see
   `src/MinimumEffortClicker.Windows/SequenceConditions.cs`).
2. **Test button on each Action List row.** Remove `TestActionButton` from the create panel. Each row gets a Test
   button under its Active box: minimise, run only that action, restore, colour the button green or red with the
   reason as tooltip; clicks show a one-second ring where they landed; colour/image waits are checked once; pause
   rows have no Test button. Windows: `QuickTestingRecording.cs` (`RowTest_Click`, `CheckConditionOnce`),
   `ClickPingWindow.cs`, the row template in `MainWindow.xaml`.
3. **Recording starts at once.** F10 starts recording immediately; the 3-second countdown is gone
   (`QuickTestingRecording.cs`, `RecordQuick`).
4. **Help rewritten** in plain language with headings, bullets and spacing (`HelpCommands.cs` on Windows: the
   `HelpSections` table). Keep the Linux-specific lines (GNOME helper, input access).
5. **Toggle list spacing:** padded cells, Hotkey and Toggle columns centred.
6. **Action rows follow the theme:** rebuild the rows when light/dark changes so their labels are readable.
7. **Wait-until on any monitor in Desktop mode.** Windows shows a Monitor list in the colour/area picker and stores the
   area in screen pixels (`RegionPicker.cs`, `MonitorPictureCommands.cs`, `DesktopMonitor.cs`). Under Wayland the
   screen copy goes through the GNOME helper; say what is possible before building it.
8. **Remove unreachable Linux code:** profile-library handlers in `ProfileCommands.cs` (`DeleteLibrary_Click` and
   others with no button), the hidden `AnchorOverlayBox`, and anything else no Linux page reaches.

Check each against `docs/Handoff/Where-We-Are.md` for the exact behaviour Roy asked for.
