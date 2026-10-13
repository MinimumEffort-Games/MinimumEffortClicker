namespace MinimumEffortClicker.Core;

/// <summary>Physical desktop pixels. Origins may be negative; output sizes are bounded.</summary>
public readonly record struct ScreenshotBounds(int X, int Y, int Width, int Height)
{
    public const long MaximumPixels = 32L * 1024 * 1024;
    public void Validate()
    {
        if (Width is < 1 or > 32768 || Height is < 1 or > 32768 || (long)Width * Height > MaximumPixels
            || (long)X + Width > int.MaxValue || (long)Y + Height > int.MaxValue)
            throw new ArgumentException("The screen is unavailable or exceeds the 32-megapixel capture limit.");
    }

    /// <summary>Normalizes either drag direction, clips to the snapshot, returns local pixels.</summary>
    public ScreenshotBounds? Selection(int startX, int startY, int endX, int endY)
    {
        Validate();
        int x1 = (int)Math.Clamp((long)startX - X, 0, Width), x2 = (int)Math.Clamp((long)endX - X, 0, Width);
        int y1 = (int)Math.Clamp((long)startY - Y, 0, Height), y2 = (int)Math.Clamp((long)endY - Y, 0, Height);
        int width = Math.Abs(x2 - x1), height = Math.Abs(y2 - y1);
        return width == 0 || height == 0 ? null : new(Math.Min(x1, x2), Math.Min(y1, y2), width, height);
    }
}
