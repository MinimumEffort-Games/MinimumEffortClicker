using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;

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
            if (runtime.IsDesktopMode && MinimizeOnStartBox.IsChecked == true) { previous = WindowState; WindowState = WindowState.Minimized; }
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
