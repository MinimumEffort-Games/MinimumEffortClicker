using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// Waits for one controller button and shows what every button already does: the global controls and the Controller
/// to Key binds. A button that is taken is explained right here and the window keeps waiting, so the user knows why
/// and can press another one or Cancel.
/// </summary>
internal sealed class ControllerCaptureWindow : DialogWindow
{
    private readonly Func<ControllerButton, string?> taken;
    private readonly TextBlock hint;
    public ControllerButton? SelectedButton { get; private set; }

    /// <param name="bound">What is bound now, as (what it does, button) pairs.</param>
    /// <param name="taken">Why a button cannot be used for this, or null when it can.</param>
    public ControllerCaptureWindow(Window owner, string purpose, IReadOnlyList<(string What, string Button)> bound,
        Func<ControllerButton, string?> taken, bool connected)
    {
        this.taken = taken;
        OwnerWindow = owner; Title = "Press a controller button"; Width = 460; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = purpose, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        hint = new TextBlock
        {
            Text = connected ? "Press the controller button to use. Cancel keeps the current choice." : "No controller is connected yet: connect one, then press a button.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12)
        };
        panel.Children.Add(hint);
        panel.Children.Add(new TextBlock { Text = "Already bound", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var list = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 14) };
        foreach (var (what, button) in bound)
        {
            list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            int row = list.RowDefinitions.Count - 1;
            var name = new TextBlock { Text = what, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 12, 1) };
            var key = new TextBlock { Text = button, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 1, 0, 1) };
            Grid.SetRow(name, row); Grid.SetRow(key, row); Grid.SetColumn(key, 1);
            list.Children.Add(name); list.Children.Add(key);
        }
        panel.Children.Add(list);
        var cancel = CancelButton(); cancel.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(cancel);
    }

    /// <summary>A controller button went down (UI thread). Takes it, or says why not and keeps waiting.</summary>
    public void Press(ControllerButton button)
    {
        if (taken(button) is { } why)
        {
            hint.Text = why;
            if (this.TryFindResource("StopBorder", ActualThemeVariant, out var brush) && brush is IBrush stop) hint.Foreground = stop;
            return;
        }
        SelectedButton = button; DialogResult = true;
    }
}
