using System.Windows;
using System.Windows.Controls;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// Waits for one controller button and shows what every button already does: the global controls and the Controller
/// to Key binds. A button that is taken is explained right here and the window keeps waiting, so the user knows why
/// and can press another one or Cancel.
/// </summary>
internal sealed class ControllerCaptureWindow : Window
{
    private readonly Func<ControllerButton, string?> taken;
    private readonly TextBlock hint;
    public ControllerButton? SelectedButton { get; private set; }

    /// <param name="bound">What is bound now, as (what it does, button) pairs; a control without a button has null.</param>
    /// <param name="taken">Why a button cannot be used for this, or null when it can.</param>
    public ControllerCaptureWindow(Window owner, string purpose, IReadOnlyList<(string What, ControllerButton? Button)> bound,
        Func<ControllerButton, string?> taken, bool connected)
    {
        this.taken = taken;
        Owner = owner; Title = "Press a controller button"; Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "TextPrimary");
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = purpose, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        hint = new TextBlock
        {
            Text = connected ? "Press the controller button to use. Cancel keeps the current choice." : "No controller is connected yet: connect one, then press a button.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12)
        };
        panel.Children.Add(hint);
        panel.Children.Add(new TextBlock { Text = "Already bound", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var list = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        list.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        list.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        foreach (var (what, button) in bound)
        {
            list.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var name = new TextBlock { Text = what, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 12, 1), VerticalAlignment = VerticalAlignment.Center };
            // The button as its picture, about as tall as the text beside it.
            var key = button is { } b ? UiArt.ControllerPicture(b, 22) : new TextBlock { Text = "None", FontWeight = FontWeights.SemiBold };
            key.Margin = new Thickness(0, 1, 0, 1); key.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(name, list.RowDefinitions.Count - 1); Grid.SetRow(key, list.RowDefinitions.Count - 1); Grid.SetColumn(key, 1);
            list.Children.Add(name); list.Children.Add(key);
        }
        panel.Children.Add(list);
        var cancel = new Button { Content = "Cancel", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => DialogResult = false;
        panel.Children.Add(cancel);
    }

    /// <summary>A controller button went down (UI thread). Takes it, or says why not and keeps waiting.</summary>
    public void Press(ControllerButton button)
    {
        if (taken(button) is { } why)
        {
            hint.Text = why;
            hint.SetResourceReference(ForegroundProperty, "StopBorder");
            return;
        }
        SelectedButton = button; DialogResult = true;
    }
}
