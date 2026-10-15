using System.Windows;
using System.Windows.Threading;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    /// <summary>
    /// Last test result per action, kept with the exact step that was tested: editing the action, loading a macro or
    /// clearing the list makes the result disappear because the step is no longer the same one. Passed is null when the
    /// action ran but has no true/false answer (a click, key or screenshot is tested by doing it).
    /// </summary>
    private readonly Dictionary<Guid, (SequenceStep Step, bool? Passed, string Detail)> rowTests = [];
    private (bool? Passed, string Detail) RowTestResult(SequenceStep step) =>
        rowTests.TryGetValue(step.Id, out var tested) && Equals(tested.Step with { Enabled = step.Enabled, Once = step.Once }, step)
            ? (tested.Passed, tested.Detail) : (null, "Test the selected action. The clicker minimises while it runs.");

    /// <summary>
    /// Test Selected Action: gray until the selected action has been tested, then green (a wait that is true now) or red
    /// (a wait that is false now, or an action that could not run). A click or key is tested by doing it, so it stays gray.
    /// </summary>
    private void ShowTestSelectedButton()
    {
        var row = QuickActionList.SelectedItem as QuickRow;
        bool idle = runtime is { } current && !current.Snapshot.Busy && !current.Snapshot.CleanupPending && operations.Idle;
        TestSelectedButton.IsEnabled = idle && row is { Step.Kind: not StepKind.Wait };
        var (passed, detail) = row is null ? (null, "Select an action in the list, then test it.")
            : row.Step.Kind == StepKind.Wait ? (null, "A pause has nothing to test.") : RowTestResult(row.Step);
        TestSelectedButton.Style = passed is true ? (Style)FindResource("GreenButton") : passed is false ? (Style)FindResource("RedButton") : TryFindResource(typeof(System.Windows.Controls.Button)) as Style;
        TestSelectedButton.ToolTip = detail;
    }

    /// <summary>
    /// Minimise, run only the selected action against the Click target, restore, and show the result on the button.
    /// A colour or image wait is checked once (true now or not). Stop All (F9) aborts.
    /// </summary>
    private async void TestSelected_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null) return;
        if (QuickActionList.SelectedItem is not QuickRow row) { ReasonLabel.Text = "Select an action in the list, then test it."; return; }
        if (!operations.Idle || runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending)
        { ReasonLabel.Text = "Finish or stop the current run before testing an action."; return; }
        var step = row.Step with { Enabled = true };
        operations.Begin(Operation.Quick);
        WindowState? minimizedFrom = null;
        bool? passed = null; string detail;
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
                // The action itself is the result: a click shows a ring where it landed. Only a failure colours the button.
                bool ran = runtime.Snapshot.Completed;
                passed = ran ? null : false;
                detail = ran ? "Ran: the action was sent." : "Failed: " + runtime.Snapshot.Reason;
                if (ran && IsClick(step)) ShowClickPing(step);
            }
        }
        catch (OperationCanceledException) { runtime.Stop("Action test aborted."); detail = "Aborted with Stop All."; }
        catch (Exception ex) { passed = false; detail = "Failed: " + ex.Message; }
        finally
        {
            runtime.Control.StopRequested -= Cancel; quickCancellation = null; operations.End(Operation.Quick);
            RestoreDesktopOperationWindow(minimizedFrom); RefreshStatus();
        }
        rowTests[step.Id] = (row.Step, passed, detail);
        if (passed == false) AppLog.Note($"Test of action {row.Index + 1} ({row.Step.Name})", detail);
        ShowTestSelectedButton();
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
        if (!operations.Has(Operation.Dialog) && (operations.Has(Operation.Quick) || currentPage == "QuickPage")) await RecordQuick(fromHotkey: true);
    });
    private async void QuickRecord_Click(object sender, RoutedEventArgs e) => await RecordQuick(fromHotkey: false);
    /// <summary>
    /// With Minimize on record ticked, the Record button minimises the clicker so the first click lands in the target; unticked,
    /// it stays where it is. The hotkey leaves it
    /// where it is with a window target, and keeps the time from the key press to the first input as a pause.
    /// </summary>
    private async Task RecordQuick(bool fromHotkey)
    {
        if (quickRecorder is not null) { quickRecorder.Stop(); return; }
        if (quickCancellation is not null) { quickCancellation.Cancel(); runtime?.Stop("Current operation cancelled."); return; }
        if (runtime is null || !operations.Idle || runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) return;
        bool includePauses = RecordDelaysBox.IsChecked == true;
        bool minimize = MinimizeOnRecordBox.IsChecked == true;
        bool movement = RecordMovementBox.IsChecked == true;
        operations.Begin(Operation.Quick);
        WindowState? minimizedFrom = null;
        using var cancellation = new CancellationTokenSource(); quickCancellation = cancellation;
        void Cancel(string reason) { _ = cancellation.CancelAsync(); }
        runtime.Control.StopRequested += Cancel; RefreshStatus();
        try
        {
            if (minimize && (runtime.IsDesktopMode || !fromHotkey) && WindowState != WindowState.Minimized)
            { minimizedFrom = WindowState; WindowState = WindowState.Minimized; }
            if (!runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            // Recording starts the moment the key or button is pressed. The target was just brought forward, so give the
            // foreground switch a few short retries instead of a fixed countdown.
            DesktopRecorder? started = null;
            for (int attempt = 0; started is null; attempt++)
            {
                try { started = new DesktopRecorder(runtime, new(Keyboard: true, MouseButtons: true, PointerPath: true)); }
                catch (InvalidOperationException) when (attempt < 10) { await Task.Delay(50, cancellation.Token); }
            }
            using var recorder = started;
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
            }, Dispatcher);
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
        catch (Exception ex) { QuickRecordStatus.Text = "Recording ended: " + ex.Message; AppLog.Failure("Recording", ex); Activate(); }
        finally
        {
            runtime.Control.StopRequested -= Cancel; quickRecorder = null; quickCancellation = null; operations.End(Operation.Quick); RestoreDesktopOperationWindow(minimizedFrom); RefreshStatus();
            // A cancelled or empty recording leaves preview rows behind; put the real list back.
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
