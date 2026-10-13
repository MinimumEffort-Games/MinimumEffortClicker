using System.Windows;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private void AbortTest_Click(object sender, RoutedEventArgs e)
    { quickCancellation?.Cancel(); if (quickRecorder is null) runtime?.Stop("Action test aborted."); }
    private async void TestAction_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || screenshotBusy || quickOperation || macroFileBusy || ioBusy || runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) return;
        SequenceStep step;
        try { step = ReadQuickAction() with { Enabled = true }; }
        catch (Exception ex) { TestResultLabel.Text = ex.Message; return; }
        quickOperation = true;
        WindowState? minimizedFrom = null;
        bool observing = true;
        using var cancellation = new CancellationTokenSource(); quickCancellation = cancellation;
        void Cancel(string reason) { _ = cancellation.CancelAsync(); }
        runtime.Control.StopRequested += Cancel;
        AbortTestButton.Visibility = Visibility.Visible; RefreshStatus();
        try
        {
            if (runtime.IsDesktopMode)
            {
                var frame = runtime.GetTargetFrame();
                SequenceCompiler.Compile(new([step]), quickPoints).ValidateDesktopMode(frame.Width, frame.Height, frame.Dpi);
                if (WindowState != WindowState.Minimized)
                { minimizedFrom = WindowState; WindowState = WindowState.Minimized; }
            }
            TestResultLabel.Text = "Test starts in 3 seconds. Only this action will run.";
            if (!step.LeaveCursorFree && !runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            await Task.Delay(3000, cancellation.Token);
            if (step.Condition is { } condition && step.Kind == StepKind.Condition)
            {
                TestResultLabel.Text = "Scanning… Stop All or Abort test cancels.";
                var result = await Task.Run(() => runtime.TestCondition(condition, cancellation.Token, observation => Dispatcher.BeginInvoke(() =>
                {
                    if (!observing || cancellation.IsCancellationRequested) return;
                    TestResultLabel.Text = observation.State switch
                    { ConditionObservationState.NotMet => "False — still scanning. Stop All aborts.", ConditionObservationState.Met => "Match seen — confirming…", ConditionObservationState.Ambiguous => "More than one image match. Narrow the area.", _ => "Target unavailable." };
                })), cancellation.Token);
                observing = false;
                TestResultLabel.Text = result.Outcome == ConditionOutcome.Matched ? $"True — matched after {result.Samples} sample{(result.Samples == 1 ? "" : "s")}." : $"Test ended: {result.Outcome}.";
            }
            else
            {
                var plan = SequenceCompiler.Compile(quickHistory.Current with { Steps = [step], Repetition = new() }, quickPoints);
                if (!runtime.TryStartQuick(plan, 1, TimeSpan.FromMilliseconds(200), ResizeClicksBox.SelectedIndex == 0)) throw new InvalidOperationException(runtime.Snapshot.Reason);
                while (runtime.Snapshot.Busy) { await Task.Delay(100, cancellation.Token); if (runtime.CanResume) { runtime.Stop("Test paused by safety; test again when ready."); break; } }
                TestResultLabel.Text = runtime.Snapshot.Completed ? "Test completed — one action ran." : "Test ended: " + runtime.Snapshot.Reason;
            }
        }
        catch (OperationCanceledException) { runtime.Stop("Action test aborted."); TestResultLabel.Text = "Test aborted. No further scanning or input."; }
        catch (Exception ex) { TestResultLabel.Text = "Test could not finish: " + ex.Message; }
        finally
        {
            observing = false;
            runtime.Control.StopRequested -= Cancel; quickCancellation = null; quickOperation = false;
            AbortTestButton.Visibility = Visibility.Collapsed; RestoreDesktopOperationWindow(minimizedFrom); RefreshStatus();
        }
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
        if (!quickEditorOpen && (quickOperation || currentPage == "QuickPage")) await RecordQuick();
    });
    private async void QuickRecord_Click(object sender, RoutedEventArgs e) => await RecordQuick();
    private async Task RecordQuick()
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
            if (runtime.IsDesktopMode && WindowState != WindowState.Minimized)
            { minimizedFrom = WindowState; WindowState = WindowState.Minimized; }
            if (!runtime.BringTargetForward()) throw new InvalidOperationException(runtime.Snapshot.Reason);
            QuickRecordStatus.Text = $"Recording starts in 3 seconds. {ShortcutKeys.Label(appliedRecordKey)} or {stopKey} finishes.";
            await Task.Delay(3000, cancellation.Token);
            using var recorder = new DesktopRecorder(runtime, new(Keyboard: true, MouseButtons: true, PointerPath: true));
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
        catch (OperationCanceledException) { QuickRecordStatus.Text = "Recording countdown cancelled."; }
        catch (Exception ex) { QuickRecordStatus.Text = "Recording ended: " + ex.Message; }
        finally
        {
            runtime.Control.StopRequested -= Cancel; quickRecorder = null; quickCancellation = null; quickOperation = false; RestoreDesktopOperationWindow(minimizedFrom); RefreshStatus();
        }
    }
}
