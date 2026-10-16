using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MinimumEffortClicker.Controllers;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private GamepadListener? gamepads;
    private ControllerControls controllerControls = ControllerControls.Default;
    // The hints show the controller's buttons after controller input and the keyboard's after keyboard input. The
    // mouse never switches them: on a Steam Deck the mouse and the controller are used together.
    private volatile bool controllerHints;
    // Set while Settings waits for the button of one control; controller presses then choose instead of act.
    private volatile bool capturingController;
    private ControllerCommand capturingCommand;
    private CancellationTokenSource? controllerCaptureTimeout;

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
        AddHandler(KeyDownEvent, (_, _) => KeyboardSeen(), RoutingStrategies.Tunnel, handledEventsToo: true);
        ShowControllerStatus();
    }

    /// <summary>On the controller thread.</summary>
    private void ControllerButtonChanged(ControllerButton button, bool down)
    {
        if (down && !controllerHints) { controllerHints = true; RefreshHints(); }
        // While a button is being chosen, the press chooses and does nothing else.
        if (down && (capturingController || capturingBind))
        {
            Dispatcher.BeginInvoke(() => { if (capturingBind) FinishBindCapture(button); else FinishControllerCapture(button); });
            return;
        }
        if (down && controllerControls.CommandFor(button) is { } command) runtime?.Control.RunControllerCommand(command);
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
        ControllerStatus.Text = gamepads.Error ?? (names.Count == 0
            ? "No controller found. Connect one or turn it on; it is picked up straight away."
            : "Connected: " + string.Join(", ", names) + ". The buttons below work in any window, like the keys above.");
    }

    private void ShowControllerButtons()
    {
        static string Label(ControllerButton? button) => button is { } b ? ControllerButtons.Label(b) : "None";
        ControllerStartBox.Content = Label(controllerControls.StartStop);
        ControllerStopBox.Content = Label(controllerControls.StopAll);
        ControllerRecordBox.Content = Label(controllerControls.Record);
        ControllerScreenshotBox.Content = Label(controllerControls.Screenshot);
    }

    private static ControllerCommand CommandOf(object sender) => Enum.Parse<ControllerCommand>((string)((Button)sender).Tag!);

    private async void CaptureControllerButton_Click(object sender, RoutedEventArgs e)
    {
        if (gamepads is null) return;
        controllerCaptureTimeout?.Cancel();
        ShowControllerButtons();
        capturingBind = false; capturingCommand = CommandOf(sender); capturingController = true;
        ((Button)sender).Content = "Press a button…";
        ControllerBindStatus.Text = gamepads.Connected.Count == 0 ? "No controller is connected yet; connect one and press a button." : "Press a controller button.";
        using var timeout = new CancellationTokenSource(); controllerCaptureTimeout = timeout;
        try { await Task.Delay(TimeSpan.FromSeconds(10), timeout.Token); }
        catch (OperationCanceledException) { return; }
        capturingController = false; ShowControllerButtons();
        ControllerBindStatus.Text = "No button pressed; nothing changed.";
    }

    private void FinishControllerCapture(ControllerButton button)
    {
        if (!capturingController) return;
        capturingController = false; controllerCaptureTimeout?.Cancel();
        var previous = controllerControls;
        controllerControls = controllerControls.With(capturingCommand, button);
        var moved = previous.CommandFor(button) is { } other && other != capturingCommand ? $" {ControlName(other)} now uses {Label(previous.ButtonFor(capturingCommand))}." : "";
        ControllerBindStatus.Text = $"{ControlName(capturingCommand)}: {ControllerButtons.Label(button)}.{moved}";
        ShowControllerButtons(); PersistPreferences(); RefreshStatus();
        static string Label(ControllerButton? b) => b is { } x ? ControllerButtons.Label(x) : "no button";
    }

    private static string ControlName(ControllerCommand command) => command switch
    {
        ControllerCommand.StartStop => "Start / toggle", ControllerCommand.StopAll => "Stop all",
        ControllerCommand.Record => "Record Macro / Stop", _ => "Screenshot"
    };

    private void ClearControllerButton_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || sender is null) return;
        var command = CommandOf(sender);
        capturingController = false; controllerCaptureTimeout?.Cancel();
        controllerControls = controllerControls.With(command, null);
        ControllerBindStatus.Text = $"{ControlName(command)} has no controller button now.";
        ShowControllerButtons(); PersistPreferences(); RefreshStatus();
        e.Handled = true;
    }

    private void ResetControllerButtons_Click(object sender, RoutedEventArgs e)
    {
        capturingController = false; controllerCaptureTimeout?.Cancel();
        controllerControls = ControllerControls.Default;
        ControllerBindStatus.Text = "Controller buttons reset: A Start / toggle, B Stop all, Y Record, X Screenshot.";
        ShowControllerButtons(); PersistPreferences(); RefreshStatus();
    }
}
