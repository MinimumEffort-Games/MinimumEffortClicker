using System.Windows;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private int selectedStartKey = ShortcutKeys.DefaultStartKey, selectedStopKey = ShortcutKeys.DefaultStopKey, selectedToggleKey;
    // Empty until chosen, like the other choices in the editor; Clear editor empties it again.
    private InputControl? selectedHeldInput;
    private async Task<int?> CaptureShortcut(string purpose)
    {
        if (runtime is null || !operations.Idle) return null;
        runtime.Stop("Stopped before choosing an input.");
        for (int i = 0; i < 80 && runtime.Snapshot.Busy; i++) await Task.Delay(25);
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) { ReasonLabel.Text = "Finish input cleanup before choosing a key."; return null; }
        operations.Begin(Operation.Dialog); operations.Begin(Operation.Binding);
        try
        {
            await runtime.Control.SuspendBindingsAsync(true);
            var capture = new ShortcutCaptureWindow(this, purpose);
            return capture.ShowDialog() == true ? capture.SelectedKey : null;
        }
        catch (Exception ex) { ReasonLabel.Text = "The shortcut could not be changed: " + ex.Message; return null; }
        finally
        {
            try { await runtime.Control.SuspendBindingsAsync(false); }
            catch (Exception ex) { ReasonLabel.Text = "Shortcuts could not be restored: " + ex.Message; }
            operations.End(Operation.Dialog); operations.End(Operation.Binding); RefreshStatus();
        }
    }
    private async void CaptureStartKey_Click(object sender, RoutedEventArgs e)
    { if (await CaptureShortcut("Actions start / stop shortcut") is { } key) { selectedStartKey = key; await ApplyHotkeysAsync(); } }
    private async void CaptureStopKey_Click(object sender, RoutedEventArgs e)
    {
        if (await CaptureShortcut("Stop All shortcut") is not { } key) return;
        selectedStopKey = key; await ApplyHotkeysAsync();
        // F1–F11 are registered system-wide hotkeys; anything else relies on the input hook, which Windows does
        // not deliver while an administrator window is in front. Ctrl+Alt+F9 is registered as the backup then.
        if (ShortcutKeys.UsesHook(key))
            ReasonLabel.Text = runtime?.Control.BackupStopActive == true
                ? $"Saved. While an administrator (elevated) window is in front this key does not reach the clicker; {DesktopControl.BackupStopLabel} still stops everything."
                : $"Saved. Note: this Stop All key will not work while an administrator (elevated) window is in front, and {DesktopControl.BackupStopLabel} is taken by another app. F1–F11 always work.";
    }
    private async void CaptureRecordKey_Click(object sender, RoutedEventArgs e)
    { if (await CaptureShortcut("Record Macro / Stop shortcut") is { } key) { selectedRecordKey = key; await SetQuickRecordKey(); } }
    private void CaptureToggleKey_Click(object sender, RoutedEventArgs e)
    {
        if (!operations.Idle) return;
        var picker = new KeyPickerWindow(this, "Toggle shortcut", "Click the key or mouse button that switches this toggle on and off.", ShortcutInput(selectedToggleKey),
            input => DesktopRuntime.VirtualKeyOf(input) is var key && key != 0 && ShortcutKeys.Supported(key) ? null : "this key cannot be a shortcut.", withMouse: true);
        if (picker.ShowDialog() != true || picker.Selected is not { } input) return;
        selectedToggleKey = DesktopRuntime.VirtualKeyOf(input); ShowToggleShortcut();
    }
    private void CaptureHeldInput_Click(object sender, RoutedEventArgs e)
    {
        if (!operations.Idle) return;
        var picker = new KeyPickerWindow(this, "Key or mouse button to hold", "Click the key or mouse button this toggle holds or taps.", selectedHeldInput, _ => null, withMouse: true);
        if (picker.ShowDialog() != true || picker.Selected is not { } input) return;
        selectedHeldInput = input; ShowHeldInput();
    }
    /// <summary>A shortcut's virtual key as the key or mouse button it is, for the picker's outline; null for none.</summary>
    private static InputControl? ShortcutInput(int key)
    {
        if (key == 0) return null;
        try { return DesktopRuntime.InputFromVirtualKey(key); } catch (ArgumentException) { return null; }
    }
    private void ShowHeldInput()
    {
        ToggleHeldKeyBox.Content = selectedHeldInput is { } held ? UiArt.InputContent(held, InputLabel(held)) : "Choose a key or mouse button…";
        ToggleHeldKeyBox.SetResourceReference(StyleProperty, selectedHeldInput is null ? "NeedsChoiceButton" : "ChosenButton");
        RefreshToggleSave();
    }
    private void ShowToggleShortcut()
    {
        ToggleShortcutBox.Content = selectedToggleKey == 0 ? "Keyboard/Mouse" : UiArt.InputContent(ShortcutInput(selectedToggleKey), ShortcutKeys.Label(selectedToggleKey));
        RefreshToggleSave();
    }
    private void ClearToggleKey_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { selectedToggleKey = 0; ShowToggleShortcut(); e.Handled = true; }
    private async void ResetGlobalKeys_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || !operations.Idle || operations.Has(Operation.RecordKey)) return;
        runtime.Stop("Stopped before resetting keybinds.");
        for (int i = 0; i < 80 && runtime.Snapshot.Busy; i++) await Task.Delay(25);
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) { ReasonLabel.Text = "Finish input cleanup before resetting keybinds."; return; }
        selectedStartKey = ShortcutKeys.DefaultStartKey; selectedStopKey = ShortcutKeys.DefaultStopKey; selectedRecordKey = ShortcutKeys.DefaultRecordKey; selectedPauseKey = ShortcutKeys.DefaultPauseKey;
        selectedScreenshotKey = ShortcutKeys.DefaultScreenshotKey; ScreenshotHotkeyBox.IsChecked = true;
        screenshotSettings = screenshotSettings with { Hotkey = ShortcutKeys.DefaultScreenshotKey, HotkeyEnabled = true };
        // Applies Start, Stop All, Record, Screenshot and Pause together (ApplyHotkeysAsync registers all of them).
        await ApplyHotkeysAsync();
    }
}
