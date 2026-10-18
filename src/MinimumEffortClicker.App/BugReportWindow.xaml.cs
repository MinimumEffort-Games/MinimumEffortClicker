using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>What a report holds besides the user's own text; the main window fills it in when the report opens.</summary>
internal sealed record BugReportParts(string Details, string Log, byte[]? Screenshot, Action<string>? SaveActionList, Func<string, string> Redact);

/// <summary>
/// Report a bug (User, 2026-10-18): the user's text plus chosen parts go into one zip, and a GitHub issue opens with
/// the text filled in. Nothing is sent by the app.
/// </summary>
public partial class BugReportWindow : Window
{
    private readonly BugReportParts parts;
    private readonly string folder;
    /// <summary>The zip that was written, once Create succeeded.</summary>
    public string? ZipPath { get; private set; }
    /// <summary>The prefilled GitHub issue for the browser.</summary>
    public string? IssueLink { get; private set; }

    internal BugReportWindow(BugReportParts parts, string folder, string? happened = null)
    {
        this.parts = parts; this.folder = folder;
        InitializeComponent();
        // On a small screen the open preview would push the buttons off it; the report then scrolls inside the window.
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        ScreenshotBox.IsEnabled = parts.Screenshot is not null;
        ActionListBox.IsEnabled = parts.SaveActionList is not null;
        HappenedBox.Text = happened ?? "";
        Loaded += (_, _) => { RefreshPreview(); HappenedBox.Focus(); HappenedBox.CaretIndex = HappenedBox.Text.Length; };
    }

    private void Text_Changed(object sender, TextChangedEventArgs e)
    {
        CreateButton.IsEnabled = HappenedBox.Text.Trim().Length > 0;
        if (IsLoaded) RefreshPreview();
    }
    private void Include_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshPreview(); }
    private void RefreshPreview() => PreviewBox.Text = ReportText();

    /// <summary>The report as GitHub markdown: the user's text, the details and the last error, redacted.</summary>
    private string ReportText()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("**What happened**").AppendLine(HappenedBox.Text.Trim()).AppendLine();
        if (ExpectedBox.Text.Trim() is { Length: > 0 } expected) text.AppendLine("**What I expected**").AppendLine(expected).AppendLine();
        if (SystemBox.IsChecked == true) text.AppendLine("**App and system**").AppendLine(parts.Details).AppendLine();
        // Only an error from the last day; an old one from days ago would point at the wrong problem.
        if (LogBox.IsChecked == true && BugReport.LastError(BugReport.Tail(parts.Log), DateTime.Now.AddDays(-1)) is { } error)
            text.AppendLine("**Last error**").AppendLine("```").AppendLine(error).AppendLine("```").AppendLine();
        return parts.Redact(text.ToString().TrimEnd());
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (HappenedBox.Text.Trim().Length == 0) { HappenedBox.Focus(); return; }
        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"bug-report-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip");
            string report = ReportText();
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Text(string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(content); }
                Text("report.md", report);
                if (LogBox.IsChecked == true) Text("log.txt", parts.Redact(BugReport.Tail(parts.Log)));
                if (ScreenshotBox.IsChecked == true && parts.Screenshot is { } png) { using var stream = zip.CreateEntry("clicker.png").Open(); stream.Write(png); }
                if (ActionListBox.IsChecked == true && parts.SaveActionList is { } save)
                {
                    string temporary = Path.Combine(Path.GetTempPath(), $"mec-report-{Guid.NewGuid():N}.mecprofile");
                    try { save(temporary); zip.CreateEntryFromFile(temporary, "action-list.mecprofile"); }
                    finally { File.Delete(temporary); }
                }
            }
            string firstLine = HappenedBox.Text.Trim().Split('\n')[0].Trim();
            string title = parts.Redact(firstLine.Length > 80 ? firstLine[..80] + "…" : firstLine);
            ZipPath = path;
            IssueLink = BugReport.IssueLink(title, report + Environment.NewLine + Environment.NewLine + "_Report zip attached._");
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { StatusText.Text = "The report could not be saved: " + ex.Message; }
    }
}
