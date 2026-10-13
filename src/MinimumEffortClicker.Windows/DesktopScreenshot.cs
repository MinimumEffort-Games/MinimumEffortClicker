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
        cancellation.ThrowIfCancellationRequested();
        var bounds = Bounds;
        nint screen = 0, memory = 0, bitmap = 0, previous = 0;
        try
        {
            screen = GetDC(0);
            if (screen == 0) throw new Win32Exception("The desktop is not available for capture.");
            memory = CreateCompatibleDC(screen);
            if (memory == 0) throw new Win32Exception("Could not create the screenshot buffer.");
            var header = new BitmapHeader { Size = (uint)Marshal.SizeOf<BitmapHeader>(), Width = bounds.Width,
                Height = -bounds.Height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(screen, ref header, 0, out var pixels, 0, 0);
            if (bitmap == 0 || pixels == 0) throw new Win32Exception("Could not allocate the screenshot buffer.");
            previous = SelectObject(memory, bitmap);
            if (previous == 0 || previous == -1) throw new Win32Exception("Could not select the screenshot buffer.");
            cancellation.ThrowIfCancellationRequested();
            if (!BitBlt(memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, 0x40CC0020))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not capture this screen.");
            GdiFlush(); // Synchronize the DIB before reading its bytes.
            cancellation.ThrowIfCancellationRequested();
            if (Bounds != bounds) throw new InvalidOperationException("The display changed. Try taking the screenshot again.");
            byte[] data = new byte[checked(bounds.Width * bounds.Height * 4)];
            Marshal.Copy(pixels, data, 0, data.Length);
            return new(bounds, data);
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
