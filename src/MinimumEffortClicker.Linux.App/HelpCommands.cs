using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    /// <summary>
    /// The Help manual (Core <see cref="HelpManual"/>, shared with Windows): a clickable contents list, then every section
    /// with a link back to the contents. A line starting with "- " renders as a bullet; an empty line is a gap.
    /// </summary>
    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        const string title = "Minimum Effort Clicker · help";
        var sections = HelpManual.For(linux: true, steam: false, steamDeck: LinuxSetup.IsSteamDeck);
        var page = new StackPanel { Margin = new Thickness(4, 0, 18, 12) };
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 12) };
        void ScrollTo(Control element)
        {
            if (element.TranslatePoint(new Point(0, 0), page) is { } at) scroll.Offset = new Vector(0, Math.Max(0, at.Y - 4));
        }
        TextBlock Link(string text, Action open)
        {
            var link = new TextBlock { Text = text, FontSize = 14, TextDecorations = TextDecorations.Underline, Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(12, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            link.PointerPressed += (_, args) => { if (args.GetCurrentPoint(link).Properties.IsLeftButtonPressed) { open(); args.Handled = true; } };
            return link;
        }

        page.Children.Add(new TextBlock { Text = "Contents", FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 8) });
        var headings = sections.Select(s => new TextBlock { Text = s.Title, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 22, 0, 8) }).ToArray();
        for (int i = 0; i < sections.Count; i++)
        {
            var heading = headings[i];
            page.Children.Add(Link($"{i + 1}. {sections[i].Title}", () => ScrollTo(heading)));
        }
        for (int i = 0; i < sections.Count; i++)
        {
            page.Children.Add(headings[i]);
            foreach (string line in sections[i].Lines)
            {
                if (line.Length == 0) { page.Children.Add(new Border { Height = 8 }); continue; }
                bool bullet = line.StartsWith("- ", StringComparison.Ordinal);
                page.Children.Add(new TextBlock { Text = bullet ? "•  " + line[2..] : line, FontSize = 14, TextWrapping = TextWrapping.Wrap,
                    LineHeight = 22, Margin = bullet ? new Thickness(12, 0, 0, 4) : new Thickness(0, 0, 0, 4) });
            }
            var back = Link("↑ Contents", () => scroll.Offset = default);
            back.Margin = new Thickness(0, 4, 0, 0); back.FontSize = 12;
            page.Children.Add(back);
        }
        Avalonia.Automation.AutomationProperties.SetName(scroll, title);
        var panel = new DockPanel { Margin = new Thickness(22) };
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new DialogWindow { Title = title, OwnerWindow = this, Width = 760, Height = 640, MinWidth = 440, MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        dialog.SetResourceReference(BackgroundProperty, "AppBackground");
        dialog.SetResourceReference(ForegroundProperty, "TextPrimary");
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close); panel.Children.Add(scroll);
        dialog.ShowDialog();
    }
}
