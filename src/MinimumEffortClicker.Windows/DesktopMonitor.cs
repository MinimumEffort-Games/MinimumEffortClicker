using System.Runtime.InteropServices;

namespace MinimumEffortClicker.Windows;

/// <summary>
/// One monitor in physical screen pixels. Numbered for people: the main monitor is 1, the rest follow left to right,
/// then top to bottom, and the label says where each sits relative to the main one.
/// </summary>
public sealed record DesktopMonitor(int Number, int X, int Y, int Width, int Height, bool Primary, string Position)
{
    public override string ToString() => $"Monitor {Number}{(Primary ? " (main)" : $" ({Position})")} · {Width} × {Height}";

    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    public static IReadOnlyList<DesktopMonitor> List()
    {
        var found = new List<(int X, int Y, int Width, int Height, bool Primary)>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                found.Add((info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, (info.Flags & 1) != 0));
            return true;
        }, 0);
        if (found.Count == 0) return [];
        var main = found.FirstOrDefault(m => m.Primary);
        if (!main.Primary) main = found[0];
        var ordered = found.OrderByDescending(m => m == main).ThenBy(m => m.X).ThenBy(m => m.Y).ToArray();
        return ordered.Select((m, i) => new DesktopMonitor(i + 1, m.X, m.Y, m.Width, m.Height, m == main, Describe(m, main))).ToArray();
    }

    private static string Describe((int X, int Y, int Width, int Height, bool Primary) monitor, (int X, int Y, int Width, int Height, bool Primary) main)
    {
        if (monitor.X >= main.X + main.Width) return "right";
        if (monitor.X + monitor.Width <= main.X) return "left";
        if (monitor.Y + monitor.Height <= main.Y) return "above";
        if (monitor.Y >= main.Y + main.Height) return "below";
        return "overlapping";
    }

    private delegate bool MonitorCallback(nint monitor, nint dc, nint rect, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
