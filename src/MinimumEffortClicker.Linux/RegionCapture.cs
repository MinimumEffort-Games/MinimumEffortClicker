using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Linux;

public sealed record WindowSnapshot(int Width, int Height, byte[] Pixels, long FrameId);
public sealed record CapturedRegion(RasterSample Image, long FrameId);

/// <summary>
/// Explicit capture of one client-area region through the GNOME helper, from the window's own
/// content. Like the Windows build, the window must stay in front for playback to continue.
/// </summary>
public sealed class RegionCapture
{
    private readonly IDesktopHelper helper;
    private readonly ulong window;
    private readonly CaptureRegion region;
    private readonly Func<bool> identityAvailable;
    private readonly bool requireForeground;
    private long frame;

    public RegionCapture(IDesktopHelper helper, ulong window, CaptureRegion region, Func<bool> identityAvailable, bool authoringPreview = false, bool requireForeground = true)
    {
        if (!authoringPreview) region.Validate();
        else if (!region.Origin.IsValid || region.Origin.X != 0 || region.Origin.Y != 0 || region.Width != region.Origin.Width || region.Height != region.Origin.Height
            || region.Width is < 1 or > 4096 || region.Height is < 1 or > 2160)
            throw new ArgumentException("Preview must be the current client area, at most 4096 × 2160.");
        ArgumentNullException.ThrowIfNull(identityAvailable);
        this.helper = helper; this.window = window; this.region = region; this.identityAvailable = identityAvailable;
        this.requireForeground = requireForeground;
        Validate();
    }

    private HelperTargetState Validate()
    {
        var state = helper.QueryTarget(window);
        int dpi = (int)Math.Round(96 * (state.Scale > 0 ? state.Scale : 1));
        if (!state.Exists || state.Minimized || !identityAvailable() || requireForeground && state.Focus != window
            || !region.Origin.Matches(state.Width, state.Height, dpi))
            throw new InvalidOperationException("Capture target changed, lost foreground, resized or changed scale.");
        return state;
    }

    public CapturedRegion Capture(CancellationToken stop)
    {
        var raw = CapturePreview(stop);
        return new(new(raw.Width, raw.Height, raw.Pixels), raw.FrameId);
    }

    public WindowSnapshot CapturePreview(CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        var state = Validate();
        HelperImage image;
        // Prefer the window's own pixels (like Windows Graphics Capture): our status overlay or another
        // window above the target can then never change what a colour wait sees.
        try { image = helper.CaptureWindowRaw(window, region.Origin.X, region.Origin.Y, region.Width, region.Height); }
        catch (HelperException ex) when (!ex.AccessDenied && !ex.NotInstalled)
        { image = helper.CaptureRaw(state.X + region.Origin.X, state.Y + region.Origin.Y, region.Width, region.Height); }
        stop.ThrowIfCancellationRequested();
        Validate();
        return new(region.Width, region.Height, ToBgra(image, region.Width, region.Height), Interlocked.Increment(ref frame));
    }

    /// <summary>Converts RGB(A) rows to tightly packed BGRA, resampling when GNOME captured at a higher scale.</summary>
    public static byte[] ToBgra(HelperImage image, int width, int height)
    {
        if (image.Width < 1 || image.Height < 1 || image.Channels is not (3 or 4) || image.RowStride < image.Width * image.Channels
            || image.Pixels.Length < (long)image.RowStride * (image.Height - 1) + image.Width * image.Channels)
            throw new InvalidOperationException("GNOME returned an unreadable capture.");
        var result = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            int sourceY = (int)((long)y * image.Height / height);
            for (int x = 0; x < width; x++)
            {
                int sourceX = (int)((long)x * image.Width / width);
                int from = sourceY * image.RowStride + sourceX * image.Channels, to = (y * width + x) * 4;
                result[to] = image.Pixels[from + 2];
                result[to + 1] = image.Pixels[from + 1];
                result[to + 2] = image.Pixels[from];
                result[to + 3] = image.Channels == 4 ? image.Pixels[from + 3] : (byte)255;
            }
        }
        return result;
    }
}
