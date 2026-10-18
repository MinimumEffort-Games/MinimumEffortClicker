using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Core;
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

    // The Click target chosen last; saved with the preferences and picked again at the next start.
    private RememberedTarget? lastTarget;
    private static string ProgramOf(int processId)
    {
        try { using var process = System.Diagnostics.Process.GetProcessById(processId); return process.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return ""; }
    }
    /// <summary>
    /// At start: selects the Click target used last when its window is open (User, 2026-10-18). Nothing runs; a missing
    /// window leaves Desktop, as before.
    /// </summary>
    private void RestoreLastTarget()
    {
        if (runtime is null || lastTarget is not { } remembered) return;
        string? restored = null;
        try
        {
            var windows = runtime.ListTargets();
            int pick = remembered.Pick(windows.Select(w => (ProgramOf(w.ProcessId), w.Title)).ToArray());
            if (pick < 0) return;
            runtime.SelectTarget(windows[pick]);
            restored = windows[pick].Title;
        }
        // The window closed meanwhile, or the desktop helper did not answer: Desktop stays, as without a remembered target.
        catch (Exception ex) when (ex is InvalidOperationException or HelperException or ArgumentException) { }
        finally { RefreshTargetChoices(); RefreshStatus(); }
        // After RefreshStatus, which shows the runtime's own "Click target selected" line once and would replace this.
        if (restored is not null) ReasonLabel.Text = $"Click target from last time selected: {restored}.";
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
            lastTarget = selected.Handle != 0 && ProgramOf(selected.ProcessId) is { Length: > 0 } program ? new(program, selected.Title) : null;
            // A new target gets the overlay back on the anchor chosen in Settings (User, 2026-10-18).
            SetOverlayAnchor(overlayOptions.AnchorX, overlayOptions.AnchorY);
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
