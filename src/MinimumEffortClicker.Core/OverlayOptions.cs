namespace MinimumEffortClicker.Core;

/// <summary>Local presentation preferences. Never includes an unlocked or running state.</summary>
public sealed record OverlayOptions(bool Expanded = false, bool KeepIdle = false, bool AnchorTarget = true,
    double Opacity = 0.94, double Scale = 1, int? X = null, int? Y = null)
{
    public bool Valid => double.IsFinite(Opacity) && Opacity is >= 0.4 and <= 1
        && double.IsFinite(Scale) && Scale is >= 0.75 and <= 1.5
        && X.HasValue == Y.HasValue && (X is null || X is >= -1000000 and <= 1000000)
        && (Y is null || Y is >= -1000000 and <= 1000000);
}
