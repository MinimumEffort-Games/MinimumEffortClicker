using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Linux;
using MinimumEffortClicker.Storage;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private string ReportsFolder => Path.Combine(store.DirectoryPath, "Reports");
    // Written at start and removed on a normal close: still there at the next start means the clicker did not close normally.
    private string SessionMarker => Path.Combine(store.DirectoryPath, "running.txt");

    private void BugReport_Click(object sender, RoutedEventArgs e) => OpenBugReport(null);

    /// <summary>Report a bug, next to Help (User, 2026-10-18): Send report posts it; the e-mail route is the fallback (User, 2026-10-19).</summary>
    private void OpenBugReport(string? happened)
    {
        // The picture is taken now, before the report window covers the clicker.
        var parts = new BugReportParts($"Minimum Effort Clicker {InformationalVersion} (Linux, {LinuxSetup.SessionSummary})",
            SystemDetails(), ReadLog(), ClickerPicture(), ActionListSaver(), RedactPersonal);
        var window = new BugReportWindow(this, parts, ReportsFolder, happened);
        if (window.ShowDialog() != true) return;
        if (window.Sent) { AppLog.Note("Bug report", "sent"); ReasonLabel.Text = "Bug report sent. Thank you!"; return; }
        if (window.ZipPath is not { } zip || window.MailLink is not { } link) return;
        try { Process.Start(new ProcessStartInfo("xdg-open", [ReportsFolder]) { UseShellExecute = false }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* The e-mail step below still names the file. */ }
        // A Steam Deck has no mail app, and xdg-open then does nothing visible: hand over the address for webmail instead.
        if (!HasMailApp())
        {
            if (Clipboard is { } clipboard) _ = Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, BugReport.Address);
            ReasonLabel.Text = $"Report saved as {Path.GetFileName(zip)}. Send it to {BugReport.Address} from your webmail.";
            AppDialog.Show(this, $"No e-mail app is set up on this computer, so the address {BugReport.Address} is copied for you.\n\n"
                + $"Write to it from your webmail (Gmail, Outlook.com, …) in the browser, paste what happened, and attach {Path.GetFileName(zip)} from the folder that just opened.", "Report a bug");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", [link]) { UseShellExecute = false });
            ReasonLabel.Text = $"Report saved as {Path.GetFileName(zip)}. Attach it to the e-mail and send it to {BugReport.Address}.";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { AppDialog.Show(this, $"Could not open your mail app: {ex.Message}\n\nThe report is saved as {zip}. Send it from your webmail to {BugReport.Address}."); }
    }

    /// <summary>Whether the desktop has an app for mailto links. True when it cannot tell, so the normal route is tried.</summary>
    private static bool HasMailApp()
    {
        try
        {
            using var query = Process.Start(new ProcessStartInfo("xdg-mime", ["query", "default", "x-scheme-handler/mailto"])
                { RedirectStandardOutput = true, UseShellExecute = false });
            if (query is null) return true;
            var answer = query.StandardOutput.ReadToEndAsync();
            if (!answer.Wait(TimeSpan.FromSeconds(2))) { try { query.Kill(); } catch (InvalidOperationException) { } return true; }
            return answer.Result.Trim().Length > 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or AggregateException) { return true; }
    }

    private static string InformationalVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    /// <summary>"Minimum Effort Clicker 1.0.0 (617701c)", shown in Settings and Help so anyone can see which build runs.</summary>
    private static string VersionLine => "Minimum Effort Clicker " + Core.AppVersion.Display(InformationalVersion);

    private static string RedactPersonal(string text) => BugReport.Redact(text, [
        (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~"),
        (Environment.UserName, "User"), (Environment.MachineName, "PC")]);

    private static string ReadLog()
    {
        if (AppLog.FilePath is not { } path || !File.Exists(path)) return "";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>A picture of the clicker window's own content, never of other windows.</summary>
    private byte[]? ClickerPicture()
    {
        try
        {
            if (Content is not Visual root || Bounds.Width < 1 || Bounds.Height < 1) return null;
            double scale = RenderScaling;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
            bitmap.Render(root);
            using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OutOfMemoryException) { return null; }
    }

    /// <summary>Writes the current Action List as a macro file, or null when the list is empty.</summary>
    private Action<string>? ActionListSaver()
    {
        var sequence = quickHistory.Current;
        if (sequence.Steps.Count == 0) return null;
        var points = quickPoints;
        return path => HelperProfiles.Export(path, new ProfileDefinition(ProfileDefinition.CurrentVersion, Guid.NewGuid(), "Bug report",
            [new MacroDefinition(Guid.NewGuid(), "Action List", null, "", sequence)], points));
    }

    /// <summary>The distribution's own name, from /etc/os-release.</summary>
    private static string Distribution()
    {
        try
        {
            foreach (var line in File.ReadLines("/etc/os-release"))
                if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal)) return line["PRETTY_NAME=".Length..].Trim('"');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return "Linux";
    }

    /// <summary>What a developer needs to reproduce a problem. No window titles: they can be personal.</summary>
    private string SystemDetails()
    {

        string monitors = string.Join("; ", Screens.All.Select((s, i) => $"Monitor {i + 1}{(s.IsPrimary ? " (main)" : "")} · {s.Bounds.Width} × {s.Bounds.Height} at {s.Scaling * 100:0} %"));
        string target = runtime is null || runtime.IsDesktopMode ? "Desktop"
            // A remembered target whose window closed has no program any more.
            : runtime.SelectedWindow is { } window && ProgramOf(window.ProcessId) is { Length: > 0 } program ? $"a window of {program}" : "a window (closed or unavailable)";
        string controllers = gamepads?.Connected is { Count: > 0 } pads ? string.Join(", ", pads) : "none";
        string screenshotKey = screenshotSettings.HotkeyEnabled ? ShortcutKeys.Label(screenshotSettings.Hotkey) : "none";
        string helper = runtime?.Control.Helper is null ? $"{LinuxSetup.HelperName} not connected" : $"{LinuxSetup.HelperName} connected";
        return string.Join(Environment.NewLine,
            $"- App: Minimum Effort Clicker {InformationalVersion} (Linux)",
            $"- System: {Distribution()}, {Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "unknown desktop"} on {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown session"}, kernel {Environment.OSVersion.Version}{(LinuxSetup.IsSteamDeck ? ", Steam Deck" : "")}",
            $"- Input access: {(runtime?.Control.PhysicalInputAvailable == true ? "on" : "off")}; {helper}",
            $"- Monitors: {monitors}; X11 pixels per desktop unit {DesktopPixels.Factor:0.##}",
            $"- Click target: {target}",
            $"- Controllers: {controllers}",
            $"- Keys: Start / Stop {ShortcutKeys.Label(appliedToggleKey)}, Stop All {ShortcutKeys.Label(appliedStopKey)}, Pause {ShortcutKeys.Label(appliedPauseKey)}, "
                + $"Record {ShortcutKeys.Label(appliedRecordKey)}, Screenshot {screenshotKey}; emergency stop: {ChosenEmergencyStop}",
            $"- Action List: {quickHistory.Current.Steps.Count} actions, Loop {(LoopBox.IsChecked == true ? "on" : "off")}; {SavedToggles.Count()} toggles; {controllerBinds.Count} Controller to Key binds");
    }

    /// <summary>At start: a marker left from last time means the clicker did not close normally, so a report is offered.</summary>
    /// <summary>When the previous run started, from the marker it left behind; null when unreadable.</summary>
    private DateTime? PreviousStart()
    {
        try { return DateTime.TryParse(File.ReadAllText(SessionMarker).Trim(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private void CheckLastSession()
    {
        bool unexpected = File.Exists(SessionMarker);
        var previousStart = PreviousStart();
        try { File.WriteAllText(SessionMarker, DateTime.Now.ToString("O")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        if (!unexpected || StartupMeasurement.Enabled || IdleMeasurement.Destination is not null) return;
        // Steam's Stop button, a kill and a forced log-out end the app with no chance to clean up. Only a crash the log
        // recorded since the previous start is worth a report (User, 2026-10-27: no report offers after Steam's Stop).
        if (BugReport.LastError(ReadLog(), previousStart) is null) { AppLog.Note("Start", "the previous run was ended from outside (no normal close, no error logged); no report offered"); return; }
        Dispatcher.UIThread.Post(() =>
        {
            if (allowClose || closing) return;
            if (AppDialog.Show(this, "The clicker did not close normally last time. Report it as a bug?", "Report a bug", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                OpenBugReport("The clicker closed unexpectedly. ");
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>A normal close, including the desktop session ending.</summary>
    private void EndSession()
    {
        try { File.Delete(SessionMarker); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
