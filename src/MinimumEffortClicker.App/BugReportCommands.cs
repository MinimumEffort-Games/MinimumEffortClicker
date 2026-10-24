using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Storage;
using MinimumEffortClicker.Windows;

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
        var parts = new BugReportParts($"Minimum Effort Clicker {InformationalVersion} (Windows, {(SteamMode.Enabled ? "Steam" : "portable")})",
            SystemDetails(), ReadLog(), ClickerPicture(), ActionListSaver(), RedactPersonal);
        var window = new BugReportWindow(parts, ReportsFolder, happened) { Owner = this };
        if (window.ShowDialog() != true) return;
        if (window.Sent) { AppLog.Note("Bug report", "sent"); ReasonLabel.Text = "Bug report sent. Thank you!"; return; }
        if (window.ZipPath is not { } zip || window.MailLink is not { } link) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* The e-mail step below still names the file. */ }
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            ReasonLabel.Text = $"Report saved. Attach the zip to the e-mail and send it to {BugReport.Address}.";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { AppDialog.Show(this, $"Could not open your mail app: {ex.Message}\n\nThe report is saved as {zip}. Send it from your webmail to {BugReport.Address}."); }
    }

    private static string InformationalVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    /// <summary>"Minimum Effort Clicker 1.0.0 (617701c)", shown in Settings and Help so anyone can see which build runs.</summary>
    private static string VersionLine => "Minimum Effort Clicker " + Core.AppVersion.Display(InformationalVersion);

    private static string RedactPersonal(string text) => BugReport.Redact(text, [
        (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
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
            if (Content is not FrameworkElement root || root.ActualWidth < 1 || root.ActualHeight < 1) return null;
            var dpi = VisualTreeHelper.GetDpi(this);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream);
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

    /// <summary>What a developer needs to reproduce a problem. No window titles: they can be personal.</summary>
    private string SystemDetails()
    {

        string monitors;
        try { monitors = string.Join("; ", DesktopMonitor.List()); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { monitors = "unknown"; }
        string target = runtime is null || runtime.IsDesktopMode ? "Desktop"
            // A remembered target whose window closed has no program any more.
            : runtime.SelectedWindow is { } window && ProgramOf(window.ProcessId) is { Length: > 0 } program ? $"a window of {program}" : "a window (closed or unavailable)";
        string controllers = gamepads?.Connected is { Count: > 0 } pads ? string.Join(", ", pads) : "none";
        string screenshotKey = screenshotSettings.HotkeyEnabled ? ShortcutKeys.Label(screenshotSettings.Hotkey) : "none";
        return string.Join(Environment.NewLine,
            $"- App: Minimum Effort Clicker {InformationalVersion} ({(SteamMode.Enabled ? "Steam" : "portable")})",
            $"- System: {RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}",
            $"- Monitors: {monitors}; clicker scaling {VisualTreeHelper.GetDpi(this).DpiScaleX * 100:0} %",
            $"- Click target: {target}",
            $"- Controllers: {controllers}",
            $"- Keys: Start / Stop {ShortcutKeys.Label(appliedToggleKey)}, Stop All {ShortcutKeys.Label(appliedStopKey)}, Pause {ShortcutKeys.Label(appliedPauseKey)}, "
                + $"Record {ShortcutKeys.Label(appliedRecordKey)}, Screenshot {screenshotKey}; emergency stop: {ChosenEmergencyStop}",
            $"- Action List: {quickHistory.Current.Steps.Count} actions, Loop {(LoopBox.IsChecked == true ? "on" : "off")}; {SavedToggles.Count()} toggles; {controllerBinds.Count} Controller to Key binds");
    }

    /// <summary>At start: a marker left from last time means the clicker did not close normally, so a report is offered.</summary>
    private void CheckLastSession()
    {
        bool unexpected = File.Exists(SessionMarker);
        try { File.WriteAllText(SessionMarker, DateTime.Now.ToString("O")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        if (!unexpected || StartupMeasurement.Enabled || IdleMeasurement.Destination is not null) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (allowClose || closing) return;
            if (AppDialog.Show(this, "The clicker did not close normally last time. Report it as a bug?", "Report a bug", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                OpenBugReport("The clicker closed unexpectedly. ");
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>A normal close, including Windows ending the session.</summary>
    private void EndSession()
    {
        try { File.Delete(SessionMarker); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
