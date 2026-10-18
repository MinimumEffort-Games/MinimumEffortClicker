namespace MinimumEffortClicker.Linux;

/// <summary>
/// How many X11 pixels one unit of the desktop helper (KWin, GNOME Shell) is. With display scaling and Xwayland drawing
/// at the screen's real resolution the X11 screen is wider than the helper's logical desktop: 3840 against 2195 on a 4K
/// screen at 175 %. Without scaling, or when the compositor scales X11 apps itself, the two are equal.
/// </summary>
public static class XwaylandScale
{
    /// <summary>The ratio, with values within 0.01 of a quarter step snapped to it (the logical size is rounded, so a 1.75
    /// screen measures 1.7494) and anything unlikely kept between 0.5 and 4.</summary>
    public static double Ratio(int x11Width, double logicalWidth)
    {
        if (x11Width <= 0 || logicalWidth <= 0) return 1;
        double ratio = Math.Clamp(x11Width / logicalWidth, 0.5, 4), quarter = Math.Round(ratio * 4) / 4;
        return Math.Abs(ratio - quarter) < 0.01 ? quarter : ratio;
    }
}
