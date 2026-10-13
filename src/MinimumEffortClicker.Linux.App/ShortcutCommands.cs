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
    private int selectedStartKey = 0x77, selectedStopKey = 0x78, selectedToggleKey, selectedMacroKey;
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
    private async void CaptureToggleKey_Click(object sender, RoutedEventArgs e)
    { if (await CaptureShortcut("Toggle shortcut") is { } key) { selectedToggleKey = key; ToggleShortcutBox.Content = ShortcutKeys.Label(key); } }
    private async void CaptureMacroKey_Click(object sender, RoutedEventArgs e)
    { if (await CaptureShortcut("Saved macro shortcut") is { } key) { selectedMacroKey = key; MacroKeyBox.Content = ShortcutKeys.Label(key); } }
    private void ClearMacroKey_Click(object sender, RoutedEventArgs e) { selectedMacroKey = 0; MacroKeyBox.Content = "Choose a key…"; }
    private async void CaptureHeldInput_Click(object sender, RoutedEventArgs e)
    {
        if (await CaptureShortcut("Key or mouse button to hold") is not { } key) return;
        try
        {
            selectedHeldInput = MinimumEffortClicker.Linux.DesktopRuntime.InputFromVirtualKey(key);
            ToggleHeldKeyBox.Content = InputLabel(selectedHeldInput);
        }
        catch (ArgumentException ex) { ReasonLabel.Text = ex.Message; }
    }
    private async void ResetGlobalKeys_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || bindingBusy || recordKeyChanging || quickOperation || screenshotBusy || ioBusy) return;
        runtime.Stop("Stopped before resetting keybinds.");
        for (int i = 0; i < 80 && runtime.Snapshot.Busy; i++) await Task.Delay(25);
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) { ReasonLabel.Text = "Finish input cleanup before resetting keybinds."; return; }
        selectedStartKey = 0x77; selectedStopKey = 0x78; selectedRecordKey = 0x79;
        selectedScreenshotKey = 0; ScreenshotHotkeyBox.IsChecked = false;
        screenshotSettings = screenshotSettings with { Hotkey = 0, HotkeyEnabled = false };
        await ApplyHotkeysAsync();
    }
}
