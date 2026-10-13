namespace MinimumEffortClicker.Core;

/// <summary>Local presentation preferences for the status overlay. Never includes a running state.</summary>
public sealed record OverlayOptions(bool Expanded = false, bool KeepIdle = false, double Opacity = 0.94, double Scale = 1)
{
    public bool Valid => double.IsFinite(Opacity) && Opacity is >= 0.4 and <= 1
        && double.IsFinite(Scale) && Scale is >= 0.75 and <= 1.5;
}
