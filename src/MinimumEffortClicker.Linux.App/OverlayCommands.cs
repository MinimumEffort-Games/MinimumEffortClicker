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
    }

    /// <summary>The overlay's square: shows the clicker in front, also when GNOME would only say it "is ready".</summary>
    private void BringToFront()
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
}
