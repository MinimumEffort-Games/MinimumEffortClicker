using Avalonia;

namespace MinimumEffortClicker.App;

/// <summary>
/// The app's own windows (status overlay, click rings, area outlines) are X11 windows placed in X11 pixels, while the
/// desktop helper (KWin, GNOME Shell) gives positions in the compositor's logical units. With display scaling and
/// Xwayland drawing at the screen's real resolution (KDE's "apply scaling themselves") the two differ: on a 4K screen at
/// 175 % the helper's desktop is 2195 wide and the X11 screen 3840. A helper position is multiplied by the ratio before
/// it places one of the app's windows; without scaling the ratio is 1 and nothing changes.
/// </summary>
internal static class DesktopPixels
{
    /// <summary>X11 pixels per helper unit.</summary>
    public static double Factor { get; private set; } = 1;

    /// <summary>Measures the ratio from the helper's desktop (all monitors, logical units) and the X11 screen.</summary>
    public static void Measure(double logicalDesktopWidth)
    {
        if (X11Windows.ScreenWidth() is { } x11Width) Factor = MinimumEffortClicker.Linux.XwaylandScale.Ratio(x11Width, logicalDesktopWidth);
    }

    /// <summary>A helper position as an X11 pixel position for one of the app's windows.</summary>
    public static PixelPoint ToWindow(double x, double y) => new((int)Math.Round(x * Factor), (int)Math.Round(y * Factor));
    public static int ToWindow(double length) => (int)Math.Round(length * Factor);
}
