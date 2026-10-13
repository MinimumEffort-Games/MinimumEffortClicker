using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private readonly HashSet<Guid> enabledToggles = [];
    private ToggleRow[] toggleRows = [];
    private Guid? editingToggle;
    private bool refreshingToggles;

    private sealed record ToggleRow(Guid Id, string Name, string Shortcut, bool Enabled);
    private IEnumerable<MacroDefinition> SavedToggles => profiles.SelectMany(p => p.Macros).Where(m => m.IsToggle);
    private void ToggleMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ToggleIntervalPanel is not null) ToggleIntervalPanel.Visibility = ToggleModeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ToggleInterval_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (ToggleIntervalLabel is not null) ToggleIntervalLabel.Text = $"{e.NewValue:0} ms · {1000 / e.NewValue:0.#} taps/sec";
    }
    private static string HeldKey(MacroDefinition macro) => InputLabel(macro.Plan!.Control);
    private static string InputLabel(InputControl input)
    { var label = RecordingNormalizer.Label(input); return label.StartsWith("Hold ", StringComparison.Ordinal) ? label[5..] : label; }

    private void RefreshToggleList()
    {
        if (ToggleList is null) return;
        refreshingToggles = true;
        try
        {
            toggleRows = SavedToggles.Select(m => new ToggleRow(m.Id, m.Name,
                macroBindings.TryGetValue(m.Id, out var binding) ? MacroBindings.KeyLabel(binding.Key) : "Choose shortcut", enabledToggles.Contains(m.Id))).ToArray();
            ToggleList.ItemsSource = toggleRows;
            ToggleList.SelectedItem = toggleRows.FirstOrDefault(r => r.Id == editingToggle);
            ToggleEmptyLabel.Visibility = toggleRows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { refreshingToggles = false; }
    }
    private void ToggleSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingToggles || ToggleList.SelectedItem is not ToggleRow row) return;
        var macro = SavedToggles.First(m => m.Id == row.Id);
        editingToggle = row.Id; ToggleModeBox.SelectedIndex = macro.Plan!.Mode == HelperMode.Repeat ? 1 : 0; ToggleIntervalSlider.Value = Math.Clamp(macro.Plan.Interval.TotalMilliseconds, 10, 1000);
        selectedHeldInput = macro.Plan!.Control; ToggleHeldKeyBox.Content = HeldKey(macro); ToggleNameBox.Text = macro.Name;
        selectedToggleKey = macroBindings.TryGetValue(row.Id, out var binding) ? binding.Key : 0;
        ToggleShortcutBox.Content = ShortcutKeys.Label(selectedToggleKey);
        SaveToggleButton.ToolTip = "Save changes";
        DeleteToggleButton.IsEnabled = true;
    }
    private void NewToggle_Click(object sender, RoutedEventArgs e)
    {
        editingToggle = null; ToggleList.SelectedIndex = -1; ToggleModeBox.SelectedIndex = 0; ToggleIntervalSlider.Value = 100;
        selectedToggleKey = 0; ToggleShortcutBox.Content = "Choose a key…"; ToggleNameBox.Text = "";
        SaveToggleButton.ToolTip = "Add toggle"; DeleteToggleButton.IsEnabled = false;
    }
    private async void SaveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || bindingBusy || ioBusy) return;
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) { ReasonLabel.Text = "Stop playback and finish cleanup before changing toggles."; return; }
        Pages.IsEnabled = false;
        try
        {
            var id = editingToggle ?? Guid.NewGuid();
            int key = selectedToggleKey;
            var binding = new MacroBinding(id, key, false);
            MacroBindings.Validate([binding], appliedToggleKey, appliedStopKey);
            if (macroBindings.Values.Any(b => b.MacroId != id && b.Key == key))
                throw new ArgumentException("That shortcut is assigned to another toggle. Choose a different key.");
            var held = InputLabel(selectedHeldInput);
            if (MinimumEffortClicker.Linux.DesktopRuntime.InputMatchesKey(selectedHeldInput, key))
                throw new ArgumentException("Choose a shortcut different from the input being held.");
            string name = (ToggleNameBox.Text ?? "").Trim(); if (name.Length == 0) name = "Toggle " + held;
            var macro = new MacroDefinition(id, name,
                new(selectedHeldInput, ToggleModeBox.SelectedIndex == 1 ? HelperMode.Repeat : HelperMode.Hold, TimeSpan.FromMilliseconds(ToggleModeBox.SelectedIndex == 1 ? ToggleIntervalSlider.Value : 100), TimeSpan.FromMilliseconds(ToggleModeBox.SelectedIndex == 1 ? 2 : 20), 0), IsToggle: true);
            var owner = profiles.FirstOrDefault(p => p.Macros.Any(m => m.Id == id))
                ?? profiles.FirstOrDefault(p => p.Macros.Count < 100 && p.Macros.All(m => m.IsToggle));
            var updated = owner is null
                ? new(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Toggles", [macro])
                : owner with { Macros = owner.Macros.Any(m => m.Id == id) ? owner.Macros.Select(m => m.Id == id ? macro : m).ToArray() : [.. owner.Macros, macro] };
            enabledToggles.Remove(id);
            await ApplyAvailability();
            await SaveProfiles(owner is null ? [.. profiles, updated] : profiles.Select(p => p.Id == owner.Id ? updated : p).ToList());
            macroBindings[id] = binding;
            await Task.Run(() => bindingsStore.Save(macroBindings.Values.ToArray()));
            enabledToggles.Add(id);
            editingToggle = id;
            await ApplyAvailability();
            RefreshToggleList(); SaveToggleButton.ToolTip = "Save changes"; DeleteToggleButton.IsEnabled = true;
            ReasonLabel.Text = enabledToggles.Contains(id)
                ? $"Saved. Tap {MacroBindings.KeyLabel(key)} in your Click target to {(macro.Plan!.Mode == HelperMode.Repeat ? "spam" : "hold")} {held}, then again to stop."
                : "Saved disabled. " + ReasonLabel.Text;
        }
        catch (Exception ex) { ReasonLabel.Text = "Could not save toggle: " + ex.Message; }
        finally { Pages.IsEnabled = true; RefreshToggleList(); }
    }
    private async void ToggleEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized || refreshingToggles || bindingBusy || ioBusy || sender is not CheckBox { DataContext: ToggleRow row } box) return;
        if (box.IsChecked == true) enabledToggles.Add(row.Id); else enabledToggles.Remove(row.Id);
        await ApplyAvailability();
    }
    private async void DeleteToggle_Click(object sender, RoutedEventArgs e)
    {
        if (editingToggle is not { } id || runtime is null || bindingBusy || ioBusy) return;
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) { ReasonLabel.Text = "Stop playback before removing a toggle."; return; }
        var owner = profiles.FirstOrDefault(p => p.Macros.Any(m => m.Id == id));
        if (owner is null) return;
        if (AppDialog.Show(this, "Remove this saved toggle?", "Remove toggle", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Pages.IsEnabled = false;
        try
        {
            enabledToggles.Remove(id); await ApplyAvailability();
            var remaining = owner.Macros.Where(m => m.Id != id).ToArray();
            await SaveProfiles(remaining.Length == 0 ? profiles.Where(p => p.Id != owner.Id).ToList()
                : profiles.Select(p => p.Id == owner.Id ? p with { Macros = remaining } : p).ToList());
            macroBindings.Remove(id); await Task.Run(() => bindingsStore.Save(macroBindings.Values.ToArray()));
            NewToggle_Click(sender, e); RefreshToggleList(); ReasonLabel.Text = "Toggle removed.";
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
        finally { Pages.IsEnabled = true; }
    }
    /// <summary>Arm the checked toggles' shortcuts in the runtime. Arming never starts anything; a shortcut press does.</summary>
    private async Task ApplyAvailability()
    {
        if (runtime is null || bindingBusy) return;
        bindingBusy = true; TogglePage.IsEnabled = false;
        try
        {
            var helpers = SavedToggles.Where(m => enabledToggles.Contains(m.Id)).Select(m => new ArmedHelper(macroBindings.GetValueOrDefault(m.Id)
                ?? throw new ArgumentException($"Assign a shortcut for '{m.Name}' first."), m.Plan!, DisplayName: m.Name, IsToggle: true)).ToArray();
            MacroBindings.Validate(helpers.Select(h => h.Binding).ToArray(), appliedToggleKey, appliedStopKey);
            if (!await runtime.ConfigureHelpersAsync(helpers)) throw new InvalidOperationException("A toggle shortcut is already in use. Choose another key.");
        }
        catch (Exception ex)
        {
            enabledToggles.Clear();
            await runtime.ConfigureHelpersAsync([]);
            ReasonLabel.Text = "Toggles disabled: " + ex.Message;
        }
        finally { bindingBusy = false; TogglePage.IsEnabled = true; RefreshToggleList(); }
    }
    private string[] ToggleOverlayLines(RuntimeSnapshot snapshot)
    {
        var ids = snapshot.ActiveHelperIds ?? [];
        return SavedToggles.Where(m => enabledToggles.Contains(m.Id)).Select(m =>
            $"{m.Name} · {(macroBindings.TryGetValue(m.Id, out var binding) ? MacroBindings.KeyLabel(binding.Key) : "Unassigned")} · {(snapshot.CleanupPending ? "Release pending" : ids.Contains(m.Id) ? snapshot.PlaybackActive ? "On" : snapshot.State : "Ready")}").ToArray();
    }
}
