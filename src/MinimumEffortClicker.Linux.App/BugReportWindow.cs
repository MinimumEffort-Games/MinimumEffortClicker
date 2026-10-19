using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>What a report holds besides the user's own text; the main window fills it in when the report opens.</summary>
internal sealed record BugReportParts(string Details, string Log, byte[]? Screenshot, Action<string>? SaveActionList, Func<string, string> Redact);

/// <summary>
/// Report a bug (User, 2026-10-18): the user's text plus chosen parts go into one zip, and a new e-mail to
/// <see cref="BugReport.Address"/> opens with the text filled in (User, 2026-10-19). Nothing is sent by the app.
/// </summary>
internal sealed class BugReportWindow : DialogWindow
{
    private readonly BugReportParts parts;
    private readonly string folder;
    private readonly TextBox happened = new() { MaxLength = 4000, Height = 90, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBox expected = new() { MaxLength = 2000, Height = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Margin = new Thickness(0, 4, 0, 12) };
    private readonly ToggleButton system = Choice("App and system details", true, "App version, Linux distribution and desktop, monitors and scaling, input access, controllers, the main settings.");
    private readonly ToggleButton log = Choice("End of the log", true, "The last lines of log.txt, with a recent error.");
    private readonly ToggleButton screenshot = Choice("Screenshot of the clicker", false, "A picture of the clicker window only, as it is now.");
    private readonly ToggleButton actionList = Choice("Current Action List", false, "The list as a macro file. It may hold colours and image areas captured from your screen.");
    private readonly TextBox preview = new() { IsReadOnly = true, Height = 180, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button create = new() { Content = "Create report and open e-mail", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsEnabled = false };
    /// <summary>The zip that was written, once Create succeeded.</summary>
    public string? ZipPath { get; private set; }
    /// <summary>The prefilled new e-mail for the mail app.</summary>
    public string? MailLink { get; private set; }

    public BugReportWindow(Window owner, BugReportParts parts, string folder, string? text = null)
    {
        this.parts = parts; this.folder = folder; OwnerWindow = owner;
        Title = "Report a bug"; Width = 640; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; FontSize = 14;
        var panel = new StackPanel { Margin = new Thickness(20, 20, 20, 0) };
        panel.Children.Add(new TextBlock { Text = "Report a bug", FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(Secondary("This saves a report as a zip file and opens a new e-mail with your text filled in. Attach the zip to the e-mail, then send it. The clicker itself sends nothing.", new Thickness(0, 0, 0, 8)));
        // For webmail users, whose browser may not open mailto links: the address to copy.
        var address = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var copy = new Button { Content = "Copy address", Margin = new Thickness(8, 0, 0, 0) };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is not { } clipboard) return;
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, BugReport.Address);
            status.Text = "Address copied.";
        };
        DockPanel.SetDock(copy, Dock.Right); address.Children.Add(copy);
        address.Children.Add(new TextBlock { Text = "Send to:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        address.Children.Add(new SelectableTextBlock { Text = BugReport.Address, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(address);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "What happened?" }); body.Children.Add(happened);
        body.Children.Add(new TextBlock { Text = "What did you expect? (optional)" }); body.Children.Add(expected);
        body.Children.Add(new TextBlock { Text = "Include", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var include = new WrapPanel();
        foreach (var toggle in new[] { system, log, screenshot, actionList }) { include.Children.Add(toggle); toggle.IsCheckedChanged += (_, _) => RefreshPreview(); }
        body.Children.Add(include);
        body.Children.Add(Secondary("Your user name, home folder and computer name are replaced with neutral words. Only the developer reads reports; still, check the preview and the zip before you send.", new Thickness(0, 4, 0, 8)));
        body.Children.Add(new Expander { Header = "Preview of the text in the report", Content = preview, HorizontalAlignment = HorizontalAlignment.Stretch });
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = body };
        card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Border");
        panel.Children.Add(card);
        var footer = new StackPanel { Margin = new Thickness(20, 0, 20, 20) };
        footer.Children.Add(status);
        var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Left, IsCancel = true };
        cancel.SetButtonStyle("RedButton"); create.SetButtonStyle("GreenButton");
        cancel.Click += (_, _) => DialogResult = false;
        create.Click += (_, _) => Create();
        buttons.Children.Add(cancel); buttons.Children.Add(create);
        footer.Children.Add(buttons);
        // The buttons stay at the bottom; the report above them scrolls when the screen is too short for the open preview.
        var layout = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
        screenshot.IsEnabled = parts.Screenshot is not null;
        actionList.IsEnabled = parts.SaveActionList is not null;
        happened.Text = text ?? "";
        happened.TextChanged += (_, _) => { create.IsEnabled = (happened.Text ?? "").Trim().Length > 0; RefreshPreview(); };
        expected.TextChanged += (_, _) => RefreshPreview();
        Opened += (_, _) => { if (Screens.ScreenFromWindow(this) is { } screen) MaxHeight = screen.WorkingArea.Height / screen.Scaling - 40; RefreshPreview(); create.IsEnabled = (happened.Text ?? "").Trim().Length > 0; happened.Focus(); happened.CaretIndex = happened.Text?.Length ?? 0; };
    }

    private static ToggleButton Choice(string text, bool on, string tip)
    {
        var toggle = new ToggleButton { Content = text, IsChecked = on, Margin = new Thickness(0, 0, 6, 6) };
        toggle.Classes.Add("choice"); ToolTip.SetTip(toggle, tip);
        return toggle;
    }
    private static TextBlock Secondary(string text, Thickness margin)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        block.Classes.Add("secondary");
        return block;
    }

    private void RefreshPreview() => preview.Text = ReportText();

    /// <summary>The report text, redacted. Only an error from the last day counts: an old one would point at the wrong problem.</summary>
    private string ReportText() => parts.Redact(BugReport.Text(happened.Text ?? "", expected.Text ?? "", system.IsChecked == true ? parts.Details : null,
        log.IsChecked == true ? BugReport.LastError(BugReport.Tail(parts.Log), DateTime.Now.AddDays(-1)) : null));

    private void Create()
    {
        string what = (happened.Text ?? "").Trim();
        if (what.Length == 0) { happened.Focus(); return; }
        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"bug-report-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip");
            string report = ReportText();
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Text(string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(content); }
                Text("report.txt", report);
                if (log.IsChecked == true) Text("log.txt", parts.Redact(BugReport.Tail(parts.Log)));
                if (screenshot.IsChecked == true && parts.Screenshot is { } png) { using var stream = zip.CreateEntry("clicker.png").Open(); stream.Write(png); }
                if (actionList.IsChecked == true && parts.SaveActionList is { } save)
                {
                    string temporary = Path.Combine(Path.GetTempPath(), $"mec-report-{Guid.NewGuid():N}.mecprofile");
                    try { save(temporary); zip.CreateEntryFromFile(temporary, "action-list.mecprofile"); }
                    finally { File.Delete(temporary); }
                }
            }
            ZipPath = path;
            MailLink = BugReport.MailLink(parts.Redact(BugReport.Subject(what)),
                report + Environment.NewLine + Environment.NewLine + $"Report zip: {Path.GetFileName(path)} (attached)");
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { status.Text = "The report could not be saved: " + ex.Message; }
    }
}
