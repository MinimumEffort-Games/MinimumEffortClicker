using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private readonly Queue<string> diagnosticStates = new();
    private string? lastDiagnosticState;
    private void RecordDiagnosticState(RuntimeSnapshot snapshot)
    {
        if (snapshot.State == lastDiagnosticState) return;
        lastDiagnosticState = snapshot.State;
        if (diagnosticStates.Count == 64) diagnosticStates.Dequeue();
        // Only application-defined states and counts: no keys, names, titles, paths or images.
        diagnosticStates.Enqueue($"{DateTimeOffset.UtcNow:O}  {snapshot.State}; inputs={snapshot.InputsSent}; loops={snapshot.CompletedCycles}; cleanup={snapshot.CleanupPending}");
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        using var process = Process.GetCurrentProcess();
        var snapshot = runtime?.Snapshot;
        var report = new StringBuilder();
        report.AppendLine("Minimum Effort Clicker · private personal preview");
        report.AppendLine($"Generated UTC: {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"Build: {typeof(App).Assembly.GetName().Version}; profile schema: {ProfileDefinition.CurrentVersion}");
        report.AppendLine($"Windows: {Environment.OSVersion.Version}; .NET: {Environment.Version}; process: {(Environment.Is64BitProcess ? "x64" : "x86")}");
        report.AppendLine($"Logical processors: {Environment.ProcessorCount}; private MiB: {process.PrivateMemorySize64 / 1048576d:F1}; handles: {process.HandleCount}");
        report.AppendLine($"Data mode: {(File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode")) ? "portable" : "local user")}");
        report.AppendLine($"Controls available: {runtime?.Control.Available}; notification failures: {runtime?.NotificationFailures}");
        report.AppendLine($"State: {snapshot?.State}; cleanup pending: {snapshot?.CleanupPending}; lifetime loops: {snapshot?.TotalCompletedLoops}; active seconds: {snapshot?.TotalActiveTime.TotalSeconds:F1}");
        report.AppendLine($"Shared click cap: {appliedClickLimit}; keep awake during playback: {appliedKeepAwake}; shutdown armed: {shutdownSchedule.Armed}");
        report.AppendLine("Timing policy: skip missed helper intervals; preserve ordered sequence holds and stretch for the click cap.");
        if (runtime is { } activeRuntime)
        {
            var timing = activeRuntime.NativeDispatchTiming;
            report.AppendLine($"Native input dispatch: {timing.Count} calls; {timing.TotalMilliseconds:F1} ms total; {timing.MaximumMilliseconds:F1} ms maximum. Requested CPS is a ceiling, not a guaranteed rate.");
        }
        report.AppendLine("Recent state changes (at most 64; memory only, discarded on exit):");
        foreach (string state in diagnosticStates) report.AppendLine(state);
        report.AppendLine("Private content omitted: target titles, paths, profile/action names, key history, screenshots and templates. Nothing is uploaded.");
        ShowText("Local diagnostics · preview before saving", report.ToString(), save: true);
    }

    private void Help_Click(object sender, RoutedEventArgs e) => ShowText("Minimum Effort · getting started", """
        Start small
        Open TestTarget, refresh the target list and select it. Choose a Quick helper and a finite repeat count. Start brings the selected app forward and counts down (3 seconds by default). F9 is Stop All by default; F8 is the optional enabled start/toggle hotkey. Every launch starts stopped.

        Click speed and power
        Quick’s speed and power options cap mouse clicks across all helpers and sequences at 20 CPS by default (1–100). Lower the cap if the target lags. Keyboard timing is separate. Prevent sleep applies only during playback. Optional shutdown can count completed loops, active playback minutes or the next chosen system time. Recording, idle time and startup countdowns do not consume active playback minutes. A visible 30-second countdown can be cancelled; Stop All cancels the plan.

        Saved macros and folders
        Save a Quick helper, or create an editable sequence. Press F10 in the editor to start or finish recording (the record key can be changed). Recording captures only in the selected foreground target and pauses on focus loss; resume is explicit. Review the captured actions before saving. Edit durations, overlap, waits and named positions, then use Timing view to inspect them. Exports include captured templates, so review them before sharing. Import never enables playback.

        Reusable actions and decisions
        Select consecutive actions and choose Make subsequence, then insert calls from Subsequences. If/repeat chooses reusable bodies from a fresh, stable observation. Else requires a confirmed negative; timeout, ambiguity and unavailable capture never select it. Repeat-until checks before and after the body and stops at its explicit bound. General sequences run exclusively.

        Sequence loops and pace
        Loop settings chooses once, a completed loop count, active seconds or explicit until-stopped playback. Delay between loops is separate from action gaps. Run once and Run selected ignore saved repetition; Run configured loops uses it. Finish this loop completes the current sequence body; Stop All still interrupts immediately. An active duration can end midway through a hold, releasing owned input. Idle-gap scaling changes waits only; use the preview and undo. Edit a reusable body's own actions to change waits inside that body.

        Pause and resume
        Pause or target focus loss releases input and preserves remaining keyboard-hold/wait time. Returning focus never restarts playback. Resume brings the target forward and counts down three seconds; the enabled F8 toggle can resume directly while the target is foreground. Paused time and resume countdowns do not count toward active-time limits, and keep-awake is released. A timed-out condition is retried at that same step. An interrupted click/drag requires reviewing and explicitly restarting the selected action; already delivered effects cannot be undone.

        Text and keyboard modes
        Plain key names use physical positions. Logical key choices resolve against the selected target's keyboard layout when starting, then reject layout changes or overlapping physical aliases. Unicode text is the separate text-entry mode. Named points default to window-client coordinates; Desktop fixed uses physical screen coordinates and does not follow a moved target. Both require the calibrated size/DPI and reject points outside the selected foreground target.

        Add Unicode text is an explicit editor action. Its contents are saved in profiles and exports; review them before sharing. It types through Windows Unicode input without touching the clipboard. Choose the intended text field before starting. This differs from physical scan-code game bindings and may not work in every application. Enter and Tab remain explicit key actions. Diagnostics show only a generic text label. Stopping can leave a partially typed string; it cannot undo delivered characters.

        Run compatible helpers together
        Enable simple helpers in Saved macros, then review Run together. Separate key resources can coexist (up to four helpers). A member's toggle stops that member while running. Another compatible simple helper may join through its explicit assigned trigger. Conflicting controls, pointer use and sequences are rejected. Focus loss pauses the set; restoration never resumes it. While paused, a member trigger stops the entire set. Resume explicitly resumes the reviewed set; Stop All always ends it.

        Recovery
        If cleanup is pending, release the physical key or button and choose Retry cleanup. Reselect a recreated target. Recalibrate positions after size/DPI changes. Restart after a control-thread failure. Closing normally stops and cleans up; forced process termination cannot guarantee release. Higher-integrity or protected targets may reject standard Windows input.

        Portable data
        A build with portable.mode stores Data beside the executable. Keep that folder writable and back it up. Saved macro folders retain recovery copies. Schema 8 upgrades older imports; older builds cannot read schema-8 saves. Export before switching versions, and keep older data separately. No account, network service, telemetry or automatic elevation is used.

        About
        Minimum Effort Clicker is a private personal preview. This build is unsigned. The package's licenses folder contains third-party notices. Long-run, physical-input and mixed-display acceptance are documented separately; compatibility with every game is not promised.
        """);

    private void OpenTestTarget_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "MinimumEffortClicker.TestTarget.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("TestTarget is included beside the executable in the portable package. In a source checkout, use scripts/Start-TestTarget.ps1.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
        }
        catch (Exception ex) { ReasonLabel.Text = "TestTarget could not open: " + ex.Message; }
    }

    private void ShowText(string title, string text, bool save = false)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 12) };
        AutomationProperties.SetName(box, title);
        var panel = new DockPanel { Margin = new Thickness(22) };
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window { Title = title, Owner = this, Width = 720, Height = 620, MinWidth = 440, MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        dialog.SetResourceReference(BackgroundProperty, "AppBackground");
        dialog.SetResourceReference(ForegroundProperty, "TextPrimary");
        foreach (var type in new[] { typeof(Button), typeof(TextBox) })
            if (TryFindResource(type) is Style style) dialog.Resources[type] = style;
        if (save)
        {
            var export = new Button { Content = "Save this report…", Margin = new Thickness(0, 0, 10, 0) };
            export.Click += async (_, _) =>
            {
                var picker = new SaveFileDialog { FileName = "MinimumEffort-diagnostics.txt", Filter = "Text report|*.txt", AddExtension = true };
                if (picker.ShowDialog(dialog) != true) return;
                export.IsEnabled = false;
                try { await File.WriteAllTextAsync(picker.FileName, text); export.Content = "Report saved"; }
                catch (Exception ex) { AppDialog.Show(dialog, "Could not save report: " + ex.Message); }
                finally { export.IsEnabled = true; }
            };
            actions.Children.Add(export);
        }
        close.Click += (_, _) => dialog.Close(); actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions); panel.Children.Add(box);
        dialog.ShowDialog();
    }
}
