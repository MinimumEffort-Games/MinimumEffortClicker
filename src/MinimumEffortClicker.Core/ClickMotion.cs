namespace MinimumEffortClicker.Core;

/// <summary>Optional ordinary-click movement and bounded position variation.</summary>
public sealed record ClickMotion(TimeSpan MoveDuration = default, int OffsetPixels = 0, bool UsePreviousPause = false)
{
    public void Validate()
    {
        if (MoveDuration != TimeSpan.Zero && (MoveDuration < TimeSpan.FromMilliseconds(32) || MoveDuration > TimeSpan.FromSeconds(2))
            || OffsetPixels is < 0 or > 25 || UsePreviousPause && MoveDuration != TimeSpan.Zero)
            throw new ArgumentException("Use 32–2,000 ms cursor movement and 0–25 pixels of position variation.");
    }
    public ClientPoint Offset(ClientPoint point, Func<double> random, int originX = 0, int originY = 0)
    {
        Validate();
        if (OffsetPixels == 0) return point;
        int Sample()
        {
            double value = random();
            if (!double.IsFinite(value) || value < 0 || value >= 1) throw new InvalidOperationException("Invalid position sample.");
            return (int)(value * (OffsetPixels * 2 + 1)) - OffsetPixels;
        }
        return point with { X = Math.Clamp(point.X + Sample(), originX, originX + point.Width - 1),
            Y = Math.Clamp(point.Y + Sample(), originY, originY + point.Height - 1) };
    }
    public static ClientPoint Interpolate(ClientPoint start, ClientPoint end, double fraction) => end with
    { X = (int)Math.Round(start.X + (end.X - start.X) * fraction), Y = (int)Math.Round(start.Y + (end.Y - start.Y) * fraction) };
}
