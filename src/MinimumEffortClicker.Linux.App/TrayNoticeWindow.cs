using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MinimumEffortClicker.App;

/// <summary>First-close notice: the window hides to the top bar and keeps hotkeys available.</summary>
public sealed class TrayNoticeWindow : DialogWindow
{
    public bool DisableRequested { get; private set; }
    public TrayNoticeWindow()
    {
        Title = "ME Clicker will stay active"; Width = 460; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; FontSize = 14;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "ME Clicker will minimize to the top bar and stay active.", FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "Click Disable if you do not want this.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "(Can be changed in Settings at any time.)", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = body };
        card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Border");
        panel.Children.Add(card);
        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        var disable = new Button { Content = "DISABLE", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0) };
        disable.SetButtonStyle("RedButton");
        disable.Click += (_, _) => { DisableRequested = true; DialogResult = false; };
        var okay = new Button { Content = "OK", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0), IsDefault = true };
        okay.SetButtonStyle("GreenButton");
        okay.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(disable); buttons.Children.Add(okay);
        panel.Children.Add(buttons);
        Content = panel;
        Opened += (_, _) => okay.Focus();
    }
}
