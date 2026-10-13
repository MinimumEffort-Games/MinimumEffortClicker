using System.Windows;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Windows;

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
    private async void RowTest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: QuickRow row } || runtime is null) return;
        e.Handled = true;
        if (!operations.Idle || runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending)
        { ReasonLabel.Text = "Finish or stop the current run before testing an action."; return; }
        var step = row.Step with { Enabled = true };
        operations.Begin(Operation.Quick);
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
            if (!step.LeaveCursorFree && !runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            if (step.Condition is { } condition && step.Kind == StepKind.Condition)
                (passed, detail) = await CheckConditionOnce(condition, cancellation.Token);
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
                passed = runtime.Snapshot.Completed;
                detail = passed ? "Passed: the action ran." : "Failed: " + runtime.Snapshot.Reason;
                if (passed && IsClick(step)) ShowClickPing(step);
            }
        }
        catch (OperationCanceledException) { runtime.Stop("Action test aborted."); detail = "Aborted with Stop All."; }
        catch (Exception ex) { detail = "Failed: " + ex.Message; }
        finally
        {
            runtime.Control.StopRequested -= Cancel; quickCancellation = null; operations.End(Operation.Quick);
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

    /// <summary>Where a tested click landed: the pointer for normal clicks; the saved point for cursor-free clicks.</summary>
    private void ShowClickPing(SequenceStep step)
    {
        if (!step.LeaveCursorFree) { ClickPingWindow.AtCursor(); return; }
        try
        {
            if ((step.Position ?? step.Children?.FirstOrDefault(c => c.Position is not null)?.Position) is not { } point) return;
            var frame = runtime!.GetTargetFrame();
            var resolved = point.InFrame(frame.Width, frame.Height, frame.Dpi);
            bool desktop = resolved.Space == CoordinateSpace.Desktop;
            new ClickPingWindow(desktop ? resolved.X : frame.X + resolved.X, desktop ? resolved.Y : frame.Y + resolved.Y).Show();
        }
        catch (InvalidOperationException) { }
    }
    private int appliedRecordKey = ShortcutKeys.DefaultRecordKey, selectedRecordKey = ShortcutKeys.DefaultRecordKey;

    private async Task SetQuickRecordKey()
    {
        if (runtime is null || operations.Has(Operation.RecordKey) || !ShortcutKeys.Supported(selectedRecordKey)) return;
        operations.Begin(Operation.RecordKey); RefreshStatus();
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
        finally { operations.End(Operation.RecordKey); RefreshStatus(); }
    }
    private void QuickRecordHotkey() => Dispatcher.BeginInvoke(async () =>
    {
        if (!operations.Has(Operation.Dialog) && (operations.Has(Operation.Quick) || currentPage == "QuickPage")) await RecordQuick();
    });
    private async void QuickRecord_Click(object sender, RoutedEventArgs e) => await RecordQuick();
    private async Task RecordQuick()
    {
        if (quickRecorder is not null) { quickRecorder.Stop(); return; }
        if (quickCancellation is not null) { quickCancellation.Cancel(); runtime?.Stop("Current operation cancelled."); return; }
        if (runtime is null || !operations.Idle || runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) return;
        bool includePauses = RecordDelaysBox.IsChecked == true;
        bool movement = RecordMovementBox.IsChecked == true;
        operations.Begin(Operation.Quick);
        WindowState? minimizedFrom = null;
        using var cancellation = new CancellationTokenSource(); quickCancellation = cancellation;
        void Cancel(string reason) { _ = cancellation.CancelAsync(); }
        runtime.Control.StopRequested += Cancel; RefreshStatus();
        try
        {
            if (runtime.IsDesktopMode && WindowState != WindowState.Minimized)
            { minimizedFrom = WindowState; WindowState = WindowState.Minimized; }
            if (!runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            // Recording starts the moment the key is pressed; the idle time before the first click or key is dropped from the list.
            // The target was just brought forward, so give the foreground switch a few short retries instead of a fixed countdown.
            DesktopRecorder? started = null;
            for (int attempt = 0; started is null; attempt++)
            {
                try { started = new DesktopRecorder(runtime, new(Keyboard: true, MouseButtons: true, PointerPath: true)); }
                catch (InvalidOperationException) when (attempt < 10) { await Task.Delay(50, cancellation.Token); }
            }
            using var recorder = started;
            quickRecorder = recorder; RefreshStatus();
            QuickRecordStatus.Text = $"Recording clicks and keys… {ShortcutKeys.Label(appliedRecordKey)} or {stopKey} finishes.";
            string reason = await recorder.Finished;
            var draft = await recorder.GetDraftAsync();
            quickRecorder = null;
            var sequence = QuickActions.RecordedActions(draft.Sequence, includePauses, movement);
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
            runtime.Control.StopRequested -= Cancel; quickRecorder = null; quickCancellation = null; operations.End(Operation.Quick); RestoreDesktopOperationWindow(minimizedFrom); RefreshStatus();
        }
    }
}
