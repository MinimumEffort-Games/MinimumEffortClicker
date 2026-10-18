using System.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private async void PausePlayback_Click(object sender, RoutedEventArgs e)
    {
        if (operations.Has(Operation.Quick) || runtime is null) return;
        if (runtime.CanResume) await ResumeActionsAsync(); else runtime.Pause();
    }
    private async Task ResumeActionsAsync()
    {
        if (runtime is null) return;
        StepAside? away = null;
        bool accepted = false;
        using var stop = new CancellationTokenSource();
        long intent = runtime.StopVersion;
        operations.Begin(Operation.Quick); quickCancellation = stop;
        try
        {
            away = StepAsideForTarget(MinimizeOnStartBox.IsChecked == true);
            if (!runtime.BringTargetForward(resume: true)) return;
            RefreshStatus();
            // Let the initiating press release and foreground handoff settle, with no countdown.
            await Task.Delay(200, stop.Token);
            if (intent != runtime.StopVersion || closing || allowClose) return;
            accepted = runtime.TryResume();
            if (accepted && playbackAway is null) { playbackAway = away; playbackSeenActive = false; }
        }
        catch (OperationCanceledException) { }
        finally
        {
            operations.End(Operation.Quick); quickCancellation = null;
            if (!accepted) ComeBack(away);
            RefreshStatus();
        }
    }
}
