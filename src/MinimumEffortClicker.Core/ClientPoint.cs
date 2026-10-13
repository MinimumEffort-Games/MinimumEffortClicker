namespace MinimumEffortClicker.Core;

public enum CoordinateSpace { WindowClient, Desktop }
/// <summary>Physical pixels with an explicit coordinate space and target calibration frame.</summary>
public sealed record ClientPoint(int X, int Y, int Width, int Height, int Dpi, CoordinateSpace Space = CoordinateSpace.WindowClient)
{
    // Runtime policy, applied to a copy at Start. Saved coordinates keep their original calibration.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ScaleWithWindow { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Width is > 0 and <= 65536 && Height is > 0 and <= 65536
        && Dpi is >= 48 and <= 960 && Enum.IsDefined(Space) && (!ScaleWithWindow || Space == CoordinateSpace.WindowClient)
        && (Space == CoordinateSpace.WindowClient ? X >= 0 && Y >= 0 && X < Width && Y < Height
            : X is >= -1000000 and <= 1000000 && Y is >= -1000000 and <= 1000000);
    public bool Matches(int width, int height, int dpi) => IsValid && width is > 0 and <= 65536
        && height is > 0 and <= 65536 && dpi is >= 48 and <= 960
        && (ScaleWithWindow || Width == width && Height == height && Dpi == dpi);
    public ClientPoint InFrame(int width, int height, int dpi)
    {
        if (!Matches(width, height, dpi)) throw new ArgumentException("Target size or DPI changed. Recalibrate recorded positions before playback.");
        if (!ScaleWithWindow) return this;
        // Always scale from the saved frame: resizing back and forth cannot accumulate rounding drift.
        return new(Math.Min(width - 1, (int)((long)X * width / Width)),
            Math.Min(height - 1, (int)((long)Y * height / Height)), width, height, dpi);
    }
    public (int X, int Y) Resolve(int originX, int originY, int width, int height, int dpi)
    {
        var point = InFrame(width, height, dpi);
        return Space == CoordinateSpace.Desktop ? (X, Y) : (checked(originX + point.X), checked(originY + point.Y));
    }
    public static int NormalizeAbsolute(int pixel, int origin, int length)
    {
        long offset = (long)pixel - origin;
        if (length is < 1 or > 65536 || offset < 0 || offset >= length) throw new ArgumentOutOfRangeException(nameof(pixel));
        // Aim at the pixel's centre in the 16-bit virtual-desktop coordinate space.
        return (int)Math.Min(65535, ((2 * offset + 1) * 65536) / (2L * length));
    }
}
