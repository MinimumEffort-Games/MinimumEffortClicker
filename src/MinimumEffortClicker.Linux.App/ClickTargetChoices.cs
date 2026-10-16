using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private static readonly TargetWindow DesktopChoice = new(0, 0, "Desktop");
    private bool syncingTargets;

    private void ClickTargets_Opened(object? sender, EventArgs e) => RefreshTargetChoices();

    private void RefreshTargetChoices()
    {
        try
        {
            var windows = runtime?.ListTargets() ?? [];
            TargetWindow[] choices = [DesktopChoice, .. windows];
            var selected = runtime?.SelectedWindow;
            var choice = runtime?.IsDesktopMode != false ? DesktopChoice
                : selected is null ? null : choices.FirstOrDefault(w => w.Handle == selected.Handle && w.ProcessId == selected.ProcessId);
            // Preserve a closed target label in a non-editable dropdown; never fall back to Desktop implicitly.
            if (choice is null)
            {
                choice = selected is null ? new TargetWindow(0, 0, "Choose a window") : selected with { Title = selected.Title + " (unavailable)" };
                choices = [.. choices, choice];
            }
            // A closed target remains unavailable until the user explicitly chooses another entry.
            string title = choice?.Title ?? (selected is null ? "Choose a window" : selected.Title + " (unavailable)");
            syncingTargets = true;
            foreach (var box in new[] { TargetLabel, ToggleTargetLabel })
            {
                box.ItemsSource = choices;
                box.SelectedItem = choice;
                box.Text = title;
            }
        }
        catch (Exception ex) { ReasonLabel.Text = "Could not refresh windows: " + ex.Message; }
        finally { syncingTargets = false; }
    }

    private void ClickTarget_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncingTargets || runtime is null || sender is not ComboBox { SelectedItem: TargetWindow selected }) return;
        var previous = runtime.SelectedWindow;
        if (selected.Handle == 0 && runtime.IsDesktopMode
            || selected.Handle != 0 && runtime.HasSelectedTarget && previous is not null && selected.Handle == previous.Handle && selected.ProcessId == previous.ProcessId) return;
        try
        {
            if (selected.Handle == 0) runtime.UseDesktop(); else runtime.SelectTarget(selected);
            quickPoint = null;
            useCursorLocation = true;
            ShowQuickCoordinates();
            // Waits whose area was picked for the other kind of target turn red until their area is selected again.
            RefreshQuickList(QuickActionList.SelectedIndex, preserveEditor: true);
            ShowConditionColour();
        }
        catch (Exception ex) { ReasonLabel.Text = "Could not select window: " + ex.Message; }
        finally { RefreshTargetChoices(); RefreshStatus(); }
    }
}
