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
        overlay.LockRequested += LockOverlay;
        overlay.StopRequested += () => runtime?.Stop();
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
    private async void UnlockOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || bindingBusy || ioBusy || overlay?.IsEditing == true) return;
        bindingBusy = true;
        try
        {
            runtime.ArmedPlan = null;
            enabledProfiles.Clear(); enabledMacros.Clear(); enabledToggles.Clear(); RefreshToggleList();
            runtime.Stop("Stopped before adjusting the overlay.");
            if (!await runtime.ConfigureHelpersAsync([])) throw new InvalidOperationException("Could not disable profile bindings.");
            ProfileSelection_Changed(this, null!);
            for (int i = 0; i < 60 && runtime.Snapshot.Busy; i++) await Task.Delay(50);
            if (closing || allowClose) return;
            runtime.BeginOverlayEditing(_ => Dispatcher.BeginInvoke(LockOverlay));
            EnsureOverlay(); overlay!.SetEditing(true); RefreshStatus();
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
        finally { bindingBusy = false; }
    }
    private void LockOverlay()
    {
        overlay?.SetEditing(false); runtime?.EndOverlayEditing(); RefreshStatus(); PersistPreferences();
    }
    private void LockOverlay_Click(object sender, RoutedEventArgs e) => LockOverlay();
}
