using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    /// <summary>
    /// Last test result per action, kept with the exact step that was tested: editing the action, loading a macro or
    /// clearing the list makes the result disappear because the step is no longer the same one.
    /// </summary>
    private readonly Dictionary<Guid, (SequenceStep Step, bool Passed, string Detail)> rowTests = [];
    private (bool? Passed, string Detail) RowTestResult(SequenceStep step) =>
        rowTests.TryGetValue(step.Id, out var tested) && Equals(tested.Step, step) ? (tested.Passed, tested.Detail) : (null, "Test only this action. The clicker minimises while it runs.");

    /// <summary>
    /// The Test button on an action row: minimise, run only that action against the Click target, restore, and colour the
    /// button green or red. A colour or image wait is checked once (true now or not). Stop All (F9) aborts.
    /// </summary>
    private async void RowTest_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: QuickRow row } || runtime is null) return;
        e.Handled = true;
        if (screenshotBusy || quickOperation || macroFileBusy || ioBusy || runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending)
        { ReasonLabel.Text = "Finish or stop the current run before testing an action."; return; }
        var step = row.Step with { Enabled = true };
        quickOperation = true;
        WindowState? minimizedFrom = null;
        bool passed = false; string detail;
        using var cancellation = new CancellationTokenSource(); quickCancellation = cancellation;
        void Cancel(string reason) { _ = cancellation.CancelAsync(); }
        runtime.Control.StopRequested += Cancel; RefreshStatus();
        try
        {
            if (runtime.IsDesktopMode)
            {
                var frame = runtime.GetTargetFrame();
                SequenceCompiler.Compile(new([step]), quickPoints).ValidateDesktopMode(frame.Width, frame.Height, frame.Dpi);
            }
            if (WindowState != WindowState.Minimized) { minimizedFrom = WindowState; WindowState = WindowState.Minimized; }
            if (!runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            if (step.Condition is { } condition && step.Kind == StepKind.Condition)
            {
                // Let the minimise animation finish so the clicker is not in the picture.
                await Task.Delay(300, cancellation.Token);
                (passed, detail) = await CheckConditionOnce(condition, cancellation.Token);
            }
            else
            {
                // A click at the cursor needs time to put the mouse where it should click; everything else starts after the
                // window has settled in front.
                bool atCursor = IsClick(step) && (step.Position ?? step.Children?.FirstOrDefault()?.Position) is null;
                var plan = SequenceCompiler.Compile(quickHistory.Current with { Steps = [step], Repetition = new() }, quickPoints);
                if (!runtime.TryStartQuick(plan, 1, TimeSpan.FromMilliseconds(atCursor ? 3000 : 300), ResizeClicksBox.SelectedIndex == 0))
                    throw new InvalidOperationException(runtime.Snapshot.Reason);
                while (runtime.Snapshot.Busy)
                {
                    await Task.Delay(50, cancellation.Token);
                    // A test is one action: if its window left the front, stop rather than wait.
                    if (runtime.CanResume) { runtime.Stop("Test stopped: the Click target left the front."); break; }
                }
                passed = runtime.Snapshot.State == "Completed";
                detail = passed ? "Passed: the action ran." : "Failed: " + runtime.Snapshot.Reason;
                if (passed && IsClick(step) && runtime.PointerPosition is { } pointer) ClickPingWindow.At(pointer.X, pointer.Y);
            }
        }
        catch (OperationCanceledException) { runtime.Stop("Action test aborted."); detail = "Aborted with Stop All."; }
        catch (Exception ex) { detail = "Failed: " + ex.Message; }
        finally
        {
            runtime.Control.StopRequested -= Cancel; quickCancellation = null; quickOperation = false;
            RestoreDesktopOperationWindow(minimizedFrom); RefreshStatus();
        }
        rowTests[step.Id] = (row.Step, passed, detail);
        RefreshQuickList(QuickActionList.SelectedIndex, preserveEditor: true);
        ReasonLabel.Text = $"Test of action {row.Index + 1}: {detail}";
    }

    private static bool IsClick(SequenceStep step) => step.Control?.Kind == InputKind.MouseButton
        || step.Children is { Count: > 0 } children && children.All(c => c.Control.Kind == InputKind.MouseButton);

    /// <summary>Looks at a colour or image wait once: green when it is true right now.</summary>
    private async Task<(bool Passed, string Detail)> CheckConditionOnce(ConditionDefinition condition, CancellationToken stop)
    {
        using var first = CancellationTokenSource.CreateLinkedTokenSource(stop);
        first.CancelAfter(TimeSpan.FromSeconds(5));
        ConditionObservationState? seen = null;
        try
        {
            await Task.Run(() => runtime!.TestCondition(condition, first.Token, observation => { seen ??= observation.State; first.Cancel(); }), first.Token);
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { }
        return seen switch
        {
            ConditionObservationState.Met => (true, "True right now."),
            ConditionObservationState.NotMet => (false, "False right now: the colour or image is not there."),
            ConditionObservationState.Ambiguous => (false, "More than one image match. Make the area smaller."),
            ConditionObservationState.Unavailable => (false, "The Click target could not be captured. Bring it in front and try again."),
            _ => (false, "No picture of the Click target arrived within 5 seconds."),
        };
    }
    private bool recordKeyChanging;
    private int appliedRecordKey = 0x79, selectedRecordKey = 0x79;

    private async Task SetQuickRecordKey()
    {
        if (runtime is null || recordKeyChanging || !ShortcutKeys.Supported(selectedRecordKey)) return;
        recordKeyChanging = true; RefreshStatus();
        try
        {
            int key = selectedRecordKey;
            bool ready = await runtime.Control.ConfigureRecordingAsync(key);
            string failure = "";
            if (ready) { appliedRecordKey = key; PersistPreferences(); }
            else
            {
                bool previousLoading = loadingQuick; loadingQuick = true;
                selectedRecordKey = appliedRecordKey; loadingQuick = previousLoading;
                bool restored = await runtime.Control.ConfigureRecordingAsync(appliedRecordKey);
                failure = restored ? "That record key is already in use. The previous key was kept."
                    : "The recording hotkey is unavailable. Choose another key.";
            }
            QuickRecordKeyBox.Content = ShortcutKeys.Label(appliedRecordKey);
            QuickRecordButton.Content = $"{ShortcutKeys.Label(appliedRecordKey)} Record/Stop";
            QuickRecordStatus.Text = ready ? "" : failure;
            if (!ready) ReasonLabel.Text = failure;
        }
        catch (Exception ex) { QuickRecordStatus.Text = ex.Message; }
        finally { recordKeyChanging = false; RefreshStatus(); }
    }
    private void QuickRecordHotkey() => Dispatcher.BeginInvoke(async () =>
    {
        if (!quickEditorOpen && (quickOperation || currentPage == 0)) await RecordQuick(fromHotkey: true);
    });
    private async void QuickRecord_Click(object sender, RoutedEventArgs e) => await RecordQuick(fromHotkey: false);
    /// <summary>
    /// The record button hides the clicker, brings the target forward and records from the first input.
    /// The recording hotkey records at once and keeps the pause from the key press to the first input.
    /// </summary>
    private async Task RecordQuick(bool fromHotkey)
    {
        if (quickRecorder is not null) { quickRecorder.Stop(); return; }
        if (quickCancellation is not null) { quickCancellation.Cancel(); runtime?.Stop("Current operation cancelled."); return; }
        if (runtime is null || quickOperation || macroFileBusy || ioBusy || runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) return;
        bool includePauses = RecordDelaysBox.IsChecked == true;
        bool movement = RecordMovementBox.IsChecked == true;
        quickOperation = true;
        WindowState? minimizedFrom = null;
        using var cancellation = new CancellationTokenSource(); quickCancellation = cancellation;
        void Cancel(string reason) { _ = cancellation.CancelAsync(); }
        runtime.Control.StopRequested += Cancel; RefreshStatus();
        try
        {
            if ((runtime.IsDesktopMode || !fromHotkey) && WindowState != WindowState.Minimized)
            { minimizedFrom = WindowState; WindowState = WindowState.Minimized; }
            if (!runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            cancellation.Token.ThrowIfCancellationRequested();
            using var recorder = new DesktopRecorder(runtime, new(Keyboard: true, MouseButtons: true, PointerPath: true));
            quickRecorder = recorder; RefreshStatus();
            QuickRecordStatus.Text = $"Recording clicks and keys… {ShortcutKeys.Label(appliedRecordKey)} or {stopKey} finishes.";
            // Show captured actions as they happen; the list is rebuilt from the full draft on stop.
            int shown = recorder.LiveVersion;
            var liveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) =>
            {
                if (quickRecorder != recorder || recorder.LiveVersion == shown) return;
                shown = recorder.LiveVersion;
                try
                {
                    var live = RecordedSteps(recorder.LivePreview());
                    ShowRecordingPreview(live.Steps);
                    QuickRecordStatus.Text = $"Recording… {live.Steps.Count(s => s.Kind != StepKind.Wait)} actions so far. {ShortcutKeys.Label(appliedRecordKey)} or {stopKey} finishes.";
                }
                catch (Exception) { } // A preview that cannot be built yet is simply not shown.
            });
            string reason;
            try { reason = await recorder.Finished; }
            finally { liveTimer.Stop(); }
            var draft = await recorder.GetDraftAsync();
            quickRecorder = null;
            var sequence = RecordedSteps(draft.Sequence);
            if (!sequence.Steps.Any(s => s.Kind != StepKind.Wait)) { QuickRecordStatus.Text = "No clicks or keys captured. " + reason; return; }
            if (quickHistory.Current.KeyboardLayout is { } layout && sequence.KeyboardLayout is { } recorded && !string.Equals(layout, recorded, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The recording uses a different keyboard layout. Use a new macro for it.");
            quickHistory.Commit(quickHistory.Current with { Steps = [.. quickHistory.Current.Steps, .. sequence.Steps], KeyboardLayout = quickHistory.Current.KeyboardLayout ?? sequence.KeyboardLayout });
            RefreshQuickList();
            QuickRecordStatus.Text = $"Added {sequence.Steps.Count} actions. Select any row to edit. " + string.Join(" ", draft.Warnings);
            ReasonLabel.Text = reason;
        }
        catch (OperationCanceledException) { QuickRecordStatus.Text = "Recording cancelled."; }
        catch (Exception ex) { QuickRecordStatus.Text = "Recording ended: " + ex.Message; }
        finally
        {
            runtime.Control.StopRequested -= Cancel; quickRecorder = null; quickCancellation = null; quickOperation = false; RestoreDesktopOperationWindow(minimizedFrom); RefreshStatus();
            if (QuickActionList.ItemsSource is QuickRow[] rows && rows.Length != quickHistory.Current.Steps.Count) RefreshQuickList();
        }
        SequenceDefinition RecordedSteps(SequenceDefinition recording)
        {
            var sequence = QuickActions.RecordedActions(recording, includePauses, movement);
            if (fromHotkey && includePauses && recording.Steps is [{ Kind: StepKind.Wait } lead, ..] && sequence.Steps.Count > 0)
                sequence = sequence with { Steps = [QuickActions.Pause(lead.Duration), .. sequence.Steps] };
            return sequence;
        }
    }
}
