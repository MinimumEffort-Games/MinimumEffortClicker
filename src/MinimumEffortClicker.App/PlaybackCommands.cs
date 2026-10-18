using System.Windows;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private async void PausePlayback_Click(object sender, RoutedEventArgs e)
    {
        if (operations.Has(Operation.Quick) || runtime is null) return;
        if (runtime.CanResume) await ResumeActionsAsync(); else runtime.Pause();
    }

    // The Pause key (F7 by default) pauses a running Action List and goes on with a paused one, like the Pause button;
    // F8 stays Start / Stop (User, 2026-10-18).
    private int appliedPauseKey = ShortcutKeys.DefaultPauseKey, selectedPauseKey = ShortcutKeys.DefaultPauseKey;
    private string PauseHint => controllerHints && controllerControls.Pause is { } pad ? ControllerButtons.Label(pad) : ShortcutKeys.Label(appliedPauseKey);
    private void PauseHotkey()
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() =>
        {
            if (runtime is { } live && (live.CanResume || live.Snapshot.PlaybackActive)) PausePlayback_Click(this, new RoutedEventArgs());
        });
    }
    private async void CapturePauseKey_Click(object sender, RoutedEventArgs e)
    { if (await CaptureShortcut("Pause / Unpause shortcut") is { } key) { selectedPauseKey = key; await ConfigurePauseHotkey(); } }
    /// <summary>Registers the chosen Pause key; a key that is taken or unavailable keeps the previous one and says why.</summary>
    private async Task ConfigurePauseHotkey()
    {
        if (runtime is null) return;
        if (ShortcutKeys.IsMouse(selectedPauseKey)) { selectedPauseKey = appliedPauseKey; ReasonLabel.Text = "Pause needs a keyboard key."; }
        bool success = await runtime.Control.ConfigurePauseAsync(selectedPauseKey);
        if (success) appliedPauseKey = selectedPauseKey;
        else
        {
            ReasonLabel.Text = "That key is already used or unavailable. The previous Pause key was kept.";
            selectedPauseKey = appliedPauseKey;
            await runtime.Control.ConfigurePauseAsync(appliedPauseKey);
        }
        PauseKeyBox.Content = ShortcutKeys.Label(appliedPauseKey);
        PersistPreferences(); RefreshStatus();
    }
    private async Task ResumeActionsAsync()
    {
        if (runtime is null) return;
        StepAside? away = null;
        bool accepted = false;
        using var stop = new CancellationTokenSource();
        long intent = runtime.StopVersion;
        operations.Begin(Operation.Quick); quickCancellation = stop;
        try
        {
            away = StepAsideForTarget(MinimizeOnStartBox.IsChecked == true);
            if (!runtime.BringTargetForward(resume: true)) return;
            RefreshStatus();
            // Let the initiating press release and foreground handoff settle, with no countdown.
            await Task.Delay(200, stop.Token);
            if (intent != runtime.StopVersion || closing || allowClose) return;
            accepted = runtime.TryResume();
            if (accepted && playbackAway is null) { playbackAway = away; playbackSeenActive = false; }
        }
        catch (OperationCanceledException) { }
        finally
        {
            operations.End(Operation.Quick); quickCancellation = null;
            if (!accepted) ComeBack(away);
            RefreshStatus();
        }
    }
}
