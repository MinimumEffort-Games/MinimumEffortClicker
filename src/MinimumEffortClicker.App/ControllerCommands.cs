using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MinimumEffortClicker.Controllers;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private GamepadListener? gamepads;
    /// <summary>Running on a Steam Deck: its controller shortcuts default to the back buttons.</summary>
    private static readonly bool OnSteamDeck = ReadSteamDeck();
    private static bool ReadSteamDeck()
    {
        try
        {
            using var bios = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            return SteamDeckHardware.Is(bios?.GetValue("SystemManufacturer") as string, bios?.GetValue("SystemProductName") as string);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }
    private ControllerControls controllerControls = ControllerControls.DefaultFor(OnSteamDeck);
    // The hints show the controller's buttons after controller input and the keyboard's after keyboard input. The
    // mouse never switches them: on a Steam Deck the mouse and the controller are used together.
    private volatile bool controllerHints;
    // Open while a button is being chosen; controller presses then choose instead of act.
    private ControllerCaptureWindow? controllerCapture;
    private volatile bool capturingButton;
    private string? loggedControllerState;
    // Buttons pressed since the start; only touched on the controller thread.
    private readonly HashSet<ControllerButton> seenButtons = [];

    private string StartHint => controllerHints && controllerControls.StartStop is { } b ? ControllerButtons.Label(b) : MacroBindings.KeyLabel(appliedToggleKey);
    private string StopHint => controllerHints && controllerControls.StopAll is { } b ? ControllerButtons.Label(b) : stopKey;
    private string RecordHint => controllerHints && controllerControls.Record is { } b ? ControllerButtons.Label(b) : ShortcutKeys.Label(appliedRecordKey);

    /// <summary>Starts reading controllers. Reading only; a press acts only through a bound control.</summary>
    private void StartControllers()
    {
        if (gamepads is not null || runtime is null) return;
        gamepads = new GamepadListener();
        gamepads.ButtonChanged += ControllerButtonChanged;
        gamepads.DevicesChanged += () => { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { ShowControllerStatus(); ShowControllerBindReady(); }); };
        runtime.Control.PhysicalChanged += (input, down) => { if (down && input.Kind == InputKind.ScanCode) KeyboardSeen(); };
        PreviewKeyDown += (_, _) => KeyboardSeen();
        ShowControllerStatus();
    }

    /// <summary>On the controller thread.</summary>
    private void ControllerButtonChanged(ControllerButton button, bool down)
    {
        if (down && !controllerHints) { controllerHints = true; RefreshHints(); }
        // While a button is being chosen, the press chooses and does nothing else.
        if (down && capturingButton) { Dispatcher.BeginInvoke(() => controllerCapture?.Press(button)); return; }
        // log.txt names the first press of each button and every global control a button runs, so a report shows which
        // buttons reach the clicker (a Steam Deck layout can turn a back button or the trackpad into something else).
        if (down && seenButtons.Add(button)) AppLog.Note("Controllers", $"first press of {ControllerButtons.Label(button)}");
        if (down && controllerControls.CommandFor(button) is { } command)
        {
            AppLog.Note("Controllers", $"{ControllerButtons.Label(button)} runs {ControlName(command)}");
            runtime?.Control.RunControllerCommand(command);
        }
        // A toggle's controller button does what tapping its shortcut does.
        if (down && armedToggleButtons.TryGetValue(button, out var toggle)) runtime?.TriggerToggle(toggle);
        RouteControllerButton(button, down);
    }

    private void KeyboardSeen()
    {
        if (!controllerHints) return;
        controllerHints = false; RefreshHints();
    }

    private void RefreshHints() { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(RefreshStatus); }

    private void ShowControllerStatus()
    {
        if (gamepads is null) return;
        var names = gamepads.Connected;
        // Each change is written to log.txt once, so a controller that is not seen can be traced in a bug report.
        string state = gamepads.Error ?? (names.Count == 0 ? "none connected" : "connected: " + string.Join(", ", names));
        if (state != loggedControllerState) { loggedControllerState = state; AppLog.Note("Controllers", state); }
        ControllerStatus.Text = gamepads.Error ?? (names.Count == 0
            ? "No controller found."
            : "Connected: " + string.Join(", ", names) + ". The buttons below work in any window, like the keys above.");
    }

    private void ShowControllerButtons()
    {
        static string Label(ControllerButton? button) => button is { } b ? ControllerButtons.Label(b) : "None";
        ControllerStartBox.Content = Label(controllerControls.StartStop);
        ControllerStopBox.Content = Label(controllerControls.StopAll);
        ControllerRecordBox.Content = Label(controllerControls.Record);
        ControllerScreenshotBox.Content = Label(controllerControls.Screenshot);
        ControllerPauseBox.Content = Label(controllerControls.Pause);
    }

    private static ControllerCommand CommandOf(object sender) => Enum.Parse<ControllerCommand>((string)((Button)sender).Tag);

    /// <summary>Everything bound now, for the capture window: the global controls, then the Controller to Key binds and toggles.</summary>
    private IReadOnlyList<(string What, ControllerButton? Button)> BoundControllerButtons()
    {
        var bound = new List<(string, ControllerButton?)>();
        foreach (var command in Enum.GetValues<ControllerCommand>())
            bound.Add((ControlName(command), controllerControls.ButtonFor(command)));
        foreach (var bind in controllerBinds) bound.Add(($"{bind.Name} (presses {OutputLabel(bind.Output)})", bind.Button));
        foreach (var toggle in SavedToggles)
            if (macroBindings.TryGetValue(toggle.Id, out var binding) && binding.Controller is { } pad) bound.Add(($"{ToggleName(toggle)} (toggle)", pad));
        return bound;
    }

    /// <summary>Shows the capture window and returns the button pressed, or null when cancelled.</summary>
    private ControllerButton? CaptureControllerButton(string purpose, Func<ControllerButton, string?> taken, ControllerButton? current)
    {
        if (gamepads is null || controllerCapture is not null) return null;
        var window = new ControllerCaptureWindow(this, purpose, BoundControllerButtons(), taken, gamepads.Connected.Count > 0, current);
        controllerCapture = window; capturingButton = true;
        try { return window.ShowDialog() == true ? window.SelectedButton : null; }
        finally { capturingButton = false; controllerCapture = null; }
    }

    private void CaptureControllerButton_Click(object sender, RoutedEventArgs e)
    {
        var command = CommandOf(sender);
        // The global controls may swap buttons among themselves; a button a Controller to Key bind uses stays with it.
        var button = CaptureControllerButton($"Controller button for {ControlName(command)}", pressed =>
            controllerBinds.FirstOrDefault(b => b.Button == pressed) is { } bind
                ? $"{ControllerButtons.Label(pressed)} presses \"{bind.Name}\" under Controller to Key. Press another button, or remove that bind first."
                : ToggleUsing(pressed) is { } toggle
                ? $"{ControllerButtons.Label(pressed)} switches the toggle \"{ToggleName(toggle)}\". Press another button, or remove it from that toggle first."
                : null, controllerControls.ButtonFor(command));
        if (button is not { } chosen) return;
        var previous = controllerControls;
        controllerControls = controllerControls.With(command, chosen);
        var moved = previous.CommandFor(chosen) is { } other && other != command ? $" {ControlName(other)} now uses {Label(previous.ButtonFor(command))}." : "";
        ControllerBindStatus.Text = $"{ControlName(command)}: {ControllerButtons.Label(chosen)}.{moved}";
        ShowControllerButtons(); PersistPreferences(); RefreshStatus(); ShowControllerBindReady();
        static string Label(ControllerButton? b) => b is { } x ? ControllerButtons.Label(x) : "no button";
    }

    /// <summary>
    /// What a controller button already does, in words, or null when it is free: a control under Settings → Controller,
    /// a Controller to Key bind or a toggle. The bind or toggle being edited does not count.
    /// </summary>
    private string? ControllerButtonUse(ControllerButton button, Guid? exceptBind = null, Guid? exceptToggle = null) =>
        controllerControls.CommandFor(button) is { } command ? $"{ControllerButtons.Label(button)} is {ControlName(command)} under Settings → Controller."
        : controllerBinds.FirstOrDefault(b => b.Button == button && b.Id != exceptBind) is { } bind ? $"{ControllerButtons.Label(button)} presses \"{bind.Name}\" under Controller to Key."
        : ToggleUsing(button, exceptToggle) is { } toggle ? $"{ControllerButtons.Label(button)} switches the toggle \"{ToggleName(toggle)}\"."
        : null;

    private static string ControlName(ControllerCommand command) => command switch
    {
        ControllerCommand.StartStop => "Start / Stop", ControllerCommand.StopAll => "Stop all", ControllerCommand.Pause => "Pause / Unpause",
        ControllerCommand.Record => "Record Macro / Stop", _ => "Screenshot"
    };

    /// <summary>
    /// Settings from before the Pause button give it View. When a Controller to Key bind or a toggle already uses View,
    /// that keeps it and Pause starts without a button, so one press never does two things.
    /// </summary>
    private void SettleControllerPause()
    {
        if (controllerControls.Pause is not { } pause || !controllerBinds.Any(b => b.Button == pause) && ToggleUsing(pause) is null) return;
        controllerControls = controllerControls with { Pause = null };
        ShowControllerButtons(); PersistPreferences();
    }

    private void ClearControllerButton_Click(object sender, MouseButtonEventArgs e)
    {
        var command = CommandOf(sender);
        controllerControls = controllerControls.With(command, null);
        ControllerBindStatus.Text = $"{ControlName(command)} has no controller button now.";
        ShowControllerButtons(); PersistPreferences(); RefreshStatus(); ShowControllerBindReady();
        e.Handled = true;
    }

    private void ResetControllerButtons_Click(object sender, RoutedEventArgs e)
    {
        // A default button that a Controller to Key bind uses stays with that bind; its control is left without one.
        var reset = ControllerControls.DefaultFor(OnSteamDeck);
        string defaults = $"{ControllerButtons.Label(reset.StartStop!.Value)} Start / Stop, {ControllerButtons.Label(reset.StopAll!.Value)} Stop all, {ControllerButtons.Label(reset.Pause!.Value)} Pause, "
            + $"{ControllerButtons.Label(reset.Record!.Value)} Record, {ControllerButtons.Label(reset.Screenshot!.Value)} Screenshot";
        var kept = new List<string>();
        foreach (var command in Enum.GetValues<ControllerCommand>())
            if (reset.ButtonFor(command) is { } b && (controllerBinds.Any(bind => bind.Button == b) || ToggleUsing(b) is not null))
            { reset = reset.With(command, null); kept.Add($"{ControllerButtons.Label(b)} stays with {(ToggleUsing(b) is { } toggle ? $"the toggle \"{ToggleName(toggle)}\"" : "Controller to Key")}, so {ControlName(command)} has none"); }
        controllerControls = reset;
        ControllerBindStatus.Text = $"Controller buttons reset: {defaults}." + (kept.Count > 0 ? " " + string.Join("; ", kept) + "." : "");
        ShowControllerButtons(); PersistPreferences(); RefreshStatus(); ShowControllerBindReady();
    }
}
