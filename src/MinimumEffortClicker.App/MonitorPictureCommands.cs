using System.Windows;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Vision;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    /// <summary>
    /// Desktop mode: minimises the clicker (its dialogs go with it) and hides the status overlay, takes a still picture of
    /// every monitor, then brings the clicker back. All pictures are taken first, so the picker never photographs itself.
    /// Sends no input.
    /// </summary>
    internal async Task<(IReadOnlyList<DesktopMonitor> Monitors, IReadOnlyList<WindowSnapshot> Pictures)> CaptureMonitorPicturesAsync(CancellationToken stop)
    {
        if (runtime is null) throw new InvalidOperationException("The clicker is still starting.");
        var monitors = DesktopMonitor.List();
        if (monitors.Count == 0) throw new InvalidOperationException("Windows reported no monitors.");
        var previous = WindowState;
        // The screenshot flag keeps the status overlay hidden while the pictures are taken.
        operations.Begin(Operation.Screenshot);
        overlay?.Hide(); overlayFollowTimer.Stop();
        try
        {
            if (WindowState != WindowState.Minimized) WindowState = WindowState.Minimized;
            await Task.Delay(400, stop); // Let the minimise animation finish before copying pixels.
            var pictures = await Task.Run(() => monitors.Select(m => runtime.CaptureMonitorPreview(m, stop)).ToArray(), stop);
            return (monitors, pictures);
        }
        finally
        {
            operations.End(Operation.Screenshot);
            if (WindowState == WindowState.Minimized && !closing) WindowState = previous == WindowState.Minimized ? WindowState.Normal : previous;
            RefreshStatus();
        }
    }
}
