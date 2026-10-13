namespace MinimumEffortClicker.Core;

public enum ConditionKind { RegionChanged, PixelColor, WindowActive, ImageAppears, ImageDisappears, RegionColor, ColorDisappears }
public sealed record ImageTemplate(int Width, int Height, string Bgra)
{
    public RasterSample Decode()
    {
        if (Width is < 1 or > 64 || Height is < 1 or > 64 || Bgra is null || Bgra.Length > 21848)
            throw new ArgumentException("Image templates must be valid BGRA snapshots no larger than 64×64 pixels.");
        try { return new(Width, Height, Convert.FromBase64String(Bgra)); }
        catch (FormatException ex) { throw new ArgumentException("Invalid image template encoding.", ex); }
    }
    public static ImageTemplate FromSample(RasterSample sample)
    {
        var result = new ImageTemplate(sample.Width, sample.Height, Convert.ToBase64String(sample.Pixels));
        result.Decode(); return result;
    }
}
public sealed record ConditionDefinition(ConditionKind Kind, ConditionPolicy Policy, CaptureRegion? Region = null,
    int Color = 0, byte Tolerance = 8, double ChangedFraction = 0.1, ImageTemplate? Template = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || Policy is null || Color is < 0 or > 0xFFFFFF
            || !double.IsFinite(ChangedFraction) || ChangedFraction is <= 0 or > 1)
            throw new ArgumentException("Invalid condition type, colour or changed-pixel fraction.");
        Policy.Validate();
        if (Kind is not (ConditionKind.ImageAppears or ConditionKind.ImageDisappears) && Template is not null)
            throw new ArgumentException("Only image conditions can contain a template.");
        if (Kind == ConditionKind.WindowActive)
        { if (Region is not null) throw new ArgumentException("A window-active wait has no capture region."); }
        else
        {
            if (Region is null) throw new ArgumentException("A visual wait needs a calibrated client region.");
            Region.Validate();
            if (Kind == ConditionKind.PixelColor && (Region.Width != 1 || Region.Height != 1))
                throw new ArgumentException("A pixel-colour wait observes exactly one pixel.");
            if (Kind is ConditionKind.ImageAppears or ConditionKind.ImageDisappears)
            {
                var image = Template?.Decode() ?? throw new ArgumentException("Capture a template before saving an image condition.");
                long comparisons = (long)(Region.Width - image.Width + 1) * (Region.Height - image.Height + 1) * image.Width * image.Height;
                if (image.Width > Region.Width || image.Height > Region.Height || comparisons > 8_000_000)
                    throw new ArgumentException("Template must fit the search region within the bounded comparison budget. Narrow the region if necessary.");
            }
        }
    }
}
public sealed record ScheduledCondition(TimeSpan Offset, int StepIndex, string Name, ConditionDefinition Definition);
