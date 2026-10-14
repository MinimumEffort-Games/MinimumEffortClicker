namespace MinimumEffortClicker.Core;

/// <summary>
/// How a wait looks. A wait has no time limit: it keeps looking until it matches or the user stops the run
/// (Roy, 2026-10-14: a level can take 5–10 minutes before the button to wait for appears).
/// </summary>
public sealed record ConditionPolicy(TimeSpan SampleInterval, int StableSamples = 1)
{
    public void Validate()
    {
        if (SampleInterval < TimeSpan.FromMilliseconds(100) || SampleInterval > TimeSpan.FromSeconds(5) || StableSamples is < 1 or > 10)
            throw new ArgumentException("Choose a 100 ms–5 s scan interval and 1–10 matching samples in a row.");
    }
}

/// <summary>A small owned snapshot; capture adapters never expose reused native buffers.</summary>
public sealed class RasterSample
{
    public const int MaximumSide = 256;
    private readonly byte[] pixels;
    public RasterSample(int width, int height, ReadOnlySpan<byte> bgra)
    {
        if (width is < 1 or > MaximumSide || height is < 1 or > MaximumSide || bgra.Length != width * height * 4)
            throw new ArgumentException("Use a valid BGRA region no larger than 256 × 256 pixels.");
        Width = width; Height = height; pixels = bgra.ToArray();
    }
    public int Width { get; }
    public int Height { get; }
    public ReadOnlySpan<byte> Pixels => pixels;
}

public enum VisualMatch { NotMet, Met, Ambiguous }
public static class RegionComparison
{
    public static bool MatchesColorCondition(RasterSample region, ConditionKind kind, int rgb, byte tolerance)
        => kind switch
        {
            ConditionKind.RegionColor or ConditionKind.PixelColor => ContainsColor(region, rgb, tolerance),
            ConditionKind.ColorDisappears => !ContainsColor(region, rgb, tolerance),
            _ => throw new ArgumentException("Choose a colour condition.")
        };

    public static bool ContainsColor(RasterSample region, int rgb, byte tolerance)
    {
        if (rgb is < 0 or > 0xFFFFFF) throw new ArgumentException("Use an RGB colour.");
        for (int i = 0; i < region.Pixels.Length; i += 4)
            if (Math.Abs(region.Pixels[i] - (rgb & 255)) <= tolerance
                && Math.Abs(region.Pixels[i + 1] - ((rgb >> 8) & 255)) <= tolerance
                && Math.Abs(region.Pixels[i + 2] - ((rgb >> 16) & 255)) <= tolerance) return true;
        return false;
    }
    public static bool Changed(RasterSample before, RasterSample current, byte tolerance, double changedFraction)
    {
        if (before.Width != current.Width || before.Height != current.Height) throw new ArgumentException("Region geometry changed; recalibrate before comparing.");
        if (!double.IsFinite(changedFraction) || changedFraction is <= 0 or > 1) throw new ArgumentException("Choose a positive changed-pixel fraction up to one.");
        int changed = 0;
        for (int i = 0; i < before.Pixels.Length; i += 4)
            if (!Close(before.Pixels.Slice(i, 3), current.Pixels.Slice(i, 3), tolerance)) changed++;
        return changed >= Math.Ceiling(before.Width * before.Height * changedFraction);
    }
    public static VisualMatch Find(RasterSample region, RasterSample template, byte tolerance, CancellationToken stop = default)
    {
        if (template.Width > region.Width || template.Height > region.Height) throw new ArgumentException("Template must fit the observed region.");
        // Exact/tolerance matching is deliberately bounded; ambiguity never chooses a best guess.
        long comparisons = (long)(region.Width - template.Width + 1) * (region.Height - template.Height + 1) * template.Width * template.Height;
        if (comparisons > 8_000_000) throw new ArgumentException("Narrow the search region or template before matching.");
        int matches = 0;
        for (int y = 0; y <= region.Height - template.Height; y++)
        {
            stop.ThrowIfCancellationRequested();
            for (int x = 0; x <= region.Width - template.Width; x++)
            {
                bool found = true;
                for (int row = 0; found && row < template.Height; row++)
                {
                    stop.ThrowIfCancellationRequested();
                    for (int column = 0; column < template.Width; column++)
                    {
                        int source = ((y + row) * region.Width + x + column) * 4;
                        int expected = (row * template.Width + column) * 4;
                        if (!Close(region.Pixels.Slice(source, 3), template.Pixels.Slice(expected, 3), tolerance)) { found = false; break; }
                    }
                }
                if (found && ++matches > 1) return VisualMatch.Ambiguous;
            }
        }
        return matches == 1 ? VisualMatch.Met : VisualMatch.NotMet;
    }
    private static bool Close(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, byte tolerance)
        => Math.Abs(left[0] - right[0]) <= tolerance && Math.Abs(left[1] - right[1]) <= tolerance && Math.Abs(left[2] - right[2]) <= tolerance;
}
