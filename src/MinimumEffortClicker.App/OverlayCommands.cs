using System.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private void EnsureOverlay() => overlay ??= new();
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
}
