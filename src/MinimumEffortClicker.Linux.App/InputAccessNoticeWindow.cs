using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MinimumEffortClicker.App;

/// <summary>
/// Startup notice. First, while the desktop helper is not ready, a one-button setup for it (User, 2026-10-23: a new player
/// must not have to find it in Settings). Then Linux input access: while it is off, why Record and Stop on my input do
/// not work yet, that everything else does, and a button that turns access on through the system password window. On a
/// Steam Deck the Deck follows: what works and what does not, then Steam's desktop buttons (User, 2026-10-19).
/// DialogResult true means "Turn on".
/// </summary>
public sealed class InputAccessNoticeWindow : DialogWindow
{
    private readonly Avalonia.Controls.Primitives.ToggleButton showAtStartup = new() { Content = "Show at startup", IsChecked = true, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    public bool ShowAtStartup => showAtStartup.IsChecked == true;

    /// <param name="deck">On a Steam Deck: its limits and the note on Steam's desktop buttons; null elsewhere.</param>
    /// <param name="helper">While the desktop helper is not ready: what it is for, its button and the setup it runs; null when ready.</param>
    /// <param name="optional">That the clicker works without input access, shown first and bold.</param>
    /// <param name="needs">What turning it on adds.</param>
    /// <param name="on">What is available while it is on.</param>
    public InputAccessNoticeWindow(string optional, string needs, string on, bool accessOff, bool canAskForPassword, string terminalCommand, (string Limits, string DesktopButtons)? deck = null,
        (string Explanation, string Button, Func<Task<(bool Ready, string Message)>> SetUp)? helper = null)
    {
        showAtStartup.Classes.Add("choice");
        Title = helper is not null ? "Set up Minimum Effort Clicker" : deck is not null ? "Recording and Steam Deck" : "Recording (optional)";
        Width = 560; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; FontSize = 14;
        var panel = new StackPanel { Margin = new Thickness(20) };
        var later = new Button { Content = accessOff || helper is not null ? "Later" : "OK", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0) };
        Button? setUp = null;
        if (helper is { } h)
        {
            panel.Children.Add(Heading("Desktop helper"));
            var status = Text("", 10);
            setUp = new Button { Content = h.Button, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            setUp.SetButtonStyle("GreenButton");
            setUp.Click += async (_, _) =>
            {
                setUp.IsEnabled = false; status.Text = "Setting up…";
                var (ready, message) = await h.SetUp();
                status.Text = message;
                setUp.IsVisible = !ready; setUp.IsEnabled = true;
                // Set up once and done (User, 2026-10-23): with nothing left to do, the notice says so and stops showing.
                if (ready && !accessOff) { later.Content = "Done"; showAtStartup.IsChecked = false; later.Focus(); }
            };
            panel.Children.Add(Card(Text(h.Explanation), setUp, status));
        }
        panel.Children.Add(Heading("Recording (optional)", top: helper is null ? 0 : 18));
        if (accessOff)
        {
            var works = Text(optional); works.FontWeight = FontWeight.SemiBold;
            var body = new List<Control>
            {
                works,
                Text(needs, 10),
                Text(canAskForPassword
                    ? "Turn on asks for your password in the system's own window, and only gives access to the person signed in at this computer. You can turn it off again in Settings → Linux setup."
                    : "This system has no password window for apps, so run this once in a terminal (it asks for your password):", 10)
            };
            if (!canAskForPassword) body.Add(new SelectableTextBlock { Text = terminalCommand, FontFamily = new FontFamily("monospace"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            panel.Children.Add(Card([.. body]));
        }
        else panel.Children.Add(Card(Text("On: " + char.ToLowerInvariant(on[0]) + on[1..])));
        if (deck is { } d)
        {
            panel.Children.Add(Heading("Steam Deck", top: 18));
            panel.Children.Add(Card(Text(d.Limits), Subheading("Steam's desktop buttons"), Text(d.DesktopButtons)));
        }
        panel.Children.Add(showAtStartup);
        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        later.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(later);
        if (accessOff && canAskForPassword)
        {
            var turnOn = new Button { Content = "Turn on", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0), IsDefault = true };
            turnOn.SetButtonStyle("GreenButton");
            turnOn.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(turnOn);
            Opened += (_, _) => turnOn.Focus();
        }
        else if (setUp is not null) Opened += (_, _) => setUp.Focus();
        else Opened += (_, _) => later.Focus();
        panel.Children.Add(buttons);
        // A long Deck notice still fits a 1280×800 screen: the text scrolls, the buttons stay at the bottom.
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Opened += (_, _) => { if (Screens.ScreenFromWindow(this) is { } screen) MaxHeight = screen.WorkingArea.Height / screen.Scaling - 40; };
    }

    private static TextBlock Heading(string text, double top = 0) =>
        new() { Text = text, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 10) };
    private static TextBlock Subheading(string text) =>
        new() { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 4) };
    private static TextBlock Text(string text, double top = 0) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
    private static Border Card(params Control[] children)
    {
        var body = new StackPanel();
        foreach (var child in children) body.Children.Add(child);
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = body };
        card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Border");
        return card;
    }
}
