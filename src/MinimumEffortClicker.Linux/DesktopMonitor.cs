namespace MinimumEffortClicker.Linux;

/// <summary>
/// One monitor in GNOME's logical desktop pixels. Numbered for people: the main monitor is 1, the rest follow left to right,
/// then top to bottom, and the label says where each sits relative to the main one.
/// </summary>
public sealed record DesktopMonitor(int Number, int X, int Y, int Width, int Height, bool Primary, string Position)
{
    public override string ToString() => $"Monitor {Number}{(Primary ? " (main)" : $" ({Position})")} · {Width} × {Height}";

    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    /// <summary>The helper lists GNOME's primary monitor first.</summary>
    public static IReadOnlyList<DesktopMonitor> List(IDesktopHelper helper) => Order(helper.GetMonitors());

    public static IReadOnlyList<DesktopMonitor> Order(IReadOnlyList<HelperMonitor> found)
    {
        if (found.Count == 0) return [];
        var main = found[0];
        var ordered = found.OrderByDescending(m => ReferenceEquals(m, main)).ThenBy(m => m.X).ThenBy(m => m.Y).ToArray();
        return ordered.Select((m, i) => new DesktopMonitor(i + 1, m.X, m.Y, m.Width, m.Height, ReferenceEquals(m, main), Describe(m, main))).ToArray();
    }

    private static string Describe(HelperMonitor monitor, HelperMonitor main)
    {
        if (monitor.X >= main.X + main.Width) return "right";
        if (monitor.X + monitor.Width <= main.X) return "left";
        if (monitor.Y + monitor.Height <= main.Y) return "above";
        if (monitor.Y >= main.Y + main.Height) return "below";
        return "overlapping";
    }
}
