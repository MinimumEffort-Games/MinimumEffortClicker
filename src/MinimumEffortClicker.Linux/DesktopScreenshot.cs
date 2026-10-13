using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Linux;

public sealed record DesktopScreenshotFrame(ScreenshotBounds Bounds, byte[] Bgr32Pixels);

/// <summary>One explicit desktop copy through the GNOME helper, in GNOME's logical desktop pixels.</summary>
public static class DesktopScreenshot
{
    public static ScreenshotBounds Bounds(ShellHelper helper)
    {
        var monitors = helper.GetMonitors();
        if (monitors.Count == 0) throw new InvalidOperationException("No monitor is available for capture.");
        int left = monitors.Min(m => m.X), top = monitors.Min(m => m.Y);
        var bounds = new ScreenshotBounds(left, top, monitors.Max(m => m.X + m.Width) - left, monitors.Max(m => m.Y + m.Height) - top);
        bounds.Validate();
        return bounds;
    }

    public static DesktopScreenshotFrame Capture(ShellHelper helper, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var bounds = Bounds(helper);
        var image = helper.CaptureRaw(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        cancellation.ThrowIfCancellationRequested();
        if (Bounds(helper) != bounds) throw new InvalidOperationException("The display changed. Try taking the screenshot again.");
        return new(bounds, RegionCapture.ToBgra(image, bounds.Width, bounds.Height));
    }

    /// <summary>
    /// Copies one rectangle of the desktop in logical pixels, on any monitor, as BGRA. Used for Desktop-mode colour and
    /// image waits and their area picker.
    /// </summary>
    public static byte[] CaptureArea(ShellHelper helper, int x, int y, int width, int height, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var bounds = Bounds(helper);
        if (width < 1 || height < 1 || x < bounds.X || y < bounds.Y || (long)x + width > (long)bounds.X + bounds.Width || (long)y + height > (long)bounds.Y + bounds.Height)
            throw new InvalidOperationException("That area is no longer on a monitor. Pick it again.");
        var image = helper.CaptureRaw(x, y, width, height);
        cancellation.ThrowIfCancellationRequested();
        return RegionCapture.ToBgra(image, width, height);
    }
}
