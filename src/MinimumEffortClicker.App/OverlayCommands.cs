using System.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private void EnsureOverlay()
    {
        if (overlay is not null) return;
        overlay = new();
        overlay.ShowRequested += RestoreFromTray;
        overlay.ExitRequested += ExitApplication;
        overlay.LockToggled += () => SetOverlayUnlocked(!overlayOptions.Unlocked);
        overlay.AnchorDragged += (x, y) => SetOverlayAnchor(x, y);
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
            Expanded = false, KeepIdle = IdleOverlayBox.IsChecked == true, Opacity = OverlayOpacity.Value, Scale = OverlayScale.Value
        };
        RefreshStatus(); overlay?.Constrain(); PersistPreferences();
    }
    /// <summary>A click on the Settings picture: the overlay goes to that point on the Click target's edge.</summary>
    private void OverlayAnchor_Chosen(double x, double y) { if (initialized) SetOverlayAnchor(x, y); }
    /// <summary>The overlay's padlock: unlocked, the overlay can be dragged.</summary>
    private void SetOverlayUnlocked(bool unlocked)
    {
        if (overlayOptions.Unlocked == unlocked) return;
        overlayOptions = overlayOptions with { Unlocked = unlocked };
        RefreshStatus(); PersistPreferences();
    }
    private void SetOverlayAnchor(double x, double y)
    {
        overlayOptions = overlayOptions with { AnchorX = x, AnchorY = y };
        OverlayAnchorPicture.Show(x, y); RefreshStatus(); PersistPreferences();
    }
}
