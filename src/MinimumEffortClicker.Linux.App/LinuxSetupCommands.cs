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
        bool captureDenied = control?.Helper is MinimumEffortClicker.Linux.Kde.KdeHelper { CaptureDenied: true };
        HelperStatusLabel.Text = captureDenied ? "KDE does not allow this copy of the app to capture the screen, so colour waits and screenshots fail. Choose Allow this copy of the app."
            : control?.Helper is not null
            ? (LinuxSetup.IsKde ? "Ready. Clicks, keys, window targets and screen capture go through KWin." : "Ready. Clicks, keys, window targets and screen capture go through the helper.")
            : (control?.HelperError ?? "Not checked yet.") + (LinuxSetup.IsGnome || LinuxSetup.IsKde ? "" : " The helper needs GNOME or KDE Plasma; other desktops are not supported yet.");
        // KDE has nothing to install: the button only lets this copy of the app use KWin.
        InstallHelperButton.IsVisible = !LinuxSetup.IsKde || control?.Helper is null || captureDenied;
        InstallHelperButton.Content = LinuxSetup.IsKde ? "Allow this copy of the app"
            : control?.Helper is not null ? "Reinstall GNOME helper" : control?.HelperAccessDenied == true ? "Allow this copy of the app" : "Install GNOME helper";
        bool ours = LinuxSetup.InputRuleInstalled;
        InputAccessLabel.Text = control?.PhysicalInputAvailable == true
            ? (ours ? "On." : "On: this system already lets the clicker read the keyboard and mouse, so nothing needs turning on.") + " " + InputAccessOn
            : "Off. " + InputAccessOptional + " " + InputAccessNeeds + " Turn on asks for your password in the system's own window.";
        if (LinuxSetup.IsSteamDeck) InputAccessLabel.Text += "\n\nSteam Deck: " + DeckLimits + "\n\nSteam's desktop buttons: " + DeckDesktopButtons;
        InputAccessNoticeBox.IsChecked = showInputAccessNotice;
        InputAccessCommand.Text = control?.PhysicalInputAvailable == true ? "" : LinuxSetup.InputAccessCommand;
        InputAccessCommand.IsVisible = CopyInputCommandButton.IsVisible = InputAccessTerminalHint.IsVisible = control?.PhysicalInputAvailable != true;
        // Without a system password window (pkexec) only the terminal command works.
        InputAccessOnButton.IsVisible = control?.PhysicalInputAvailable != true && LinuxSetup.SystemPasswordPrompt is not null;
        // Turn off only removes what Turn on installed; access the system gives by itself is not ours to take away.
        InputAccessOffButton.IsVisible = ours && LinuxSetup.InputAccessRemoveCommand is not null && LinuxSetup.SystemPasswordPrompt is not null;
        // Shown while the rule is installed, so people see how to undo it.
        InputAccessRemoveCommand.Text = LinuxSetup.InputAccessRemoveCommand ?? "";
        InputAccessScope.IsVisible = InputAccessRemoveCommand.IsVisible = CopyRemoveCommandButton.IsVisible = ours && LinuxSetup.InputAccessRemoveCommand is not null;
    }

    /// <summary>The KDE capture check runs beside the start; its answer goes to log.txt and Settings when it comes.</summary>
    private async Task ReportCaptureCheckAsync()
    {
        if (runtime?.Control.Helper is not MinimumEffortClicker.Linux.Kde.KdeHelper kde) return;
        string result = await kde.CaptureCheck;
        AppLog.Note("KDE", "screen capture check: " + result);
        if (!allowClose && !closing) RefreshLinuxSetup();
    }

    private async void InstallHelper_Click(object? sender, RoutedEventArgs e) => ReasonLabel.Text = (await SetUpHelperAsync()).Message;

    /// <summary>
    /// Installs or allows the desktop helper, then connects to it without a restart where the desktop allows that: KWin
    /// reads the new entry within about two seconds, and a GNOME helper that is already running reads its allow list on
    /// each call. A newly installed GNOME helper needs one log-out. On success every key is claimed again.
    /// </summary>
    private async Task<(bool Ready, string Message)> SetUpHelperAsync()
    {
        string installed;
        try { installed = LinuxSetup.InstallHelper(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { RefreshLinuxSetup(); return (false, "Could not set up the " + LinuxSetup.HelperName + ": " + ex.Message); }
        RefreshLinuxSetup();
        if (runtime is null) return (false, installed);
        await Task.Delay(TimeSpan.FromSeconds(LinuxSetup.IsKde ? 3 : 0.5));
        bool ready = await runtime.Control.RefreshPlatformAsync();
        if (ready) await ReclaimKeysAsync();
        RefreshLinuxSetup(); RefreshStatus();
        return ready ? (true, "Linux input controls are ready.") : (false, LinuxSetup.IsKde ? runtime.Control.Error ?? runtime.Control.PlatformSummary : installed);
    }

    /// <summary>
    /// After the helper or input access became ready: claims every key again as at startup. Keys that could not be claimed
    /// while the helper was missing (Record, Screenshot, Pause, toggle shortcuts) otherwise stayed dead until a restart,
    /// and a dead Record key was recorded and reached the app in front (Ubuntu, 2026-10-23).
    /// </summary>
    private async Task ReclaimKeysAsync()
    {
        if (runtime is null || !runtime.Control.Available) return;
        await runtime.Control.ConfigureAsync(appliedToggleKey, appliedStopKey);
        await SetQuickRecordKey();
        if (enabledToggles.Count > 0) await ApplyAvailability();
        await ConfigureScreenshotHotkey();
        await ConfigurePauseHotkey();
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
    // Input access is optional and only for recording (User, 2026-10-23): the notice and this card say so first.
    private const string InputAccessOptional = "The clicker works without this. Clicks, key presses, the Action List, toggles, colour waits, screenshots and the F-key shortcuts all work now.";
    private const string InputAccessNeeds = "Turn it on only if you want to record your own clicks and keys, stop a run by touching the mouse or keyboard, or use mouse buttons and other keys as shortcuts. For that the clicker needs to read your keyboard and mouse.";
    private const string InputAccessOn = "Recording, Stop on my input and mouse-button shortcuts are available.";
    /// <summary>
    /// What a Steam Deck can and cannot record: Steam reads the Deck's controls itself and types and clicks through the
    /// desktop, where no app may listen, so only an external mouse or keyboard reaches the recorder.
    /// </summary>
    /// <summary>What a Steam Deck can and cannot do, shown first in the startup notice and in Linux setup (User, 2026-10-19).</summary>
    private const string DeckLimits = "The Deck's own trackpads, buttons and on-screen keyboard can't be recorded: Steam types and clicks for them in a way no app can see. A USB or Bluetooth mouse or keyboard can be recorded once input access is on. Actions set up by hand (Set Location), toggles, Controller to Key, screenshots and the back-button shortcuts all work.";
    /// <summary>Steam's desktop controller layout presses keys for the back buttons, which collide with the clicker's shortcuts.</summary>
    private const string DeckDesktopButtons = "In Desktop Mode, Steam's desktop controller layout presses keys for the back buttons (for example L4 presses the Windows key), so a back button would do two things at once. To give them to the clicker only: Steam → Settings → Controller → Desktop Layout → Edit, and set L4, L5, R4 and R5 to nothing. Not needed if you only use the clicker while a game runs: a running game switches the desktop layout off.";
    private bool showInputAccessNotice = true;

    /// <summary>The Settings switch for the startup notice, so a notice turned off there or in the notice can come back.</summary>
    private void InputAccessNotice_Changed(object? sender, RoutedEventArgs e)
    {
        if (!initialized || InputAccessNoticeBox.IsChecked == showInputAccessNotice) return;
        showInputAccessNotice = InputAccessNoticeBox.IsChecked == true; PersistPreferences();
    }
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
            if (await runtime.Control.RefreshPlatformAsync()) await ReclaimKeysAsync();
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
    /// the rest of the app does, with a way to turn it on. On a Steam Deck it also shows while access is on: the Deck
    /// section comes first. "Show at startup" turns the notice off for good.
    /// </summary>
    private async Task ShowInputAccessNoticeAsync()
    {
        if (runtime is null || allowClose || closing) return;
        // Without the helper the clicker cannot click or see windows at all, so its setup shows even with the notice turned off.
        var helper = HelperSetup();
        bool accessOff = !runtime.Control.PhysicalInputAvailable;
        if (helper is null && (!showInputAccessNotice || !accessOff && !LinuxSetup.IsSteamDeck)) return;
        var notice = new InputAccessNoticeWindow(InputAccessOptional, InputAccessNeeds, InputAccessOn, accessOff, LinuxSetup.SystemPasswordPrompt is not null, LinuxSetup.InputAccessCommand,
            LinuxSetup.IsSteamDeck ? (DeckLimits, DeckDesktopButtons) : null, helper) { OwnerWindow = this };
        bool turnOn = notice.ShowDialog() == true;
        if (notice.ShowAtStartup != showInputAccessNotice) { showInputAccessNotice = notice.ShowAtStartup; InputAccessNoticeBox.IsChecked = showInputAccessNotice; PersistPreferences(); }
        if (turnOn) await SetInputAccessAsync(on: true);
    }

    /// <summary>The startup notice's helper section while the helper is not ready on GNOME or KDE; null when it is, or elsewhere.</summary>
    private (string Explanation, string Button, Func<Task<(bool Ready, string Message)>> SetUp)? HelperSetup()
    {
        var control = runtime?.Control;
        if (control is null || control.Helper is not null || !(LinuxSetup.IsGnome || LinuxSetup.IsKde)) return null;
        if (LinuxSetup.IsKde)
            return ("KDE has to allow this copy of the clicker once before it can click, press keys, see your windows and take screenshots. Nothing is installed.",
                "Allow this copy of the app", SetUpHelperAsync);
        return control.HelperAccessDenied
            ? ("The ME Clicker GNOME helper is installed, but it does not know this copy of the clicker yet (for example the Steam copy). Allow it once.",
                "Allow this copy of the app", SetUpHelperAsync)
            : ("On GNOME, the small ME Clicker helper lets the clicker click, press keys, see your windows and take screenshots. Install it once; GNOME then needs one log-out and log-in.",
                "Install GNOME helper", SetUpHelperAsync);
    }

    private async void RefreshPlatform_Click(object? sender, RoutedEventArgs e)
    {
        if (runtime is null) return;
        bool ready = await runtime.Control.RefreshPlatformAsync();
        if (ready) await ReclaimKeysAsync();
        RefreshLinuxSetup(); RefreshStatus();
        ReasonLabel.Text = ready ? "Linux input controls are ready." : runtime.Control.Error ?? runtime.Control.PlatformSummary;
    }
}
