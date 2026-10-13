using System.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private async void PausePlayback_Click(object sender, RoutedEventArgs e)
    {
        if (quickOperation || runtime is null) return;
        if (runtime.CanResume) await ResumeActionsAsync(); else runtime.Pause();
    }
    private async Task ResumeActionsAsync()
    {
        if (runtime is null) return;
        WindowState? previous = null;
        bool accepted = false;
        using var stop = new CancellationTokenSource();
        long intent = runtime.StopVersion;
        quickOperation = true; quickCancellation = stop;
        try
        {
            if (runtime.IsDesktopMode) { previous = WindowState; WindowState = WindowState.Minimized; }
            if (!runtime.BringTargetForward(resume: true)) return;
            RefreshStatus();
            // Let the initiating press release and foreground handoff settle, with no countdown.
            await Task.Delay(200, stop.Token);
            if (intent != runtime.StopVersion || closing || allowClose) return;
            accepted = runtime.TryResume();
            if (accepted) desktopPlaybackFrom = previous;
        }
        catch (OperationCanceledException) { }
        finally
        {
            quickOperation = false; quickCancellation = null;
            if (!accepted) RestoreDesktopOperationWindow(previous);
            RefreshStatus();
        }
    }
}
public partial class SequenceEditor
{
    private void Editor_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (AuthoringScroll is null || PlaybackScroll is null) return;
        // Keep a usable action list/inspector at minimum height; keyboard focus scrolls tools into view.
        AuthoringScroll.MaxHeight = Math.Clamp(ActualHeight * 0.25, 120, 180);
        PlaybackScroll.MaxHeight = Math.Clamp(ActualHeight * 0.25, 140, 180);
    }
    private void PausePlayback_Click(object sender, RoutedEventArgs e) => runtime.Pause();
    private async void ResumePlayback_Click(object sender, RoutedEventArgs e)
    {
        ResumePlaybackButton.IsEnabled = false;
        await runtime.ResumeAfterCountdownAsync(); RefreshRuntime();
    }
}
