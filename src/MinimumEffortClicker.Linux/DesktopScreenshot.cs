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
}
