using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private string MacroDirectory => Path.Combine(store.DirectoryPath, "Macros");
    private string? loadedMacroFile;
    private ProfileDefinition? loadedFileProfile;

    private async Task MakeSavedMacrosAvailable()
    {
        try
        {
            await Task.Run(() => MacroFiles.EnsureLibraryFiles(MacroDirectory, profiles));
        }
        catch (Exception ex) { MacroSaveStatus.Text = "Some older macros could not be copied to files. Originals are kept. " + ex.Message; }
    }
    private async void LoadMacroFile_Click(object sender, RoutedEventArgs e)
    {
        if (runtime?.Snapshot.Busy == true || !operations.Idle) { ReasonLabel.Text = "Stop before loading a macro."; return; }
        operations.Begin(Operation.MacroFile); RefreshStatus();
        try
        {
            Directory.CreateDirectory(MacroDirectory);
            var dialog = new OpenFileDialog { Title = "Load Macro", Filter = "Minimum Effort macros (*.mecprofile)|*.mecprofile", InitialDirectory = MacroDirectory, Multiselect = false };
            if (dialog.ShowDialog(this) != true) return;
            var (profile, dropped) = await Task.Run(() => { var opened = HelperProfiles.Open(dialog.FileName, out int removed); return (opened, removed); });
            if (closing || allowClose) return;
            var choices = profile.Macros.Where(m => !m.IsToggle).ToArray();
            if (choices.Length == 0) throw new ArgumentException("This file contains only toggles. Use the Toggles tab to set up a hold.");
            var macro = choices.Length == 1 ? choices[0] : SelectFileMacro(choices);
            if (macro is null) return;
            LoadActionMacro(profile, macro);
            loadedMacroFile = dialog.FileName; loadedFileProfile = profile;
            MacroSaveStatus.Text = $"Loaded ‘{macro.Name}’." + (HelperProfiles.DroppedWarning(dropped) is { } warning ? " " + warning : "");
            if (WorkshopMacros.IsWorkshopCopy(MacroDirectory, dialog.FileName))
            {
                // Steam replaces this copy when its author updates it, so Save Macro makes the user's own file instead.
                loadedMacroFile = null; loadedFileProfile = null; loadedQuickMacro = null;
                AppDialog.Show(this, $"‘{macro.Name}’ comes from the Steam Workshop and was made by someone else.\n\nRead every action in the list before you press Start. Save Macro keeps your own copy in the Macros folder.", "Workshop macro");
            }
        }
        catch (Exception ex) { MacroSaveStatus.Text = "Could not load: " + ex.Message; }
        finally { operations.End(Operation.MacroFile); loadingQuick = false; RefreshStatus(); }
    }
    private MacroDefinition? SelectFileMacro(MacroDefinition[] choices)
    {
        var window = new Window { Owner = this, Title = "Choose a macro from this file", Width = 400, Height = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(BackgroundProperty, "AppBackground"); window.SetResourceReference(ForegroundProperty, "TextPrimary");
        var panel = new DockPanel { Margin = new Thickness(16) };
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom);
        var load = new Button { Content = "Load Macro", IsDefault = true }; var cancel = new Button { Content = "Cancel", IsCancel = true };
        buttons.Children.Add(load); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        var list = new ListBox { ItemsSource = choices, SelectedIndex = 0 }; panel.Children.Add(list);
        load.Click += (_, _) => window.DialogResult = true; list.MouseDoubleClick += (_, _) => window.DialogResult = true;
        window.Content = panel;
        return window.ShowDialog() == true ? list.SelectedItem as MacroDefinition : null;
    }
}
