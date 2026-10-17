using System.Windows;
using System.Windows.Controls;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private readonly HashSet<Guid> enabledToggles = [];
    // The active toggles saved for the next start. Changed only by the user's own toggle edits, so a toggle that
    // cannot be armed at startup (its key taken by another app) is tried again next time instead of forgotten.
    private Guid[] rememberedToggles = [];
    private ToggleRow[] toggleRows = [];
    private Guid? editingToggle;
    private bool refreshingToggles;

    private sealed record ToggleRow(Guid Id, string Name, string Shortcut, bool Enabled, int Key, InputControl Held, string HeldLabel, ControllerButton? Pad = null)
    {
        /// <summary>The key or mouse button the toggle holds or taps, as its keycap or the mouse with its button outlined.</summary>
        public System.Windows.FrameworkElement HeldPicture => UiArt.InputPicture(Held, HeldLabel, 30);
        /// <summary>What screen readers say for the row.</summary>
        public override string ToString() => Name;
        /// <summary>The shortcut as its keycap or the mouse with its button outlined.</summary>
        public System.Windows.FrameworkElement ShortcutPicture
        {
            get
            {
                // The key or mouse button and the controller button side by side, whichever the toggle has.
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, ToolTip = Shortcut };
                if (Key != 0) row.Children.Add(UiArt.InputPicture(ShortcutInput(Key), ShortcutKeys.Label(Key), 30));
                if (Pad is { } pad) { var glyph = UiArt.ControllerGlyph(pad, 26); glyph.Margin = new Thickness(Key != 0 ? 6 : 0, 0, 0, 0); glyph.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(glyph); }
                if (row.Children.Count == 0) row.Children.Add(new TextBlock { Text = Shortcut });
                return row;
            }
        }
    }
    private IEnumerable<MacroDefinition> SavedToggles => profiles.SelectMany(p => p.Macros).Where(m => m.IsToggle);
    private void ToggleMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ToggleIntervalPanel is not null) ToggleIntervalPanel.Visibility = ToggleModeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ToggleInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ToggleIntervalLabel is not null) ToggleIntervalLabel.Text = $"{e.NewValue:0} ms · {1000 / e.NewValue:0.#} taps/sec";
    }
    /// <summary>What a toggle holds or taps: a key's name, or the short mouse name (LMB, RMB, MMB, M4, M5) so a mouse
    /// button is never mistaken for an arrow key.</summary>
    private static string InputLabel(InputControl input) => input.Kind == InputKind.MouseButton
        ? ShortcutKeys.MouseLabel((PointerButton)input.Code)
        : SequenceKeys.Supported.FirstOrDefault(k => k.Value == input.Code).Key ?? "Key";
    /// <summary>
    /// A toggle's name. Toggles saved without a name were called "Toggle Left" for the left mouse button, the same as
    /// the Left arrow key; those show the new name (Toggle LMB), and saving the toggle keeps it. Names the user typed
    /// stay as they are.
    /// </summary>
    private static string ToggleName(MacroDefinition macro) =>
        macro.Plan is { Control: { Kind: InputKind.MouseButton } held } && macro.Name == "Toggle " + (PointerButton)held.Code
            ? "Toggle " + InputLabel(held) : macro.Name;

    private void RefreshToggleList()
    {
        if (ToggleList is null) return;
        refreshingToggles = true;
        try
        {
            toggleRows = SavedToggles.Select(m => new ToggleRow(m.Id, ToggleName(m),
                macroBindings.TryGetValue(m.Id, out var binding) ? ShortcutLabel(binding) : "Not set", enabledToggles.Contains(m.Id),
                binding?.Key ?? 0, m.Plan!.Control,
                $"{(m.Plan.Mode == HelperMode.Repeat ? "Taps" : "Holds")} {InputLabel(m.Plan.Control)}", binding?.Controller)).ToArray();
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
        selectedHeldInput = macro.Plan!.Control; ShowHeldInput(); ToggleNameBox.Text = ToggleName(macro);
        selectedToggleKey = macroBindings.TryGetValue(row.Id, out var binding) ? binding.Key : 0;
        selectedToggleController = binding?.Controller; ShowToggleController();
        ShowToggleShortcut();
        SaveToggleButton.ToolTip = "Save changes";
        DeleteToggleButton.IsEnabled = true;
    }
    private void NewToggle_Click(object sender, RoutedEventArgs e)
    {
        editingToggle = null; ToggleList.SelectedIndex = -1; ToggleModeBox.SelectedIndex = 0; ToggleIntervalSlider.Value = 100;
        selectedToggleKey = 0; ShowToggleShortcut(); selectedToggleController = null; ShowToggleController(); ToggleNameBox.Text = "";
        SaveToggleButton.ToolTip = "Add toggle"; DeleteToggleButton.IsEnabled = false;
    }
    private async void SaveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || operations.Any(Operation.Binding | Operation.Library)) return;
        if (runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending) { ReasonLabel.Text = "Stop playback and finish cleanup before changing toggles."; return; }
        Pages.IsEnabled = false;
        try
        {
            var id = editingToggle ?? Guid.NewGuid();
            int key = selectedToggleKey;
            if (key == 0 && selectedToggleController is null) throw new ArgumentException("Choose a keyboard or mouse shortcut, a controller button, or both.");
            var binding = new MacroBinding(id, key, false, selectedToggleController);
            MacroBindings.Validate([binding], appliedToggleKey, appliedStopKey);
            if (key != 0 && macroBindings.Values.Any(b => b.MacroId != id && b.Key == key))
                throw new ArgumentException("That shortcut is assigned to another toggle. Choose a different key.");
            if (selectedToggleController is { } pad && ControllerButtonUse(pad, exceptToggle: id) is { } padUse)
                throw new ArgumentException(padUse + " Choose another controller button.");
            var held = InputLabel(selectedHeldInput);
            if (key != 0 && MinimumEffortClicker.Windows.DesktopRuntime.InputMatchesKey(selectedHeldInput, key))
                throw new ArgumentException("Choose a shortcut different from the input being held.");
            string name = ToggleNameBox.Text.Trim(); if (name.Length == 0) name = "Toggle " + held;
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
            RememberActiveToggles();
            RefreshToggleList(); SaveToggleButton.ToolTip = "Save changes"; DeleteToggleButton.IsEnabled = true;
            ReasonLabel.Text = enabledToggles.Contains(id)
                ? $"Saved. Press {ShortcutLabel(binding)} in your Click target to {(macro.Plan!.Mode == HelperMode.Repeat ? "spam" : "hold")} {held}, then again to stop."
                : "Saved disabled. " + ReasonLabel.Text;
        }
        catch (Exception ex) { ReasonLabel.Text = "Could not save toggle: " + ex.Message; }
        finally { Pages.IsEnabled = true; RefreshToggleList(); }
    }
    private async void ToggleEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized || refreshingToggles || operations.Any(Operation.Binding | Operation.Library) || sender is not System.Windows.Controls.Primitives.ToggleButton { DataContext: ToggleRow row } box) return;
        if (box.IsChecked == true) enabledToggles.Add(row.Id); else enabledToggles.Remove(row.Id);
        await ApplyAvailability();
        RememberActiveToggles();
    }
    private async void DeleteToggle_Click(object sender, RoutedEventArgs e)
    {
        if (editingToggle is not { } id || runtime is null || operations.Any(Operation.Binding | Operation.Library)) return;
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
            RememberActiveToggles();
            NewToggle_Click(sender, e); RefreshToggleList(); ReasonLabel.Text = "Toggle removed.";
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
        finally { Pages.IsEnabled = true; }
    }
    // The armed toggles' controller buttons, read on the controller thread; replaced whole, never changed in place.
    private volatile IReadOnlyDictionary<ControllerButton, Guid> armedToggleButtons = new Dictionary<ControllerButton, Guid>();
    private ControllerButton? selectedToggleController;

    /// <summary>A toggle's shortcut in words: its key or mouse button, its controller button, or both ("F1 / LB").</summary>
    private static string ShortcutLabel(MacroBinding binding) => string.Join(" / ",
        new[] { binding.HasKey ? MacroBindings.KeyLabel(binding.Key) : null, binding.Controller is { } pad ? ControllerButtons.Label(pad) : null }.OfType<string>());
    private void ShowToggleController() => ToggleControllerBox.Content = selectedToggleController is { } pad ? UiArt.ControllerGlyph(pad, 22) : "Controller";
    private void ClearToggleController_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { selectedToggleController = null; ShowToggleController(); e.Handled = true; }
    private void CaptureToggleController_Click(object sender, RoutedEventArgs e)
    {
        if (operations.Any(Operation.Binding | Operation.Library)) return;
        var button = CaptureControllerButton("Controller button for this toggle",
            pressed => ControllerButtonUse(pressed, exceptToggle: editingToggle) is { } use ? use + " Press another button." : null, selectedToggleController);
        if (button is not { } chosen) return;
        selectedToggleController = chosen; ShowToggleController();
    }
    /// <summary>The saved toggle switched by this controller button (other than one), if any.</summary>
    private MacroDefinition? ToggleUsing(ControllerButton button, Guid? except = null) =>
        SavedToggles.FirstOrDefault(m => m.Id != except && macroBindings.TryGetValue(m.Id, out var b) && b.Controller == button);

    /// <summary>Saves which toggles are active now, so the next start arms the same ones.</summary>
    private void RememberActiveToggles() { rememberedToggles = [.. enabledToggles]; PersistPreferences(); }

    /// <summary>
    /// At startup: arms the toggles that were active when the app closed. Arming only listens for their shortcuts; nothing
    /// is held or pressed until the user taps one, so the app still starts stopped.
    /// </summary>
    private async Task RestoreActiveToggles()
    {
        var saved = SavedToggles.Select(m => m.Id).Where(macroBindings.ContainsKey).ToHashSet();
        foreach (var id in rememberedToggles.Where(saved.Contains)) enabledToggles.Add(id);
        if (enabledToggles.Count > 0) await ApplyAvailability();
    }
    /// <summary>Arm the checked toggles' shortcuts in the runtime. Arming never starts anything; a shortcut press does.</summary>
    private async Task ApplyAvailability()
    {
        if (runtime is null || operations.Has(Operation.Binding)) return;
        operations.Begin(Operation.Binding); TogglePage.IsEnabled = false;
        try
        {
            var helpers = SavedToggles.Where(m => enabledToggles.Contains(m.Id)).Select(m => new ArmedHelper(macroBindings.GetValueOrDefault(m.Id)
                ?? throw new ArgumentException($"Assign a shortcut for '{m.Name}' first."), m.Plan!, m.Name)).ToArray();
            MacroBindings.Validate(helpers.Select(h => h.Binding).ToArray(), appliedToggleKey, appliedStopKey);
            if (!await runtime.ConfigureHelpersAsync(helpers)) throw new InvalidOperationException("A toggle shortcut is already in use. Choose another key.");
            armedToggleButtons = helpers.Where(h => h.Binding.Controller is not null).ToDictionary(h => h.Binding.Controller!.Value, h => h.Binding.MacroId);
        }
        catch (Exception ex)
        {
            enabledToggles.Clear(); armedToggleButtons = new Dictionary<ControllerButton, Guid>();
            await runtime.ConfigureHelpersAsync([]);
            ReasonLabel.Text = "Toggles disabled: " + ex.Message;
        }
        finally { operations.End(Operation.Binding); TogglePage.IsEnabled = true; RefreshToggleList(); }
    }
    private OverlayToggle[] ToggleOverlayLines(RuntimeSnapshot snapshot)
    {
        var ids = snapshot.ActiveHelperIds ?? [];
        return SavedToggles.Where(m => enabledToggles.Contains(m.Id)).Select(m =>
            new OverlayToggle(ToggleName(m), macroBindings.TryGetValue(m.Id, out var binding) ? ShortcutLabel(binding) : "Unassigned",
                OverlayToggle.StateFor(snapshot.CleanupPending, ids.Contains(m.Id), snapshot.PlaybackActive, snapshot.State), ids.Contains(m.Id))).ToArray();
    }
}
