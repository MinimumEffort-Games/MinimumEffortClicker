using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private int selectedStartKey = 0x77, selectedStopKey = 0x78, selectedToggleKey;
    private InputControl selectedHeldInput = InputControl.Key(0x11);
    private async Task<int?> CaptureShortcut(string purpose)
    {
        if (runtime is null || screenshotBusy || bindingBusy || ioBusy || quickOperation) return null;
        runtime.Stop("Stopped before choosing an input.");
        for (int i = 0; i < 80 && runtime.Snapshot.Busy; i++) await Task.Delay(25);
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) { ReasonLabel.Text = "Finish input cleanup before choosing a key."; return null; }
        quickEditorOpen = true; bindingBusy = true;
        try
        {
            await runtime.Control.SuspendBindingsAsync(true);
            var capture = new ShortcutCaptureWindow(this, purpose);
            return capture.ShowDialog() == true ? capture.SelectedKey : null;
        }
        finally
        {
            await runtime.Control.SuspendBindingsAsync(false);
            quickEditorOpen = false; bindingBusy = false; RefreshStatus();
        }
    }
    private async void CaptureStartKey_Click(object sender, RoutedEventArgs e)
    { if (await CaptureShortcut("Actions start / stop shortcut") is { } key) { selectedStartKey = key; await ApplyHotkeysAsync(); } }
    private async void CaptureStopKey_Click(object sender, RoutedEventArgs e)
    { if (await CaptureShortcut("Stop All shortcut") is { } key) { selectedStopKey = key; await ApplyHotkeysAsync(); } }
    private async void CaptureRecordKey_Click(object sender, RoutedEventArgs e)
    { if (await CaptureShortcut("Record Macro / Stop shortcut") is { } key) { selectedRecordKey = key; await SetQuickRecordKey(); } }
    private void CaptureToggleKey_Click(object sender, RoutedEventArgs e)
    {
        if (bindingBusy || ioBusy || quickOperation) return;
        var picker = new KeyPickerWindow(this, "Toggle shortcut", "Click the key or mouse button that switches this toggle on and off.", ShortcutInput(selectedToggleKey),
            input => MinimumEffortClicker.Linux.DesktopRuntime.VirtualKeyOf(input) is var key && key != 0 && ShortcutKeys.Supported(key) ? null : "this key cannot be a shortcut.", withMouse: true);
        if (picker.ShowDialog() != true || picker.Selected is not { } input) return;
        selectedToggleKey = MinimumEffortClicker.Linux.DesktopRuntime.VirtualKeyOf(input); ShowToggleShortcut();
    }
    private void CaptureHeldInput_Click(object sender, RoutedEventArgs e)
    {
        if (bindingBusy || ioBusy || quickOperation) return;
        var picker = new KeyPickerWindow(this, "Key or mouse button to hold", "Click the key or mouse button this toggle holds or taps.", selectedHeldInput, _ => null, withMouse: true);
        if (picker.ShowDialog() != true || picker.Selected is not { } input) return;
        selectedHeldInput = input; ShowHeldInput();
    }
    /// <summary>A shortcut's virtual key as the key or mouse button it is, for the picker's outline; null for none.</summary>
    private static InputControl? ShortcutInput(int key)
    {
        if (key == 0) return null;
        try { return MinimumEffortClicker.Linux.DesktopRuntime.InputFromVirtualKey(key); } catch (ArgumentException) { return null; }
    }
    private void ShowHeldInput() => ToggleHeldKeyBox.Content = UiArt.InputContent(selectedHeldInput, InputLabel(selectedHeldInput));
    private void ShowToggleShortcut() => ToggleShortcutBox.Content = UiArt.InputContent(ShortcutInput(selectedToggleKey), ShortcutKeys.Label(selectedToggleKey));
    private async void ResetGlobalKeys_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || bindingBusy || recordKeyChanging || quickOperation || screenshotBusy || ioBusy) return;
        runtime.Stop("Stopped before resetting keybinds.");
        for (int i = 0; i < 80 && runtime.Snapshot.Busy; i++) await Task.Delay(25);
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) { ReasonLabel.Text = "Finish input cleanup before resetting keybinds."; return; }
        selectedStartKey = 0x77; selectedStopKey = 0x78; selectedRecordKey = 0x79;
        selectedScreenshotKey = ShortcutKeys.DefaultScreenshotKey; ScreenshotHotkeyBox.IsChecked = true;
        screenshotSettings = screenshotSettings with { Hotkey = ShortcutKeys.DefaultScreenshotKey, HotkeyEnabled = true };
        await ApplyHotkeysAsync();
    }
}
