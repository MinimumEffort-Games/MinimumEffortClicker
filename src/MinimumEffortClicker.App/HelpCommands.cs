using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    /// <summary>
    /// The Help manual (Core <see cref="HelpManual"/>, shared with Linux): a clickable contents list, then every section
    /// with a link back to the contents. A line starting with "- " renders as a bullet; an empty line is a gap.
    /// </summary>
    private void Help_Click(object sender, RoutedEventArgs e)
    {
        const string title = "Minimum Effort Clicker · help";
        var sections = HelpManual.For(linux: false, steam: workshop is not null, steamDeck: false);
        var page = new StackPanel { Margin = new Thickness(4, 0, 18, 12) };
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 12) };
        void ScrollTo(FrameworkElement element) => scroll.ScrollToVerticalOffset(Math.Max(0, element.TranslatePoint(new Point(0, 0), page).Y - 4));
        TextBlock Link(string text, Action open)
        {
            var link = new Hyperlink(new Run(text)) { TextDecorations = TextDecorations.Underline };
            link.SetResourceReference(TextElement.ForegroundProperty, "TextPrimary");
            link.Click += (_, _) => open();
            return new TextBlock(link) { FontSize = 14, Margin = new Thickness(12, 0, 0, 4) };
        }

        page.Children.Add(new TextBlock { Text = VersionLine, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 4) });
        var contentsHeading = new TextBlock { Text = "Contents", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 8) };
        page.Children.Add(contentsHeading);
        var headings = sections.Select(s => new TextBlock { Text = s.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8) }).ToArray();
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
            var back = Link("↑ Contents", () => scroll.ScrollToTop());
            back.Margin = new Thickness(0, 4, 0, 0); back.FontSize = 12;
            page.Children.Add(back);
        }
        AutomationProperties.SetName(scroll, title);
        var panel = new DockPanel { Margin = new Thickness(22) };
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window { Title = title, Owner = this, Width = 760, Height = 640, MinWidth = 440, MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        dialog.SetResourceReference(BackgroundProperty, "AppBackground");
        dialog.SetResourceReference(ForegroundProperty, "TextPrimary");
        if (TryFindResource(typeof(Button)) is Style style) dialog.Resources[typeof(Button)] = style;
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close); panel.Children.Add(scroll);
        dialog.ShowDialog();
    }
}
