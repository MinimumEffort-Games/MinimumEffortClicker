using Avalonia.Controls;
using Avalonia.Interactivity;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Storage;

namespace MinimumEffortClicker.App;

/// <summary>
/// The Controller to Key page: a simple controller-to-keyboard mapper. While a bound controller
/// button is held its key or mouse button is held, in whichever window is in front.
/// </summary>
public partial class MainWindow
{
    private readonly ControllerKeyRouter controllerKeys = new();
    private List<ControllerKeyBind> controllerBinds = [];
    private ControllerKeyBinds? controllerBindStore;
    // False when the file exists but could not be read: saving then would overwrite the user's binds.
    private bool controllerBindsReadable = true;
    private Guid? editingControllerBind;
    private ControllerButton? selectedBindButton;
    // The key or mouse button the bind presses; null until one is chosen.
    private InputControl? selectedBindOutput;
    private bool refreshingControllerBinds;

    private sealed record ControllerBindRow(Guid Id, string Name, ControllerButton Button, InputControl Output, bool Active)
    {
        /// <summary>The controller button's picture, and the key's keycap or the mouse with its button outlined.</summary>
        public Control ButtonPicture => UiArt.ControllerGlyph(Button, 26);
        public Control OutputPicture => UiArt.InputPicture(Output, OutputLabel(Output), 30);
        /// <summary>What screen readers say for the row.</summary>
        public override string ToString() => Name;
    }

    private static string OutputLabel(InputControl output) => output.Kind == InputKind.MouseButton
        ? ShortcutKeys.MouseLabel((PointerButton)output.Code)
        : SequenceKeys.Supported.FirstOrDefault(k => k.Value == output.Code).Key ?? "Key";

    /// <summary>
    /// A bind's name. Binds saved without a name were called "Menu → Left mouse" (or Right, Middle, Back, Forward
    /// mouse); those show the short mouse name (Menu → LMB), and saving the bind keeps it. Typed names stay as they are.
    /// </summary>
    private static string BindName(ControllerKeyBind bind)
    {
        if (bind.Output.Kind != InputKind.MouseButton) return bind.Name;
        string old = (PointerButton)bind.Output.Code switch { PointerButton.Left => "Left", PointerButton.Right => "Right", PointerButton.Middle => "Middle", PointerButton.XButton1 => "Back", _ => "Forward" };
        return bind.Name == $"{ControllerButtons.Label(bind.Button)} → {old} mouse" ? $"{ControllerButtons.Label(bind.Button)} → {OutputLabel(bind.Output)}" : bind.Name;
    }

    /// <summary>At startup: loads the saved binds and arms the active ones. Arming presses nothing.</summary>
    private async Task LoadControllerBinds()
    {
        controllerBindStore = new ControllerKeyBinds(store.DirectoryPath);
        var loaded = await Task.Run(controllerBindStore.Load);
        controllerBindsReadable = loaded is not null;
        controllerBinds = loaded?.ToList() ?? [];
        if (!controllerBindsReadable) ControllerBindStatusLabel.Text = "The saved Controller to Key binds could not be read; they are left as they are. " + controllerBindStore.FilePath;
        RefreshControllerBindList(); NewControllerBind_Click(this, new RoutedEventArgs()); ApplyControllerBinds();
        runtime!.StopIssued += () => SendControllerOutputs(controllerKeys.ReleaseAll().Select(o => (o, false)));
    }

    /// <summary>Arms the active binds; anything held by the previous set is let go first.</summary>
    private void ApplyControllerBinds() => SendControllerOutputs(controllerKeys.Configure(controllerBinds).Select(o => (o, false)));

    private void SendControllerOutputs(IEnumerable<(InputControl Output, bool Down)> edges)
    {
        if (runtime is null) return;
        foreach (var (output, down) in edges)
            // The desktop repeats a held key itself (also for virtual keyboards), so nothing is repeated here.
            runtime.SendControllerOutput(output, down);
    }

    /// <summary>On the controller thread: a bound button presses or releases its key.</summary>
    private void RouteControllerButton(ControllerButton button, bool down) => SendControllerOutputs(controllerKeys.Edge(button, down));

    private void RefreshControllerBindList()
    {
        refreshingControllerBinds = true;
        try
        {
            var rows = controllerBinds.Select(b => new ControllerBindRow(b.Id, BindName(b), b.Button, b.Output, b.Active)).ToArray();
            ControllerBindList.ItemsSource = rows;
            ControllerBindList.SelectedItem = rows.FirstOrDefault(r => r.Id == editingControllerBind);
            ControllerBindEmptyLabel.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { refreshingControllerBinds = false; }
        ShowControllerBindReady();
    }

    /// <summary>Chooses the key or mouse button to press on the keyboard-and-mouse picker.</summary>
    private void ChooseBindOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new KeyPickerWindow(this, "Key or mouse button to press", "Click the key or mouse button the controller button holds.", selectedBindOutput, _ => null, withMouse: true);
        if (picker.ShowDialog() != true || picker.Selected is not { } output) return;
        selectedBindOutput = output;
        ShowControllerBindReady();
    }

    private InputControl? ChosenOutput() => selectedBindOutput;

    /// <summary>What still stops the bind from being saved, or null; Save stays grey until it is complete.</summary>
    private string? ControllerBindMissing()
    {
        if (!controllerBindsReadable) return "The saved binds could not be read, so nothing is saved over them.";
        if (ChosenOutput() is null) return "Choose a key or a mouse button to press.";
        if (selectedBindButton is not { } button) return "Press Controller binding, then a controller button.";
        if (controllerControls.CommandFor(button) is { } command)
            return $"{ControllerButtons.Label(button)} is {ControlName(command)} under Settings → Controller. Choose another button, or clear it there.";
        if (controllerBinds.FirstOrDefault(b => b.Button == button && b.Id != editingControllerBind) is { } other)
            return $"{ControllerButtons.Label(button)} already presses \"{other.Name}\".";
        if (ToggleUsing(button) is { } toggle) return $"{ControllerButtons.Label(button)} switches the toggle \"{ToggleName(toggle)}\". Choose another button, or remove it from that toggle.";
        if (editingControllerBind is null && controllerBinds.Count >= ControllerKeyBinds.MaxBinds) return $"At most {ControllerKeyBinds.MaxBinds} binds.";
        return null;
    }

    private void ShowControllerBindReady()
    {
        if (SaveControllerBindButton is null) return;
        string? missing = ControllerBindMissing();
        SaveControllerBindButton.IsEnabled = missing is null;
        ToolTip.SetTip(SaveControllerBindButton, missing ?? (editingControllerBind is null ? "Add bind" : "Save changes"));
        ControllerBindButtonBox.Content = selectedBindButton is { } b ? UiArt.ControllerGlyph(b, 24) : "Press to choose a button…";
        ControllerOutputBox.Content = selectedBindOutput is { } o ? UiArt.InputContent(o, OutputLabel(o)) : "Choose a key or mouse button…";
        ControllerOutputBox.SetButtonStyle(selectedBindOutput is null ? "NeedsChoiceButton" : "ChosenButton");
        ControllerBindButtonBox.Classes.Set("needs-choice", selectedBindButton is null); ControllerBindButtonBox.Classes.Set("chosen", selectedBindButton is not null);
        ControllerBindControllerLabel.Text = gamepads is null ? "" : gamepads.Error ?? (gamepads.Connected.Count == 0
            ? "No controller is connected right now."
            : "Connected: " + string.Join(", ", gamepads.Connected) + ".");
    }

    private void CaptureBindButton_Click(object sender, RoutedEventArgs e)
    {
        // A button a global control or another bind already uses is refused in the window itself, with the reason.
        var button = CaptureControllerButton("Controller button for this bind", pressed =>
            controllerControls.CommandFor(pressed) is { } command
                ? $"{ControllerButtons.Label(pressed)} is {ControlName(command)} under Settings → Controller. Press another button, or clear it there first."
            : controllerBinds.FirstOrDefault(b => b.Button == pressed && b.Id != editingControllerBind) is { } other
                ? $"{ControllerButtons.Label(pressed)} already presses \"{other.Name}\". Press another button."
            : ToggleUsing(pressed) is { } toggle
                ? $"{ControllerButtons.Label(pressed)} switches the toggle \"{ToggleName(toggle)}\". Press another button, or remove it from that toggle first."
            : null, selectedBindButton);
        if (button is not { } chosen) return;
        selectedBindButton = chosen;
        ControllerBindStatusLabel.Text = ControllerBindMissing() ?? $"Controller binding: {ControllerButtons.Label(chosen)}.";
        ShowControllerBindReady();
    }

    private void NewControllerBind_Click(object sender, RoutedEventArgs e)
    {
        editingControllerBind = null; selectedBindButton = null; selectedBindOutput = null;
        refreshingControllerBinds = true;
        ControllerBindList.SelectedIndex = -1; ControllerBindNameBox.Text = "";
        refreshingControllerBinds = false;
        DeleteControllerBindButton.IsEnabled = false;
        ShowControllerBindReady();
    }

    private void ControllerBindSelection_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (refreshingControllerBinds || ControllerBindList.SelectedItem is not ControllerBindRow row) return;
        var bind = controllerBinds.First(b => b.Id == row.Id);
        editingControllerBind = bind.Id; selectedBindButton = bind.Button; selectedBindOutput = bind.Output;
        refreshingControllerBinds = true;
        ControllerBindNameBox.Text = BindName(bind);
        refreshingControllerBinds = false;
        DeleteControllerBindButton.IsEnabled = true;
        ShowControllerBindReady();
    }

    private async void SaveControllerBind_Click(object sender, RoutedEventArgs e)
    {
        if (ControllerBindMissing() is { } missing) { ControllerBindStatusLabel.Text = missing; return; }
        var output = ChosenOutput()!.Value; var button = selectedBindButton!.Value;
        string name = (ControllerBindNameBox.Text ?? "").Trim();
        if (name.Length == 0) name = $"{ControllerButtons.Label(button)} → {OutputLabel(output)}";
        var id = editingControllerBind ?? Guid.NewGuid();
        bool active = controllerBinds.FirstOrDefault(b => b.Id == id)?.Active ?? true;
        var bind = new ControllerKeyBind(id, name, button, output, active);
        var updated = controllerBinds.Any(b => b.Id == id) ? controllerBinds.Select(b => b.Id == id ? bind : b).ToList() : [.. controllerBinds, bind];
        if (await SaveControllerBinds(updated))
        {
            editingControllerBind = id;
            ControllerBindStatusLabel.Text = $"Saved. Holding {ControllerButtons.Label(button)} holds {OutputLabel(output)}.";
            RefreshControllerBindList(); DeleteControllerBindButton.IsEnabled = true;
        }
    }

    private async void DeleteControllerBind_Click(object sender, RoutedEventArgs e)
    {
        if (editingControllerBind is not { } id) return;
        if (AppDialog.Show(this, "Remove this bind?", "Remove bind", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (await SaveControllerBinds(controllerBinds.Where(b => b.Id != id).ToList()))
        {
            NewControllerBind_Click(sender, e); RefreshControllerBindList();
            ControllerBindStatusLabel.Text = "Bind removed.";
        }
    }

    private async void ControllerBindActive_Click(object? sender, RoutedEventArgs e)
    {
        if (refreshingControllerBinds || sender is not Avalonia.Controls.Primitives.ToggleButton { DataContext: ControllerBindRow row } box) return;
        var updated = controllerBinds.Select(b => b.Id == row.Id ? b with { Active = box.IsChecked == true } : b).ToList();
        if (!await SaveControllerBinds(updated)) RefreshControllerBindList();
    }

    /// <summary>Writes the binds, then arms the new set. False (with the reason shown) when they could not be written.</summary>
    private async Task<bool> SaveControllerBinds(List<ControllerKeyBind> updated)
    {
        if (controllerBindStore is null || !controllerBindsReadable) return false;
        try
        {
            await Task.Run(() => controllerBindStore.Save(updated));
            controllerBinds = updated; ApplyControllerBinds();
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            ControllerBindStatusLabel.Text = "Could not save the binds: " + ex.Message;
            AppLog.Failure("Save Controller to Key binds", ex);
            return false;
        }
    }
}
