using System.IO;
using System.Windows;
using System.Windows.Threading;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;

namespace MinimumEffortClicker.App;

/// <summary>
/// Steam build only: Share on Workshop, Browse Workshop, and keeping Macros\Workshop in line with the user's
/// subscriptions. Every other build has no Workshop client and none of this shows.
/// </summary>
public partial class MainWindow
{
    private IWorkshopClient? workshop;
    private readonly DispatcherTimer workshopPump = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool workshopSyncing, workshopSyncAgain;
    private readonly HashSet<string> refusalsShown = [];
    private WorkshopLedger Ledger => new(store.DirectoryPath);
    private string WorkshopWorkFolder => Path.Combine(Path.GetTempPath(), "MinimumEffortClicker-workshop");

    private void InitializeWorkshop()
    {
        workshop = SteamMode.StartWorkshop();
        if (workshop is null) return;
        WorkshopButtons.Visibility = Visibility.Visible;
        if (!workshop.Available)
        {
            ShareWorkshopButton.IsEnabled = BrowseWorkshopButton.IsEnabled = false;
            ShareWorkshopButton.ToolTip = BrowseWorkshopButton.ToolTip = workshop.UnavailableReason;
            AppLog.Note("Steam Workshop", "unavailable: " + workshop.UnavailableReason);
            return;
        }
        workshop.ItemsChanged += SyncWorkshopMacros;
        workshopPump.Tick += (_, _) => workshop.Pump();
        workshopPump.Start();
    }

    private void CloseWorkshop()
    {
        workshopPump.Stop();
        workshop?.Dispose(); workshop = null;
    }

    /// <summary>Copies subscribed macros into Macros\Workshop. Bursts of Steam notices run one pass, then one more.</summary>
    private async void SyncWorkshopMacros()
    {
        if (workshop is not { Available: true } || closing || allowClose) return;
        if (workshopSyncing) { workshopSyncAgain = true; return; }
        workshopSyncing = true;
        try
        {
            do
            {
                workshopSyncAgain = false;
                var items = workshop.InstalledItems();
                var ledger = Ledger;
                var result = await Task.Run(() => WorkshopMacros.Sync(items, MacroDirectory, ledger));
                if (result.Changed)
                    MacroSaveStatus.Text = $"Workshop macros: {result.Added} new, {result.Updated} updated, {result.Removed} removed. Open them with Load Macro, in the Workshop folder.";
                if (result.Skipped > 0) AppLog.Note("Workshop sync", $"{result.Skipped} subscribed item(s) hold no macro this version can open.");
                ReportRefusals(result.Refused);
            }
            while (workshopSyncAgain && workshop is { Available: true } && !closing);
        }
        catch (Exception ex) { MacroSaveStatus.Text = "Workshop macros could not be copied: " + ex.Message; AppLog.Failure("Workshop sync", ex); }
        finally { workshopSyncing = false; }
    }

    /// <summary>Says once per session which subscribed macros were left out and why; the log keeps every pass.</summary>
    private void ReportRefusals(IReadOnlyList<string> refused)
    {
        foreach (var line in refused) AppLog.Note("Workshop sync", "refused " + line);
        var fresh = refused.Where(refusalsShown.Add).ToList();
        if (fresh.Count == 0) return;
        MacroSaveStatus.Text = $"{fresh.Count} Workshop macro(s) refused: they press system shortcuts.";
        AppDialog.Show(this, "These subscribed macros were not added to your Workshop folder:\n\n" + string.Join("\n", fresh.Select(line => "- " + line))
            + "\n\nThese shortcuts can open a command line, Task Manager or a run box, close windows or shut the PC down. The clicker never runs them from a Workshop macro. Unsubscribe in Steam to stop this notice.", "Workshop macro refused");
    }

    /// <summary>
    /// Keys from a Workshop action are sent only with a window as Click target: then they reach that window alone, and the
    /// run pauses when anything else comes to the front. With Desktop as target they would go to whatever is in front.
    /// </summary>
    private bool WorkshopKeysBlocked(SequenceDefinition sequence)
    {
        if (runtime?.IsDesktopMode != true || !WorkshopMacros.HasWorkshopKeys(sequence, Ledger.OriginSteps())) return false;
        ReasonLabel.Text = "This list has key presses from a Workshop macro. Select the window it is for as Click target first; its keys then only reach that window.";
        return true;
    }

    /// <summary>A copy of a Workshop action is still a Workshop action.</summary>
    private void MarkWorkshopCopies(IReadOnlyList<SequenceStep> originals, IReadOnlyList<SequenceStep> copies)
    {
        var origins = Ledger.OriginSteps();
        var marked = originals.Zip(copies).Where(pair => origins.Contains(pair.First.Id)).Select(pair => pair.Second.Id).ToList();
        if (marked.Count > 0) Ledger.AddOriginSteps(marked);
    }

    private void BrowseWorkshop_Click(object sender, RoutedEventArgs e)
    {
        try { workshop?.OpenWorkshop(); }
        catch (Exception ex) { MacroSaveStatus.Text = "Steam could not open the Workshop: " + ex.Message; }
    }

    private async void ShareWorkshop_Click(object sender, RoutedEventArgs e)
    {
        if (workshop is not { Available: true }) { MacroSaveStatus.Text = workshop?.UnavailableReason ?? "The Workshop is only in the Steam version."; return; }
        // Sharing always sends what is in the list now, so it saves first.
        if (!await SaveCurrentMacroAsync() || loadedMacroFile is not { } file || loadedQuickMacro is not { } macroId) return;
        var ledger = Ledger;
        var previous = ledger.Find(macroId);
        bool holdsImages = loadedFileProfile?.Macros.FirstOrDefault(m => m.Id == macroId)?.Sequence?.Steps
            .Any(s => s.Condition?.Template is not null) == true;
        var dialog = new WorkshopShareWindow(ActionMacroNameBox.Text.Trim(), previous, holdsImages) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!operations.Idle || runtime?.Snapshot.Busy == true) { MacroSaveStatus.Text = "Stop before sharing the macro."; return; }
        operations.Begin(Operation.MacroFile); RefreshStatus();
        string? logo = null;
        try
        {
            var progress = new Progress<double>(part => MacroSaveStatus.Text = $"Uploading to the Workshop… {part:P0}");
            MacroSaveStatus.Text = "Uploading to the Workshop…";
            var result = await new WorkshopPublisher(workshop, ledger, WorkshopWorkFolder).ShareAsync(macroId, file, dialog.ShareTitle, dialog.ShareDescription,
                dialog.ShareVisibility, dialog.PreviewImage, () => logo ??= WriteLogo(), progress);
            if (!result.Success) { MacroSaveStatus.Text = result.Message; AppLog.Note("Workshop share", "failed: " + result.Message); return; }
            MacroSaveStatus.Text = result.Message;
            AppLog.Note("Workshop share", $"macro {macroId} is item {result.ItemId}.");
            if (result.NeedsLegalAgreement || AppDialog.Show(this, result.Message + "\n\nOpen its Workshop page in Steam?", "Shared on the Workshop", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                workshop.OpenItem(result.ItemId);
        }
        catch (Exception ex) { MacroSaveStatus.Text = "Could not share: " + ex.Message; AppLog.Failure("Workshop share", ex); }
        finally
        {
            operations.End(Operation.MacroFile); RefreshStatus();
            if (logo is not null)
                try { File.Delete(logo); } catch (IOException) { /* A leftover temporary copy is harmless; Windows clears the temp folder. */ }
        }
    }

    /// <summary>The app logo as a file, because Steam takes the preview image from disk.</summary>
    private string WriteLogo()
    {
        Directory.CreateDirectory(WorkshopWorkFolder);
        string path = Path.Combine(WorkshopWorkFolder, "preview-" + Guid.NewGuid().ToString("N") + ".png");
        using var source = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Logo.png")).Stream;
        using var target = File.Create(path);
        source.CopyTo(target);
        return path;
    }
}
