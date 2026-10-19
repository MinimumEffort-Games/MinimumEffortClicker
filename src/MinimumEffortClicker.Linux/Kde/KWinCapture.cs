using System.IO.Pipes;
using System.Runtime.InteropServices;
using Tmds.DBus.Protocol;

namespace MinimumEffortClicker.Linux.Kde;

/// <summary>
/// Screen and window pixels through KWin's org.kde.KWin.ScreenShot2. KWin answers only programs whose .desktop file
/// lists the interface under X-KDE-DBUS-Restricted-Interfaces. KWin writes raw pixels into a pipe and describes them
/// in its reply. Images come back at the monitor's real resolution (native-resolution), so nothing is blended.
/// </summary>
internal static class KWinCapture
{
    private const string Interface = "org.kde.KWin.ScreenShot2";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const int MaxPixels = 32 * 1024 * 1024;

    public static HelperImage Area(DBusConnection connection, int x, int y, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > MaxPixels) throw new HelperException("Capture area is empty or too large.");
        return Capture(connection, "CaptureArea", "iiuua{sv}h", (ref MessageWriter w, SafeHandle pipe) =>
        {
            w.WriteInt32(x); w.WriteInt32(y); w.WriteUInt32((uint)width); w.WriteUInt32((uint)height);
            WriteOptions(ref w, window: false);
            w.WriteHandle(pipe);
        });
    }

    /// <summary>The window's whole client area, its own pixels (windows above it do not show).</summary>
    public static HelperImage Window(DBusConnection connection, string windowId) =>
        Capture(connection, "CaptureWindow", "sa{sv}h", (ref MessageWriter w, SafeHandle pipe) =>
        {
            w.WriteString(windowId);
            WriteOptions(ref w, window: true);
            w.WriteHandle(pipe);
        });

    private delegate void WriteCall(ref MessageWriter writer, SafeHandle pipe);

    private static void WriteOptions(ref MessageWriter w, bool window)
    {
        var options = w.WriteDictionaryStart();
        w.WriteDictionaryEntryStart(); w.WriteString("native-resolution"); w.WriteVariantBool(true);
        w.WriteDictionaryEntryStart(); w.WriteString("include-cursor"); w.WriteVariantBool(false);
        if (window)
        {
            w.WriteDictionaryEntryStart(); w.WriteString("include-decoration"); w.WriteVariantBool(false);
            w.WriteDictionaryEntryStart(); w.WriteString("include-shadow"); w.WriteVariantBool(false);
        }
        w.WriteDictionaryEnd(options);
    }

    private static HelperImage Capture(DBusConnection connection, string member, string signature, WriteCall write)
    {
        var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        // Read while KWin writes, so a large picture never fills the pipe and stalls it. The reader owns the pipe and
        // closes it when the read ends: on Linux, closing a pipe while a read is blocked waits until the other end closes,
        // and KWin can hold its end for minutes without writing (a Steam Deck start hung 6 min 42 s on that, 2026-10-19).
        // After a timeout the reader is simply left to finish on its own thread.
        var read = Task.Factory.StartNew(() =>
        {
            try { using var buffer = new MemoryStream(); pipe.CopyTo(buffer); return buffer.ToArray(); }
            finally { pipe.Dispose(); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Dictionary<string, VariantValue> info;
        try
        {
            var writePipe = pipe.ClientSafePipeHandle;
            info = KdeHelper.Call(connection, "org.kde.KWin", "/org/kde/KWin/ScreenShot2", Interface, member, signature,
                (ref MessageWriter w) => write(ref w, writePipe), (m, _) => m.GetBodyReader().ReadDictionaryOfStringToVariantValue(), Timeout);
        }
        finally { pipe.DisposeLocalCopyOfClientHandle(); } // KWin holds its own copy; ours must close for the read to end.
        if (!read.Wait(Timeout)) throw new HelperException("KWin did not finish the capture in time.");
        return ToImage(info, read.Result);
    }

    /// <summary>Converts KWin's QImage layout to RGBA rows; KWin sends 32-bit pixels in memory order B, G, R, A.</summary>
    internal static HelperImage ToImage(IReadOnlyDictionary<string, VariantValue> info, byte[] data)
    {
        int width = (int)info["width"].GetUInt32(), height = (int)info["height"].GetUInt32(), stride = (int)info["stride"].GetUInt32();
        uint format = info["format"].GetUInt32();
        return ToImage(width, height, stride, format, data);
    }

    internal static HelperImage ToImage(int width, int height, int stride, uint format, byte[] data)
    {
        // QImage formats: 4 RGB32, 5 ARGB32, 6 ARGB32 premultiplied (B, G, R, A in memory); 16 RGBX8888, 17 RGBA8888,
        // 18 RGBA8888 premultiplied (R, G, B, A). Screen pixels are opaque, so premultiplied equals straight.
        bool bgra = format is 4 or 5 or 6;
        if (!bgra && format is not (16 or 17 or 18)) throw new HelperException($"KWin sent a picture format the clicker cannot read ({format}).");
        if (width < 1 || height < 1 || stride < width * 4 || data.Length < (long)stride * (height - 1) + width * 4)
            throw new HelperException("KWin sent an unreadable capture.");
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int from = y * stride + x * 4, to = (y * width + x) * 4;
                pixels[to] = data[from + (bgra ? 2 : 0)];
                pixels[to + 1] = data[from + 1];
                pixels[to + 2] = data[from + (bgra ? 0 : 2)];
                pixels[to + 3] = format is 4 or 16 ? (byte)255 : data[from + 3];
            }
        return new(width, height, width * 4, 4, pixels);
    }

    /// <summary>A part of an image, in that image's own pixels.</summary>
    internal static HelperImage Crop(HelperImage image, int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, image.Width - 1); y = Math.Clamp(y, 0, image.Height - 1);
        width = Math.Clamp(width, 1, image.Width - x); height = Math.Clamp(height, 1, image.Height - y);
        var pixels = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(image.Pixels, (y + row) * image.RowStride + x * 4, pixels, row * width * 4, width * 4);
        return new(width, height, width * 4, 4, pixels);
    }
}
