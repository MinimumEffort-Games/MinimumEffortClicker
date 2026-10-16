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
    private static readonly PointerButton[] BindMouseButtons = [PointerButton.Left, PointerButton.Right, PointerButton.Middle, PointerButton.XButton1, PointerButton.XButton2];
    private readonly ControllerKeyRouter controllerKeys = new();
    private List<ControllerKeyBind> controllerBinds = [];
    private ControllerKeyBinds? controllerBindStore;
    // False when the file exists but could not be read: saving then would overwrite the user's binds.
    private bool controllerBindsReadable = true;
    private Guid? editingControllerBind;
    private ControllerButton? selectedBindButton;
    private volatile bool capturingBind;
    private bool refreshingControllerBinds;

    private sealed record ControllerBindRow(Guid Id, string Name, string Button, string Output, bool Active);

    private static string OutputLabel(InputControl output) => output.Kind == InputKind.MouseButton
        ? (PointerButton)output.Code switch { PointerButton.Left => "Left mouse", PointerButton.Right => "Right mouse", PointerButton.Middle => "Middle mouse", PointerButton.XButton1 => "Back mouse", _ => "Forward mouse" }
        : SequenceKeys.Supported.FirstOrDefault(k => k.Value == output.Code).Key ?? "Key";

    /// <summary>At startup: fills the key list, loads the saved binds and arms the active ones. Arming presses nothing.</summary>
    private async Task LoadControllerBinds()
    {
        refreshingControllerBinds = true;
        ControllerKeyBox.Items.Clear();
        ControllerKeyBox.Items.Add("Select a key…");
        foreach (var name in SequenceKeys.Supported.Keys) ControllerKeyBox.Items.Add(name);
        ControllerKeyBox.SelectedIndex = 0;
        refreshingControllerBinds = false;
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
        foreach (var (output, down) in edges) runtime.SendControllerOutput(output, down);
    }

    /// <summary>On the controller thread: a bound button presses or releases its key.</summary>
    private void RouteControllerButton(ControllerButton button, bool down) => SendControllerOutputs(controllerKeys.Edge(button, down));

    private void RefreshControllerBindList()
    {
        refreshingControllerBinds = true;
        try
        {
            var rows = controllerBinds.Select(b => new ControllerBindRow(b.Id, b.Name, ControllerButtons.Label(b.Button), OutputLabel(b.Output), b.Active)).ToArray();
            ControllerBindList.ItemsSource = rows;
            ControllerBindList.SelectedItem = rows.FirstOrDefault(r => r.Id == editingControllerBind);
            ControllerBindEmptyLabel.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { refreshingControllerBinds = false; }
        ShowControllerBindReady();
    }

    private void ControllerKeyChoice_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // One output per bind: choosing a key clears the mouse button, and the other way round.
        if (refreshingControllerBinds || ControllerMouseBox is null) return;
        if (ControllerKeyBox.SelectedIndex > 0) ControllerMouseBox.SelectedIndex = 0;
        ShowControllerBindReady();
    }

    private void ControllerMouseChoice_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (refreshingControllerBinds || ControllerKeyBox is null) return;
        if (ControllerMouseBox.SelectedIndex > 0) ControllerKeyBox.SelectedIndex = 0;
        ShowControllerBindReady();
    }

    private InputControl? ChosenOutput() =>
        ControllerKeyBox.SelectedIndex > 0 && ControllerKeyBox.SelectedItem is string name && SequenceKeys.Supported.TryGetValue(name, out int code) ? InputControl.Key(code)
        : ControllerMouseBox.SelectedIndex > 0 ? InputControl.Mouse(BindMouseButtons[ControllerMouseBox.SelectedIndex - 1]) : null;

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
        if (editingControllerBind is null && controllerBinds.Count >= ControllerKeyBinds.MaxBinds) return $"At most {ControllerKeyBinds.MaxBinds} binds.";
        return null;
    }

    private void ShowControllerBindReady()
    {
        if (SaveControllerBindButton is null) return;
        string? missing = ControllerBindMissing();
        SaveControllerBindButton.IsEnabled = missing is null;
        ToolTip.SetTip(SaveControllerBindButton, missing ?? (editingControllerBind is null ? "Add bind" : "Save changes"));
        ControllerBindButtonBox.Content = capturingBind ? "Press a controller button…" : selectedBindButton is { } b ? ControllerButtons.Label(b) : "Press to choose a button…";
        ControllerBindButtonBox.Classes.Set("needs-choice", selectedBindButton is null); ControllerBindButtonBox.Classes.Set("chosen", selectedBindButton is not null);
        ControllerBindControllerLabel.Text = gamepads is null ? "" : gamepads.Error ?? (gamepads.Connected.Count == 0
            ? "No controller is connected right now."
            : "Connected: " + string.Join(", ", gamepads.Connected) + ".");
    }

    private async void CaptureBindButton_Click(object sender, RoutedEventArgs e)
    {
        if (gamepads is null) return;
        controllerCaptureTimeout?.Cancel();
        capturingController = false; capturingBind = true; ShowControllerBindReady();
        ControllerBindStatusLabel.Text = gamepads.Connected.Count == 0 ? "No controller is connected yet; connect one and press a button." : "Press a controller button.";
        using var timeout = new CancellationTokenSource(); controllerCaptureTimeout = timeout;
        try { await Task.Delay(TimeSpan.FromSeconds(10), timeout.Token); }
        catch (OperationCanceledException) { return; }
        capturingBind = false; ShowControllerBindReady();
        ControllerBindStatusLabel.Text = "No button pressed; nothing changed.";
    }

    private void FinishBindCapture(ControllerButton button)
    {
        if (!capturingBind) return;
        capturingBind = false; controllerCaptureTimeout?.Cancel();
        selectedBindButton = button;
        ControllerBindStatusLabel.Text = ControllerBindMissing() ?? $"Controller binding: {ControllerButtons.Label(button)}.";
        ShowControllerBindReady();
    }

    private void NewControllerBind_Click(object sender, RoutedEventArgs e)
    {
        editingControllerBind = null; selectedBindButton = null; capturingBind = false;
        refreshingControllerBinds = true;
        ControllerBindList.SelectedIndex = -1; ControllerBindNameBox.Text = "";
        ControllerKeyBox.SelectedIndex = 0; ControllerMouseBox.SelectedIndex = 0;
        refreshingControllerBinds = false;
        DeleteControllerBindButton.IsEnabled = false;
        ShowControllerBindReady();
    }

    private void ControllerBindSelection_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (refreshingControllerBinds || ControllerBindList.SelectedItem is not ControllerBindRow row) return;
        var bind = controllerBinds.First(b => b.Id == row.Id);
        editingControllerBind = bind.Id; selectedBindButton = bind.Button; capturingBind = false;
        refreshingControllerBinds = true;
        ControllerBindNameBox.Text = bind.Name;
        if (bind.Output.Kind == InputKind.MouseButton) { ControllerKeyBox.SelectedIndex = 0; ControllerMouseBox.SelectedIndex = Array.IndexOf(BindMouseButtons, (PointerButton)bind.Output.Code) + 1; }
        else { ControllerMouseBox.SelectedIndex = 0; ControllerKeyBox.SelectedItem = OutputLabel(bind.Output); }
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
        if (refreshingControllerBinds || sender is not CheckBox { DataContext: ControllerBindRow row } box) return;
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
