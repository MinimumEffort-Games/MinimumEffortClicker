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
        var previous = WindowState;
        // The screenshot flag keeps the status overlay hidden while the pictures are taken.
        bool wasBusy = screenshotBusy; screenshotBusy = true;
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
            screenshotBusy = wasBusy;
            if (WindowState == WindowState.Minimized && !closing) WindowState = previous == WindowState.Minimized ? WindowState.Normal : previous;
            RefreshStatus();
        }
    }

    /// <summary>Cuts a Desktop-mode area (desktop pixels) out of the monitor pictures.</summary>
    internal static RasterSample CutFromMonitorPictures(IReadOnlyList<DesktopMonitor> monitors, IReadOnlyList<WindowSnapshot> pictures, CaptureRegion area)
    {
        int index = monitors.ToList().FindIndex(m => m.Contains(area.Origin.X, area.Origin.Y) && m.Contains(area.Origin.X + area.Width - 1, area.Origin.Y + area.Height - 1));
        if (index < 0) throw new InvalidOperationException("That area is not on one monitor. Pick it again.");
        var monitor = monitors[index]; var picture = pictures[index];
        int left = area.Origin.X - monitor.X, top = area.Origin.Y - monitor.Y;
        byte[] pixels = new byte[area.Width * area.Height * 4];
        for (int y = 0; y < area.Height; y++)
            Array.Copy(picture.Pixels, ((top + y) * picture.Width + left) * 4, pixels, y * area.Width * 4, area.Width * 4);
        return new(area.Width, area.Height, pixels);
    }
}
