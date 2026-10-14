using Avalonia.Interactivity;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

/// <summary>Settings → Linux setup: the desktop helper (GNOME extension or KDE access) and physical input access.</summary>
public partial class MainWindow
{
    private void RefreshLinuxSetup()
    {
        LinuxSessionLabel.Text = LinuxSetup.SessionSummary;
        var control = runtime?.Control;
        HelperHeading.Text = LinuxSetup.HelperName[..1].ToUpperInvariant() + LinuxSetup.HelperName[1..];
        HelperStatusLabel.Text = control?.Helper is not null
            ? (LinuxSetup.IsKde ? "Ready. Clicks, keys, window targets and screen capture go through KWin." : "Ready. Clicks, keys, window targets and screen capture go through the helper.")
            : (control?.HelperError ?? "Not checked yet.") + (LinuxSetup.IsGnome || LinuxSetup.IsKde ? "" : " The helper needs GNOME or KDE Plasma; other desktops are not supported yet.");
        // KDE has nothing to install: the button only lets this copy of the app use KWin.
        InstallHelperButton.IsVisible = !LinuxSetup.IsKde || control?.Helper is null;
        InstallHelperButton.Content = LinuxSetup.IsKde ? "Allow this copy of the app"
            : control?.Helper is not null ? "Reinstall GNOME helper" : control?.HelperAccessDenied == true ? "Allow this copy of the app" : "Install GNOME helper";
        InputAccessLabel.Text = control?.PhysicalInputAvailable == true
            ? "Ready. Hotkeys work in every app, mouse-button shortcuts and recording are available, and touching your mouse or keyboard stops playback."
            : "Off. Recording, mouse-button shortcuts and the physical-input safety stop need read access to your keyboard and mouse. Run this once in a terminal (it asks for your password):";
        InputAccessCommand.Text = control?.PhysicalInputAvailable == true ? "" : LinuxSetup.InputAccessCommand;
        InputAccessCommand.IsVisible = InputAccessTools.IsVisible = control?.PhysicalInputAvailable != true;
        // Shown whether access is on or off: the rule is reversible, and people should see how.
        InputAccessRemoveCommand.Text = LinuxSetup.InputAccessRemoveCommand ?? "";
        InputAccessScope.IsVisible = InputAccessRemoveCommand.IsVisible = LinuxSetup.InputAccessRemoveCommand is not null;
    }

    private async void InstallHelper_Click(object? sender, RoutedEventArgs e)
    {
        try { ReasonLabel.Text = LinuxSetup.InstallHelper(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ReasonLabel.Text = "Could not set up the " + LinuxSetup.HelperName + ": " + ex.Message; RefreshLinuxSetup(); return; }
        RefreshLinuxSetup();
        if (!LinuxSetup.IsKde || runtime is null) return;
        // KWin reads the new entry within about two seconds; then connect without a log-out.
        await Task.Delay(TimeSpan.FromSeconds(3));
        bool ready = await runtime.Control.RefreshPlatformAsync();
        RefreshLinuxSetup(); RefreshStatus();
        ReasonLabel.Text = ready ? "Linux input controls are ready." : runtime.Control.Error ?? runtime.Control.PlatformSummary;
    }

    private async void CopyInputCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not { } clipboard) return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, LinuxSetup.InputAccessCommand);
        ReasonLabel.Text = "Command copied. Paste it into a terminal, then choose Check again.";
    }

    private async void RefreshPlatform_Click(object? sender, RoutedEventArgs e)
    {
        if (runtime is null) return;
        bool ready = await runtime.Control.RefreshPlatformAsync();
        RefreshLinuxSetup(); RefreshStatus();
        ReasonLabel.Text = ready ? "Linux input controls are ready." : runtime.Control.Error ?? runtime.Control.PlatformSummary;
    }
}
