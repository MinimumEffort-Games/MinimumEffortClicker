using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MinimumEffortClicker.App;

/// <summary>
/// Startup notice while physical input access is off: why Record and Stop on my input do not work
/// yet, that everything else does, and a button that turns access on through the system password window.
/// DialogResult true means "Turn on".
/// </summary>
public sealed class InputAccessNoticeWindow : DialogWindow
{
    private readonly Avalonia.Controls.Primitives.ToggleButton showAtStartup = new() { Content = "Show at startup", IsChecked = true, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    public bool ShowAtStartup => showAtStartup.IsChecked == true;

    public InputAccessNoticeWindow(string needs, bool canAskForPassword, string terminalCommand, string? deckNote = null)
    {
        showAtStartup.Classes.Add("choice");
        Title = "Input access is off"; Width = 520; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; FontSize = 14;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Recording needs input access", FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = needs, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "You can use the clicker either way: clicks, keys, the Action List, toggles, colour waits and screenshots all work now.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        body.Children.Add(new TextBlock
        {
            Text = canAskForPassword
                ? "Turn on asks for your password in the system's own window and gives read access to keyboards and mice to the person signed in at this computer only. You can turn it on or off, and bring this notice back, any time in Settings → Linux setup."
                : "This system has no password window for apps, so run this once in a terminal (it asks for your password):",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
        });
        if (!canAskForPassword) body.Children.Add(new SelectableTextBlock { Text = terminalCommand, FontFamily = new FontFamily("monospace"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        if (deckNote is not null) body.Children.Add(new TextBlock { Text = deckNote, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = body };
        card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Border");
        panel.Children.Add(card);
        panel.Children.Add(showAtStartup);
        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        var later = new Button { Content = "Later", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0) };
        later.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(later);
        if (canAskForPassword)
        {
            var turnOn = new Button { Content = "Turn on", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0), IsDefault = true };
            turnOn.SetButtonStyle("GreenButton");
            turnOn.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(turnOn);
            Opened += (_, _) => turnOn.Focus();
        }
        else Opened += (_, _) => later.Focus();
        panel.Children.Add(buttons);
        Content = panel;
    }
}
