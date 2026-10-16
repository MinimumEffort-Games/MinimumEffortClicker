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
            ? "Ready. Recording, Stop on my input, mouse-button shortcuts and hotkeys that work in every app are available."
            : "Off. " + InputAccessNeeds + " Turn on asks for your password in the system's own window.";
        InputAccessCommand.Text = control?.PhysicalInputAvailable == true ? "" : LinuxSetup.InputAccessCommand;
        InputAccessCommand.IsVisible = CopyInputCommandButton.IsVisible = InputAccessTerminalHint.IsVisible = control?.PhysicalInputAvailable != true;
        // Without a system password window (pkexec) only the terminal command works.
        InputAccessOnButton.IsVisible = control?.PhysicalInputAvailable != true && LinuxSetup.SystemPasswordPrompt is not null;
        InputAccessOffButton.IsVisible = LinuxSetup.InputAccessRemoveCommand is not null && LinuxSetup.SystemPasswordPrompt is not null;
        // Green when input access is on, red when it is off.
        CheckInputAccessButton.Classes.Set("green", control?.PhysicalInputAvailable == true);
        CheckInputAccessButton.Classes.Set("red", control?.PhysicalInputAvailable != true);
        // Shown whether access is on or off: the rule is reversible, and people should see how.
        InputAccessRemoveCommand.Text = LinuxSetup.InputAccessRemoveCommand ?? "";
        InputAccessScope.IsVisible = InputAccessRemoveCommand.IsVisible = CopyRemoveCommandButton.IsVisible = LinuxSetup.InputAccessRemoveCommand is not null;
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

    private async void CopyRemoveCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not { } clipboard || LinuxSetup.InputAccessRemoveCommand is not { } command) return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, command);
        ReasonLabel.Text = "Remove command copied. Paste it into a terminal to turn input access off, then choose Check again.";
    }

    /// <summary>What physical input access unlocks; the Settings card and the startup notice say the same.</summary>
    private const string InputAccessNeeds = "Recording, Stop on my input, mouse-button shortcuts, hotkeys that work in every app and holding a hotkey to run need read access to your keyboard and mouse. Everything else works without it.";
    private bool showInputAccessNotice = true;

    private void InputAccessOn_Click(object? sender, RoutedEventArgs e) => _ = SetInputAccessAsync(on: true);
    private void InputAccessOff_Click(object? sender, RoutedEventArgs e) => _ = SetInputAccessAsync(on: false);

    /// <summary>Runs the input-access script through the system password window, then checks again by itself.</summary>
    private async Task<bool> SetInputAccessAsync(bool on)
    {
        if (runtime is null) return false;
        InputAccessOnButton.IsEnabled = InputAccessOffButton.IsEnabled = false;
        ReasonLabel.Text = "Waiting for your password in the system window…";
        try
        {
            var (done, message) = await LinuxSetup.SetInputAccessAsync(on);
            ReasonLabel.Text = message;
            if (!done) return false;
            // udev hands out the new access right after the script; give it a moment, then reconnect.
            await Task.Delay(TimeSpan.FromMilliseconds(800));
            await runtime.Control.RefreshPlatformAsync();
            RefreshStatus();
            bool available = runtime.Control.PhysicalInputAvailable;
            ReasonLabel.Text = on == available ? message : message + " The clicker has not picked it up yet; choose Check again, or restart the clicker.";
            return on == available;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        { ReasonLabel.Text = "Could not open the password window: " + ex.Message; return false; }
        finally { InputAccessOnButton.IsEnabled = InputAccessOffButton.IsEnabled = true; RefreshLinuxSetup(); }
    }

    /// <summary>
    /// At startup, while physical input access is off, explains why Record and Stop on my input do not work yet and that
    /// the rest of the app does, with a way to turn it on. "Show at startup" turns the notice off for good.
    /// </summary>
    private async Task ShowInputAccessNoticeAsync()
    {
        if (!showInputAccessNotice || runtime is null || runtime.Control.PhysicalInputAvailable || allowClose || closing) return;
        var notice = new InputAccessNoticeWindow(InputAccessNeeds, LinuxSetup.SystemPasswordPrompt is not null, LinuxSetup.InputAccessCommand) { OwnerWindow = this };
        bool turnOn = notice.ShowDialog() == true;
        if (notice.ShowAtStartup != showInputAccessNotice) { showInputAccessNotice = notice.ShowAtStartup; PersistPreferences(); }
        if (turnOn) await SetInputAccessAsync(on: true);
    }

    private async void RefreshPlatform_Click(object? sender, RoutedEventArgs e)
    {
        if (runtime is null) return;
        bool ready = await runtime.Control.RefreshPlatformAsync();
        RefreshLinuxSetup(); RefreshStatus();
        ReasonLabel.Text = ready ? "Linux input controls are ready." : runtime.Control.Error ?? runtime.Control.PlatformSummary;
    }
}
