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
    private async void DeleteLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (runtime is null || runtime.Snapshot.Busy || runtime.Snapshot.CleanupPending || ioBusy || bindingBusy)
        { ReasonLabel.Text = "Stop and finish cleanup or saving before deleting the library."; return; }
        if (AppDialog.Show(this, "Permanently delete every local profile, helper and recorded sequence, including their recovery backups?\n\nThis cannot be undone. Saved .mecprofile files and appearance preferences are kept.",
            "Delete local library", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        ioBusy = true; Pages.IsEnabled = false;
        try
        {
            runtime.ArmedPlan = null;
            enabledProfiles.Clear(); enabledMacros.Clear(); enabledToggles.Clear(); RefreshToggleList();
            if (!await runtime.ConfigureHelpersAsync([])) throw new InvalidOperationException("Profile bindings could not be disabled. Nothing was deleted.");
            await Task.Run(() => { bindingsStore.Save([]); store.DeleteAllProfileData(); });
            macroBindings.Clear(); profiles = []; RefreshLibraryLists(); MacroList.ItemsSource = null;
            ProfileSelection_Changed(this, null!);
            AvailabilityLabel.Text = "No profile bindings are enabled.";
            ReasonLabel.Text = "Local profiles, recordings and their recovery copies were deleted. Playback remains stopped.";
        }
        catch (Exception ex)
        {
            var remaining = await Task.Run(store.Load); profiles = remaining.Profiles.ToList(); RefreshLibraryLists();
            ReasonLabel.Text = "Deletion did not complete. Remaining data is disabled; check the data folder. " + ex.Message;
        }
        finally { ioBusy = false; Pages.IsEnabled = true; }
    }
    private static string DescribeMacro(MacroDefinition macro) => macro.Plan is { } plan ? Describe(plan)
        : $"Sequence · {macro.Sequence!.Steps.Count} editable actions";
    private void ProfileSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MacroList is null) return;
        selectingProfile = true;
        var profile = ProfileList.SelectedItem as ProfileDefinition;
        ProfileEnabledBox.IsChecked = profile is not null && enabledProfiles.Contains(profile.Id);
        ProfileEnabledBox.IsEnabled = profile is not null;
        if (profile is not null) ProfileNameBox.Text = profile.Name;
        MacroList.ItemsSource = profile?.Macros.Where(m => !m.IsToggle).ToArray();
        MacroList.SelectedIndex = profile is null ? -1 : 0;
        selectingProfile = false;
        MacroSelection_Changed(sender, e);
    }

    private void MacroSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MacroNameBox is null) return;
        bool previous = selectingProfile; selectingProfile = true;
        var macro = MacroList.SelectedItem as MacroDefinition;
        MacroNameBox.Text = macro?.Name ?? "";
        MacroSummary.Text = macro is null ? "Select a folder and macro to configure it." : DescribeMacro(macro);
        MacroEnabledBox.IsChecked = macro is not null && enabledMacros.Contains(macro.Id);
        MacroEnabledBox.IsEnabled = macro is not null;
        var binding = macro is null ? null : macroBindings.GetValueOrDefault(macro.Id);
        selectedMacroKey = binding?.Key ?? 0; MacroKeyBox.Content = ShortcutKeys.Label(selectedMacroKey);
        MacroHoldBox.IsChecked = binding?.HoldTrigger ?? false;
        selectingProfile = previous;
    }

    private async void Availability_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized || selectingProfile || bindingBusy) return;
        if (ProfileList.SelectedItem is not ProfileDefinition profile) return;
        if (ProfileEnabledBox.IsChecked == true) enabledProfiles.Add(profile.Id); else enabledProfiles.Remove(profile.Id);
        if (MacroList.SelectedItem is MacroDefinition macro)
        {
            if (MacroEnabledBox.IsChecked == true) enabledMacros.Add(macro.Id); else enabledMacros.Remove(macro.Id);
        }
        await ApplyAvailability();
    }

    private async Task ApplyAvailability()
    {
        if (runtime is null || bindingBusy) return;
        bindingBusy = true; ProfilesPage.IsEnabled = false; TogglePage.IsEnabled = false;
        try
        {
            var macros = profiles.SelectMany(p => p.Macros.Select(m => (Profile: p, Macro: m)))
                .Where(x => x.Macro.IsToggle ? enabledToggles.Contains(x.Macro.Id) : enabledProfiles.Contains(x.Profile.Id) && enabledMacros.Contains(x.Macro.Id)).ToArray();
            var helpers = macros.Select(x => new ArmedHelper(macroBindings.GetValueOrDefault(x.Macro.Id)
                ?? throw new ArgumentException($"Assign and apply a binding for '{x.Macro.Name}' first."), x.Macro.Plan,
                x.Macro.Sequence is null ? null : SequenceCompiler.Compile(x.Macro.Sequence, x.Profile.Points), x.Macro.IsToggle ? x.Macro.Name : x.Profile.Name + " · " + x.Macro.Name, x.Macro.IsToggle)).ToArray();
            MacroBindings.Validate(helpers.Select(h => h.Binding).ToArray(), appliedToggleKey, appliedStopKey);
            if (!await runtime.ConfigureHelpersAsync(helpers)) throw new InvalidOperationException("A macro hotkey is already in use. Choose another key.");
            AvailabilityLabel.Text = helpers.Length == 0 ? "No saved macro hotkeys are enabled." : $"{helpers.Length} macro hotkeys ready. Nothing starts until you press one.";
        }
        catch (Exception ex)
        {
            enabledProfiles.Clear(); enabledMacros.Clear(); enabledToggles.Clear(); RefreshToggleList();
            await runtime.ConfigureHelpersAsync([]);
            selectingProfile = true; ProfileEnabledBox.IsChecked = MacroEnabledBox.IsChecked = false; selectingProfile = false;
            AvailabilityLabel.Text = "Bindings disabled: " + ex.Message;
            ReasonLabel.Text = AvailabilityLabel.Text;
        }
        finally { bindingBusy = false; ProfilesPage.IsEnabled = true; TogglePage.IsEnabled = true; RefreshToggleList(); }
    }

    private async Task ReplaceProfile(ProfileDefinition updated, Guid selectedMacro)
    {
        await SaveProfiles(profiles.Select(p => p.Id == updated.Id ? updated : p).ToList());
        ProfileList.SelectedItem = updated;
        MacroList.SelectedItem = updated.Macros.FirstOrDefault(m => m.Id == selectedMacro);
        await ApplyAvailability();
    }

    private async void ApplyMacro_Click(object sender, RoutedEventArgs e)
    {
        if (bindingBusy || ioBusy || ProfileList.SelectedItem is not ProfileDefinition profile || MacroList.SelectedItem is not MacroDefinition macro) return;
        try
        {
            var updated = profile with { Name = (ProfileNameBox.Text ?? "").Trim(), Macros = profile.Macros.Select(m => m.Id == macro.Id ? m with { Name = (MacroNameBox.Text ?? "").Trim() } : m).ToArray() };
            var errors = updated.Validate();
            if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors));
            if (selectedMacroKey == 0) { macroBindings.Remove(macro.Id); enabledMacros.Remove(macro.Id); }
            else macroBindings[macro.Id] = new(macro.Id, selectedMacroKey, MacroHoldBox.IsChecked == true);
            await ApplyAvailability(); // Revoke the old runtime binding before any fallible disk operation.
            var savedBindings = macroBindings.Values.ToArray();
            ioBusy = true;
            try { await Task.Run(() => bindingsStore.Save(savedBindings)); } finally { ioBusy = false; }
            await ReplaceProfile(updated, macro.Id);
            ReasonLabel.Text = "Names and hotkey saved. Imported and restored macro folders remain disabled.";
        }
        catch (Exception ex) { ReasonLabel.Text = "Could not apply: " + ex.Message; }
    }

    private async void AddQuickMacro_Click(object sender, RoutedEventArgs e)
    {
        if (ioBusy || bindingBusy || ProfileList.SelectedItem is not ProfileDefinition profile) return;
        if (!TryQuickSequence(out var definition, out _, out var error)) { ReasonLabel.Text = error; return; }
        try
        {
            var macro = new MacroDefinition(Guid.NewGuid(), QuickSummary(definition!), null, Sequence: definition);
            await ReplaceProfile(profile with { Macros = [.. profile.Macros, macro] }, macro.Id);
            ReasonLabel.Text = "Quick helper added as a disabled macro.";
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
    }

    private async void UpdateQuickMacro_Click(object sender, RoutedEventArgs e)
    {
        if (ioBusy || bindingBusy || ProfileList.SelectedItem is not ProfileDefinition profile || MacroList.SelectedItem is not MacroDefinition macro) return;
        if (!TryQuickSequence(out var definition, out _, out var error)) { ReasonLabel.Text = error; return; }
        if (AppDialog.Show(this, $"Replace the action and timing of '{macro.Name}' with the current action list?", "Update macro", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        try
        {
            enabledMacros.Remove(macro.Id);
            await ApplyAvailability();
            await ReplaceProfile(profile with { Macros = profile.Macros.Select(m => m.Id == macro.Id ? m with { Plan = null, Sequence = definition, IsToggle = false } : m).ToArray() }, macro.Id);
            ReasonLabel.Text = "Macro updated and disabled for review.";
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
    }

    private async void DuplicateMacro_Click(object sender, RoutedEventArgs e)
    {
        if (ioBusy || bindingBusy || ProfileList.SelectedItem is not ProfileDefinition profile || MacroList.SelectedItem is not MacroDefinition macro) return;
        try
        {
            var duplicate = macro with { Id = Guid.NewGuid(), Name = macro.Name.Length <= 93 ? macro.Name + " (copy)" : macro.Name[..93] + " (copy)" };
            await ReplaceProfile(profile with { Macros = [.. profile.Macros, duplicate] }, duplicate.Id);
            ReasonLabel.Text = "Duplicate created without a binding or enablement.";
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
    }

    private async void DeleteMacro_Click(object sender, RoutedEventArgs e)
    {
        if (ioBusy || bindingBusy || ProfileList.SelectedItem is not ProfileDefinition profile || MacroList.SelectedItem is not MacroDefinition macro) return;
        if (profile.Macros.Count == 1) { ReasonLabel.Text = "This is the last macro. Delete the folder to remove it."; return; }
        if (AppDialog.Show(this, $"Delete '{macro.Name}'?", "Delete macro", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            enabledMacros.Remove(macro.Id); macroBindings.Remove(macro.Id);
            await ApplyAvailability();
            await ReplaceProfile(profile with { Macros = profile.Macros.Where(m => m.Id != macro.Id).ToArray() }, Guid.Empty);
            ReasonLabel.Text = "Macro deleted.";
        }
        catch (Exception ex) { ReasonLabel.Text = ex.Message; }
    }
}
