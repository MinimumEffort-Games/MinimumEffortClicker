using System.ComponentModel;
using System.Runtime.InteropServices;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.Windows;

public sealed record DesktopScreenshotFrame(ScreenshotBounds Bounds, byte[] Bgr32Pixels);

/// <summary>One explicit GDI desktop copy. Owns and releases every DC/bitmap in the call.</summary>
public static class DesktopScreenshot
{
    public static ScreenshotBounds Bounds
    {
        get
        {
            var bounds = new ScreenshotBounds(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
            bounds.Validate();
            return bounds;
        }
    }

    public static DesktopScreenshotFrame Capture(CancellationToken cancellation)
    {
        var bounds = Bounds;
        var data = Copy(bounds.X, bounds.Y, bounds.Width, bounds.Height, opaque: false, cancellation);
        if (Bounds != bounds) throw new InvalidOperationException("The display changed. Try taking the screenshot again.");
        return new(bounds, data);
    }

    /// <summary>
    /// Copies one rectangle of the screen in physical pixels, on any monitor, as opaque BGRA. Used for Desktop-mode
    /// colour and image waits and their area picker.
    /// </summary>
    public static byte[] CaptureArea(int x, int y, int width, int height, CancellationToken cancellation)
    {
        var bounds = Bounds;
        if (width < 1 || height < 1 || x < bounds.X || y < bounds.Y || (long)x + width > (long)bounds.X + bounds.Width || (long)y + height > (long)bounds.Y + bounds.Height)
            throw new InvalidOperationException("That area is no longer on a monitor. Pick it again.");
        return Copy(x, y, width, height, opaque: true, cancellation);
    }

    private static byte[] Copy(int x, int y, int width, int height, bool opaque, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        nint screen = 0, memory = 0, bitmap = 0, previous = 0;
        try
        {
            screen = GetDC(0);
            if (screen == 0) throw new Win32Exception("The desktop is not available for capture.");
            memory = CreateCompatibleDC(screen);
            if (memory == 0) throw new Win32Exception("Could not create the screenshot buffer.");
            var header = new BitmapHeader { Size = (uint)Marshal.SizeOf<BitmapHeader>(), Width = width,
                Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(screen, ref header, 0, out var pixels, 0, 0);
            if (bitmap == 0 || pixels == 0) throw new Win32Exception("Could not allocate the screenshot buffer.");
            previous = SelectObject(memory, bitmap);
            if (previous == 0 || previous == -1) throw new Win32Exception("Could not select the screenshot buffer.");
            cancellation.ThrowIfCancellationRequested();
            if (!BitBlt(memory, 0, 0, width, height, screen, x, y, 0x40CC0020))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not capture this screen.");
            GdiFlush(); // Synchronize the DIB before reading its bytes.
            cancellation.ThrowIfCancellationRequested();
            byte[] data = new byte[checked(width * height * 4)];
            Marshal.Copy(pixels, data, 0, data.Length);
            // GDI leaves the alpha byte undefined; pickers draw BGRA, so make every pixel opaque.
            if (opaque) for (int i = 3; i < data.Length; i += 4) data[i] = 255;
            return data;
        }
        finally
        {
            if (previous != 0 && previous != -1) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            if (screen != 0) ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPels, YPels; public uint Used, Important;
    }
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapHeader info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
