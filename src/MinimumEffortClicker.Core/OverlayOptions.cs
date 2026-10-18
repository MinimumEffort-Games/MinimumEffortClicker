namespace MinimumEffortClicker.Core;

/// <summary>Local presentation preferences for the status overlay. Never includes a running state.</summary>
/// <param name="AnchorX">Where the locked overlay sits on its Click target, see <see cref="OverlayAnchor"/>; 1, 0 is the top-right corner.</param>
/// <param name="Unlocked">The overlay can be dragged anywhere; it still stays in front.</param>
/// <param name="FreeX">Where the unlocked overlay was dropped, in screen pixels (its top-left corner). It stays there and does
/// not follow the Click target (User, 2026-10-18). Null until it is first unlocked.</param>
public sealed record OverlayOptions(bool Expanded = false, bool KeepIdle = false, double Opacity = 0.94, double Scale = 1,
    double AnchorX = 1, double AnchorY = 0, bool Unlocked = false, int? FreeX = null, int? FreeY = null)
{
    /// <summary>Far beyond any desktop; a larger value is a corrupt file.</summary>
    public const int MaxScreenCoordinate = 1_000_000;

    public bool Valid => double.IsFinite(Opacity) && Opacity is >= 0.4 and <= 1
        && double.IsFinite(Scale) && Scale is >= 0.75 and <= 1.5 && OverlayAnchor.Valid(AnchorX, AnchorY)
        && Math.Abs(FreeX ?? 0) <= MaxScreenCoordinate && Math.Abs(FreeY ?? 0) <= MaxScreenCoordinate;

    /// <summary>The unlocked overlay's own place on the screen, when it has one.</summary>
    public (int X, int Y)? FreePlace => Unlocked && FreeX is { } x && FreeY is { } y ? (x, y) : null;
}
