using System.IO;
using System.Windows;
using System.Windows.Controls;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>What a report holds besides the user's own text; the main window fills it in when the report opens.</summary>
internal sealed record BugReportParts(string App, string Details, string Log, byte[]? Screenshot, Action<string>? SaveActionList, Func<string, string> Redact);

/// <summary>
/// Report a bug (User, 2026-10-18): the user's text plus chosen parts go into one zip. Send report posts it to the
/// developer in one press (User, 2026-10-19: no mail app or account needed); only when that fails does the window offer
/// to e-mail it instead. Nothing is sent before Send report is pressed.
/// </summary>
public partial class BugReportWindow : Window
{
    private readonly BugReportParts parts;
    private readonly string folder;
    private readonly CancellationTokenSource closed = new();
    /// <summary>The zip that was written.</summary>
    public string? ZipPath { get; private set; }
    /// <summary>True when the report reached the developer.</summary>
    public bool Sent { get; private set; }
    /// <summary>The prefilled new e-mail, when the user chose to e-mail the report instead.</summary>
    public string? MailLink { get; private set; }

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
        Closed += (_, _) => closed.Cancel();
    }

    private void Text_Changed(object sender, TextChangedEventArgs e)
    {
        SendButton.IsEnabled = HappenedBox.Text.Trim().Length > 0;
        if (IsLoaded) RefreshPreview();
    }
    private void Include_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshPreview(); }
    private void RefreshPreview() => PreviewBox.Text = ReportText();

    /// <summary>The report text, redacted. Only an error from the last day counts: an old one would point at the wrong problem.</summary>
    private string ReportText() => parts.Redact(BugReport.Text(HappenedBox.Text, ExpectedBox.Text, SystemBox.IsChecked == true ? parts.Details : null,
        LogBox.IsChecked == true ? BugReport.LastError(BugReport.Tail(parts.Log), DateTime.Now.AddDays(-1)) : null));
    private string Subject() => parts.Redact(BugReport.Subject(HappenedBox.Text));

    /// <summary>Writes the zip as the report reads now; false (with the reason shown) when it could not be saved.</summary>
    private bool WriteReport()
    {
        try
        {
            Directory.CreateDirectory(folder);
            string path = ZipPath ?? Path.Combine(folder, $"bug-report-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip");
            BugReport.WriteZip(path, ReportText(), LogBox.IsChecked == true ? parts.Redact(BugReport.Tail(parts.Log)) : null,
                ScreenshotBox.IsChecked == true ? parts.Screenshot : null, ActionListBox.IsChecked == true ? parts.SaveActionList : null);
            ZipPath = path;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { StatusText.Text = "The report could not be saved: " + ex.Message; return false; }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (HappenedBox.Text.Trim().Length == 0) { HappenedBox.Focus(); return; }
        if (!WriteReport()) return;
        SendButton.IsEnabled = EmailButton.IsEnabled = false;
        StatusText.Text = "Sending…";
        var result = await BugReportSender.SendAsync(Subject(), ReportText(), ReplyBox.Text.Trim(), parts.App, ZipPath!, closed.Token);
        if (closed.IsCancellationRequested) return;
        if (result.Sent) { Sent = true; DialogResult = true; return; }
        AppLog.Note("Bug report", "could not be sent: " + result.Problem);
        StatusText.Text = $"The report could not be sent. {result.Problem} Try again, or e-mail it instead.";
        SendButton.Content = "Try again";
        EmailButton.Visibility = Visibility.Visible;
        SendButton.IsEnabled = EmailButton.IsEnabled = true;
    }

    private void Email_Click(object sender, RoutedEventArgs e)
    {
        if (!WriteReport()) return;
        MailLink = BugReport.MailLink(Subject(), ReportText() + Environment.NewLine + Environment.NewLine + $"Report zip: {Path.GetFileName(ZipPath)} (attached)");
        DialogResult = true;
    }
}
