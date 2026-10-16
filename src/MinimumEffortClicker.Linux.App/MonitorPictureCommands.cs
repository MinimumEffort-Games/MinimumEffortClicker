using Avalonia.Controls;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    /// <summary>
    /// Desktop mode: minimises the clicker (its dialogs go with it) and hides the status overlay, takes a still picture of
    /// every monitor through the GNOME helper, then brings the clicker back. All pictures are taken first, so the picker
    /// never photographs itself. Sends no input.
    /// </summary>
    internal async Task<(IReadOnlyList<DesktopMonitor> Monitors, IReadOnlyList<WindowSnapshot> Pictures)> CaptureMonitorPicturesAsync(CancellationToken stop)
    {
        if (runtime is null) throw new InvalidOperationException("The clicker is still starting.");
        var monitors = runtime.ListMonitors();
        if (monitors.Count == 0) throw new InvalidOperationException("GNOME reported no monitors.");
        var previous = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        // The screenshot flag keeps the status overlay hidden while the pictures are taken.
        bool wasBusy = screenshotBusy; screenshotBusy = true;
        overlay?.Hide(); overlayFollowTimer.Stop();
        try
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            WindowState = WindowState.Minimized;
            // Wait until GNOME shows it minimised and the animation has ended before copying pixels.
            await WaitUntilMinimized(stop);
            var pictures = await Task.Run(() => monitors.Select(m => runtime.CaptureMonitorPreview(m, stop)).ToArray(), stop);
            return (monitors, pictures);
        }
        finally
        {
            screenshotBusy = wasBusy;
            if (!closing)
            {
                // GNOME refuses an X11 window's own request to come back; the helper does not.
                WindowState = previous;
                runtime.ActivateOwnWindow(Title ?? "");
            }
            RefreshStatus();
        }
    }
}
