using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;
using System.IO;
using Avalonia.Platform.Storage;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private string MacroDirectory => Path.Combine(store.DirectoryPath, "Macros");
    private string? loadedMacroFile;
    private ProfileDefinition? loadedFileProfile;
    private bool macroFileBusy;

    private async Task MakeSavedMacrosAvailable()
    {
        try
        {
            await Task.Run(() => MacroFiles.EnsureLibraryFiles(MacroDirectory, profiles));
        }
        catch (Exception ex) { MacroSaveStatus.Text = "Some older macros could not be copied to files. Originals are kept. " + ex.Message; }
    }
    private async void LoadMacroFile_Click(object? sender, RoutedEventArgs e)
    {
        if (runtime?.Snapshot.Busy == true || quickOperation || ioBusy || macroFileBusy) { ReasonLabel.Text = "Stop before loading a macro."; return; }
        macroFileBusy = true; RefreshStatus();
        try
        {
            Directory.CreateDirectory(MacroDirectory);
            var folder = await StorageProvider.TryGetFolderFromPathAsync(MacroDirectory);
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Load Macro", AllowMultiple = false, SuggestedStartLocation = folder,
                FileTypeFilter = [new("Minimum Effort macros") { Patterns = ["*.mecprofile"] }] });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } fileName) return;
            var profile = await Task.Run(() => HelperProfiles.Open(fileName));
            if (closing || allowClose) return;
            var choices = profile.Macros.Where(m => !m.IsToggle).ToArray();
            if (choices.Length == 0) throw new ArgumentException("This file contains only toggles. Use the Toggles tab to set up a hold.");
            var macro = choices.Length == 1 ? choices[0] : SelectFileMacro(choices);
            if (macro is null) return;
            LoadActionMacro(profile, macro);
            loadedMacroFile = fileName; loadedFileProfile = profile;
            MacroSaveStatus.Text = $"Loaded ‘{macro.Name}’.";
        }
        catch (Exception ex) { MacroSaveStatus.Text = "Could not load: " + ex.Message; }
        finally { macroFileBusy = false; loadingQuick = false; RefreshStatus(); }
    }
    private MacroDefinition? SelectFileMacro(MacroDefinition[] choices)
    {
        var window = new DialogWindow { OwnerWindow = this, Title = "Choose a macro from this file", Width = 400, Height = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom);
        var load = new Button { Content = "Load Macro", IsDefault = true }; var cancel = window.CancelButton();
        buttons.Children.Add(load); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        var list = new ListBox { ItemsSource = choices, SelectedIndex = 0, DisplayMemberBinding = new Avalonia.Data.Binding(nameof(MacroDefinition.Name)) }; panel.Children.Add(list);
        load.Click += (_, _) => window.DialogResult = true; list.DoubleTapped += (_, _) => window.DialogResult = true;
        window.Content = panel;
        return window.ShowDialog() == true ? list.SelectedItem as MacroDefinition : null;
    }
}
