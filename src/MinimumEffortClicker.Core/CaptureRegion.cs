namespace MinimumEffortClicker.Core;

/// <summary>
/// The area a visual wait looks at. In a window it is relative to that window's client area; in Desktop mode it is in
/// absolute screen pixels across all monitors, calibrated to the monitor layout it was picked on.
/// </summary>
public sealed record CaptureRegion(ClientPoint Origin, int Width, int Height)
{
    public void Validate()
    {
        if (Origin is null || !Origin.IsValid || Origin.ScaleWithWindow || Width is < 1 or > RasterSample.MaximumSide || Height is < 1 or > RasterSample.MaximumSide
            || Origin.Space == CoordinateSpace.WindowClient && (Origin.X + Width > Origin.Width || Origin.Y + Height > Origin.Height))
            throw new ArgumentException("Choose an area no larger than 256 × 256 pixels inside the Click target or monitor.");
    }
}
