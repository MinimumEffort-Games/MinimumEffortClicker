namespace MinimumEffortClicker.Core;

/// <summary>Local presentation preferences for the status overlay. Never includes a running state.</summary>
/// <param name="AnchorX">Where the overlay sits across its Click target, see <see cref="OverlayAnchor"/>; 1, 0 is the top-right corner.</param>
/// <param name="Unlocked">The overlay can be dragged anywhere; it still stays in front and keeps that place relative to the target.</param>
public sealed record OverlayOptions(bool Expanded = false, bool KeepIdle = false, double Opacity = 0.94, double Scale = 1,
    double AnchorX = 1, double AnchorY = 0, bool Unlocked = false)
{
    public bool Valid => double.IsFinite(Opacity) && Opacity is >= 0.4 and <= 1
        && double.IsFinite(Scale) && Scale is >= 0.75 and <= 1.5 && OverlayAnchor.Valid(AnchorX, AnchorY);
}
