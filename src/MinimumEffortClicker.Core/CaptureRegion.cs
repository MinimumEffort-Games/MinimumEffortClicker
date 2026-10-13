namespace MinimumEffortClicker.Core;

public sealed record CaptureRegion(ClientPoint Origin, int Width, int Height)
{
    public void Validate()
    {
        if (Origin is null || !Origin.IsValid || Origin.ScaleWithWindow || Origin.Space != CoordinateSpace.WindowClient || Width is < 1 or > RasterSample.MaximumSide || Height is < 1 or > RasterSample.MaximumSide
            || Origin.X + Width > Origin.Width || Origin.Y + Height > Origin.Height)
            throw new ArgumentException("Choose a calibrated client region no larger than 256 × 256 pixels.");
    }
}
