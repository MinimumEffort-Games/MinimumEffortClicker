namespace MinimumEffortClicker.Core;

/// <summary>
/// Where the status overlay sits on its Click target (a window, or the desktop). An anchor is a pair of fractions of the
/// room the overlay has inside the target: 0 puts it against the left or top edge, 1 against the right or bottom edge,
/// 0.5 centres it, always <see cref="Inset"/> pixels inside the edge. The Settings picture gives points on the edge; a
/// drag of the unlocked overlay gives any pair, also outside 0 to 1 when it is dropped beside the target, so it keeps its
/// place relative to the target while that moves or changes size.
/// </summary>
public static class OverlayAnchor
{
    /// <summary>Gap between the overlay and the target's edge, in pixels.</summary>
    public const double Inset = 12;
    /// <summary>How far outside the target a dragged anchor may go, in multiples of the room; further is a corrupt value.</summary>
    public const double Reach = 10;
    /// <summary>A click this close to a corner or the middle of an edge (as a fraction of the edge) lands exactly on it.</summary>
    public const double Snap = 0.06;

    public static bool Valid(double x, double y) => double.IsFinite(x) && double.IsFinite(y)
        && x is >= -Reach and <= 1 + Reach && y is >= -Reach and <= 1 + Reach;

    /// <summary>The overlay's top-left corner for a target and an overlay size, all in the same units; a smaller inset
    /// draws the same place on the Settings picture.</summary>
    public static (double X, double Y) Place(double left, double top, double width, double height, double overlayWidth, double overlayHeight, double anchorX, double anchorY, double inset = Inset)
        => (left + inset + anchorX * Room(width, overlayWidth, inset), top + inset + anchorY * Room(height, overlayHeight, inset));

    /// <summary>The anchor that puts the overlay's top-left corner at x, y: the reverse of <see cref="Place"/>. An axis with no
    /// room (the overlay fills the target that way) keeps its current anchor.</summary>
    public static (double X, double Y) From(double left, double top, double width, double height, double overlayWidth, double overlayHeight, double x, double y, double currentX, double currentY)
        => (Fraction(x - left - Inset, Room(width, overlayWidth), currentX), Fraction(y - top - Inset, Room(height, overlayHeight), currentY));

    /// <summary>The anchor on the target's edge nearest to a point given as fractions of the target (0, 0 top-left; 1, 1
    /// bottom-right), with the corners and the middles of the edges snapping.</summary>
    public static (double X, double Y) OnEdge(double x, double y)
    {
        x = Math.Clamp(double.IsFinite(x) ? x : 1, 0, 1); y = Math.Clamp(double.IsFinite(y) ? y : 0, 0, 1);
        double nearest = Math.Min(Math.Min(x, 1 - x), Math.Min(y, 1 - y));
        return nearest == Math.Min(x, 1 - x) ? (x < 0.5 ? 0 : 1, Snapped(y)) : (Snapped(x), y < 0.5 ? 0 : 1);
    }

    /// <summary>One arrow-key step from an anchor, in the arrow's direction, through the corners and the edge middles: from
    /// the top middle, Down crosses to the bottom middle (the centre is not a point on the edge). An anchor between the
    /// points starts from the nearest one.</summary>
    public static (double X, double Y) Step(double x, double y, int dx, int dy)
    {
        double fromX = Nearest(x), fromY = Nearest(y);
        if (fromX == 0.5 && fromY == 0.5) fromY = 0; // A drag into the middle: step from the top middle.
        double toX = Math.Clamp(fromX + 0.5 * Math.Sign(dx), 0, 1), toY = Math.Clamp(fromY + 0.5 * Math.Sign(dy), 0, 1);
        if (toX == 0.5 && toY == 0.5) (toX, toY) = (Math.Clamp(fromX + Math.Sign(dx), 0, 1), Math.Clamp(fromY + Math.Sign(dy), 0, 1));
        return (toX, toY);
    }

    private static double Nearest(double value) => double.IsFinite(value) ? Math.Round(Math.Clamp(value, 0, 1) * 2) / 2 : 1;
    private static double Room(double length, double size, double inset = Inset) => Math.Max(0, length - size - 2 * inset);
    private static double Fraction(double offset, double room, double current)
        => room < 1 || !double.IsFinite(offset) ? current : Math.Clamp(offset / room, -Reach, 1 + Reach);
    private static double Snapped(double value)
    {
        foreach (double point in (double[])[0, 0.5, 1]) if (Math.Abs(value - point) <= Snap) return point;
        return value;
    }
}
