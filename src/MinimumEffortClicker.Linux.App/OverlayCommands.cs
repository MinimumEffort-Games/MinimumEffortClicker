using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private void EnsureOverlay()
    {
        if (overlay is not null) return;
        overlay = new();
        overlay.ShowRequested += BringToFront;
        overlay.ExitRequested += ExitApplication;
        overlay.LockToggled += () => SetOverlayUnlocked(!overlayOptions.Unlocked);
        overlay.Dropped += (x, y) => { overlayOptions = overlayOptions with { FreeX = x, FreeY = y }; PersistPreferences(); };
    }

    /// <summary>The overlay's square and a second launch of the app: show the clicker in front, also when GNOME would only
    /// say it "is ready".</summary>
    internal void BringToFront()
    {
        RestoreFromTray();
        runtime?.ActivateOwnWindow(Title ?? "");
    }
    private void DisplaysChanged(object? sender, EventArgs e)
    {
        CancelScreenshot("Display changed");
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => overlay?.Constrain());
    }
    private void OverlayOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        overlayOptions = overlayOptions with
        {
            Expanded = false, KeepIdle = IdleOverlayBox.IsChecked == true,
            Opacity = OverlayOpacity.Value, Scale = OverlayScale.Value
        };
        RefreshStatus(); overlay?.Constrain(); PersistPreferences();
    }
    /// <summary>A click on the Settings picture: the overlay goes to that point on the Click target's edge.</summary>
    private void OverlayAnchor_Chosen(double x, double y) { if (initialized) SetOverlayAnchor(x, y); }
    /// <summary>The overlay's padlock: unlocked, the overlay can be dragged.</summary>
    /// <summary>
    /// The overlay's padlock. Unlocking keeps the overlay where it is on the screen, and it stays there while the Click
    /// target moves; locking turns where it is now into its anchor on the target. Either way it does not jump.
    /// </summary>
    private void SetOverlayUnlocked(bool unlocked)
    {
        if (overlayOptions.Unlocked == unlocked) return;
        if (unlocked && overlay?.CurrentPosition() is { } at) overlayOptions = overlayOptions with { FreeX = at.X, FreeY = at.Y };
        if (!unlocked && overlay?.CurrentAnchor() is { } anchor)
        {
            overlayOptions = overlayOptions with { AnchorX = anchor.X, AnchorY = anchor.Y };
            OverlayAnchorPicture.Show(anchor.X, anchor.Y);
        }
        overlayOptions = overlayOptions with { Unlocked = unlocked };
        RefreshStatus(); PersistPreferences();
    }
    /// <summary>A spot picked on the Settings picture: the overlay goes there on the Click target, locked.</summary>
    private void SetOverlayAnchor(double x, double y)
    {
        overlayOptions = overlayOptions with { AnchorX = x, AnchorY = y, Unlocked = false };
        OverlayAnchorPicture.Show(x, y); RefreshStatus(); PersistPreferences();
    }
}
