using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MinimumEffortClicker.App;

/// <summary>
/// Startup notice. First Linux input access: while it is off, why Record and Stop on my input do not work yet, that
/// everything else does, and a button that turns access on through the system password window. On a Steam Deck the Deck
/// follows: what works and what does not, then Steam's desktop buttons (User, 2026-10-19).
/// DialogResult true means "Turn on".
/// </summary>
public sealed class InputAccessNoticeWindow : DialogWindow
{
    private readonly Avalonia.Controls.Primitives.ToggleButton showAtStartup = new() { Content = "Show at startup", IsChecked = true, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    public bool ShowAtStartup => showAtStartup.IsChecked == true;

    /// <param name="deck">On a Steam Deck: its limits and the note on Steam's desktop buttons; null elsewhere.</param>
    public InputAccessNoticeWindow(string needs, bool accessOff, bool canAskForPassword, string terminalCommand, (string Limits, string DesktopButtons)? deck = null)
    {
        showAtStartup.Classes.Add("choice");
        Title = deck is not null ? "Input access and Steam Deck" : "Input access is off"; Width = 560; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; FontSize = 14;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Heading("Linux input access"));
        if (accessOff)
        {
            var body = new List<Control>
            {
                Text(needs),
                Text("You can use the clicker either way: clicks, keys, the Action List, toggles, colour waits and screenshots all work now.", 10),
                Text(canAskForPassword
                    ? "Turn on asks for your password in the system's own window and gives read access to keyboards and mice to the person signed in at this computer only. You can turn it on or off, and bring this notice back, any time in Settings → Linux setup."
                    : "This system has no password window for apps, so run this once in a terminal (it asks for your password):", 10)
            };
            if (!canAskForPassword) body.Add(new SelectableTextBlock { Text = terminalCommand, FontFamily = new FontFamily("monospace"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            panel.Children.Add(Card([.. body]));
        }
        else panel.Children.Add(Card(Text("On. Recording, Stop on my input, mouse-button shortcuts and hotkeys that work in every app are available.")));
        if (deck is { } d)
        {
            panel.Children.Add(Heading("Steam Deck", top: 18));
            panel.Children.Add(Card(Text(d.Limits), Subheading("Steam's desktop buttons"), Text(d.DesktopButtons)));
        }
        panel.Children.Add(showAtStartup);
        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        var later = new Button { Content = accessOff ? "Later" : "OK", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0) };
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
