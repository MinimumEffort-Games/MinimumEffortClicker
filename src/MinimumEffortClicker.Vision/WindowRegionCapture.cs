using System.Runtime.InteropServices;
using MinimumEffortClicker.Core;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace MinimumEffortClicker.Vision;

public sealed record WindowSnapshot(int Width, int Height, byte[] Pixels, long FrameId);
public sealed record CapturedRegion(RasterSample Image, long FrameId);

/// <summary>Explicit-lifetime WGC capture. No input, saved files, idle watcher or UI dispatcher.</summary>
public sealed class WindowRegionCapture : IDisposable
{
    private readonly nint window;
    private readonly CaptureRegion region;
    private readonly Func<bool> identityAvailable;
    private readonly bool requireForeground;
    private readonly AutoResetEvent arrived = new(false);
    private CaptureDevice? device;
    private GraphicsCaptureItem? item;
    private Direct3D11CaptureFramePool? pool;
    private GraphicsCaptureSession? session;
    private volatile bool closed;
    private bool disposed;
    private long lastFrame = -1;
    public WindowRegionCapture(nint window, CaptureRegion region, Func<bool> identityAvailable, bool authoringPreview = false, bool requireForeground = true)
    {
        if (!authoringPreview) region.Validate();
        else if (!region.Origin.IsValid || region.Origin.X != 0 || region.Origin.Y != 0 || region.Width != region.Origin.Width || region.Height != region.Origin.Height
            || region.Width is < 1 or > 4096 || region.Height is < 1 or > 2160)
            throw new ArgumentException("Preview must be the current client area, at most 4096 × 2160.");
        ArgumentNullException.ThrowIfNull(identityAvailable);
        this.window = window; this.region = region; this.identityAvailable = identityAvailable;
        this.requireForeground = requireForeground;
        try
        {
            ValidateWindow();
            if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows Graphics Capture is unavailable.");
            item = CaptureNative.Item(window);
            if (item.Size.Width is < 1 or > 4096 || item.Size.Height is < 1 or > 2160) throw new NotSupportedException("Capture window exceeds the supported 4096 × 2160 bounds.");
            item.Closed += ItemClosed;
            device = new(region.Width, region.Height);
            StartSnapshot();
        }
        catch { Dispose(); throw; }
    }
    private void SignalFrame() { try { arrived.Set(); } catch (ObjectDisposedException) { } }
    private void ItemClosed(GraphicsCaptureItem sender, object args) { closed = true; SignalFrame(); }
    private void FrameArrived(Direct3D11CaptureFramePool sender, object args) => SignalFrame();
    private void StartSnapshot()
    {
        // WGC may omit unchanged frames. A new explicit snapshot obtains current pixels rather
        // than relabelling a cached frame as fresh. Reuse the device and small staging allocation.
        if (pool is not null) pool.FrameArrived -= FrameArrived;
        session?.Dispose(); pool?.Dispose(); arrived.Reset();
        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device!.Device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item!.Size);
        pool.FrameArrived += FrameArrived;
        session = pool.CreateCaptureSession(item); session.IsCursorCaptureEnabled = false;
        session.StartCapture();
    }
    private void ValidateWindow()
    {
        if (closed || !identityAvailable() || requireForeground && CaptureNative.GetForegroundWindow() != window
            || !CaptureNative.GetClientRect(window, out var client)
            || !region.Origin.Matches(client.Right, client.Bottom, (int)CaptureNative.GetDpiForWindow(window)))
            throw new InvalidOperationException("Capture target changed, lost foreground, resized or changed DPI.");
    }
    public CapturedRegion Capture(CancellationToken stop)
    {
        var raw = CapturePreview(stop);
        return new(new(raw.Width, raw.Height, raw.Pixels), raw.FrameId);
    }
    public WindowSnapshot CapturePreview(CancellationToken stop)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        stop.ThrowIfCancellationRequested(); ValidateWindow();
        if (lastFrame >= 0) StartSnapshot();
        var deadline = Environment.TickCount64 + 2000;
        while (true)
        {
            stop.ThrowIfCancellationRequested(); ValidateWindow();
            using var frame = pool!.TryGetNextFrame();
            if (frame is not null && frame.SystemRelativeTime.Ticks > lastFrame)
            {
                if (frame.ContentSize.Width != item!.Size.Width || frame.ContentSize.Height != item.Size.Height)
                    throw new InvalidOperationException("Capture size changed; recalibrate before continuing.");
                Marshal.ThrowExceptionForHR(CaptureNative.DwmGetWindowAttribute(window, 9, out var bounds, Marshal.SizeOf<CaptureNative.Rect>()));
                var client = new CaptureNative.Point();
                if (!CaptureNative.ClientToScreen(window, ref client)) throw new InvalidOperationException("Client mapping unavailable.");
                int x = client.X - bounds.Left + region.Origin.X, y = client.Y - bounds.Top + region.Origin.Y;
                if (x < 0 || y < 0 || x + region.Width > frame.ContentSize.Width || y + region.Height > frame.ContentSize.Height)
                    throw new InvalidOperationException("Client region does not fit the capture frame.");
                using var surface = frame.Surface; // Release the surface wrapper now instead of leaving it to the finalizer.
                var pixels = device!.Read(surface, x, y);
                stop.ThrowIfCancellationRequested(); ValidateWindow();
                lastFrame = frame.SystemRelativeTime.Ticks;
                return new(region.Width, region.Height, pixels.ToArray(), lastFrame);
            }
            int remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
            if (remaining == 0 || WaitHandle.WaitAny([stop.WaitHandle, arrived], remaining) == WaitHandle.WaitTimeout)
                throw new NotSupportedException("No fresh capture frame arrived. This window may not support capture.");
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; closed = true;
        if (pool is not null) pool.FrameArrived -= FrameArrived;
        if (item is not null) item.Closed -= ItemClosed;
        session?.Dispose(); pool?.Dispose(); device?.Dispose(); arrived.Dispose();
    }
}
