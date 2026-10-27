using System.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private void EnsureOverlay()
    {
        if (overlay is not null) return;
        overlay = new();
        // Should the overlay still go, the next refresh makes a new one instead of failing on the closed window.
        overlay.Closed += (_, _) => overlay = null;
        overlay.ShowRequested += RestoreFromTray;
        overlay.ExitRequested += ExitApplication;
        overlay.LockToggled += () => SetOverlayUnlocked(!overlayOptions.Unlocked);
        // The overlay gets its new place at once; without RefreshStatus it kept the old one and the next placement put it back.
        overlay.Dropped += (x, y) => { overlayOptions = overlayOptions with { FreeX = x, FreeY = y }; RefreshStatus(); PersistPreferences(); };
    }
    // A short red line on the overlay, for example why a toggle did not switch on (User, 2026-10-19: "why is toggle not
    // working"). It shows for OverlayAlertMilliseconds, also when the overlay would otherwise be hidden.
    private const int OverlayAlertMilliseconds = 3000;
    private string? overlayAlert;
    private long overlayAlertUntil;
    private System.Windows.Threading.DispatcherTimer? overlayAlertTimer;
    private string? OverlayAlert => Environment.TickCount64 < overlayAlertUntil ? overlayAlert : null;
    /// <summary>The runtime refused a toggle press (any thread).</summary>
    private void ToggleRefused(string brief)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            overlayAlert = brief; overlayAlertUntil = Environment.TickCount64 + OverlayAlertMilliseconds;
            if (overlayAlertTimer is null)
            {
                overlayAlertTimer = new() { Interval = TimeSpan.FromMilliseconds(OverlayAlertMilliseconds + 100) };
                overlayAlertTimer.Tick += (_, _) => { overlayAlertTimer.Stop(); RefreshStatus(); };
            }
            overlayAlertTimer.Stop(); overlayAlertTimer.Start();
            RefreshStatus();
        });
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
