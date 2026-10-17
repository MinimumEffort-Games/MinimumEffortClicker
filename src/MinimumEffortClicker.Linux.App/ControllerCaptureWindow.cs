using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// Waits for one controller button, pressed on the controller or clicked on the picture. Buttons that already do
/// something are outlined red (what they do shows on hover), the current choice green. A button that cannot be used
/// for this is explained right here and the window keeps waiting, so the user knows why and can choose another or
/// Cancel. The Steam Deck back buttons and the touchpad are not on the picture and are buttons below it.
/// </summary>
internal sealed class ControllerCaptureWindow : DialogWindow
{
    private static readonly ControllerButton[] Extras = [ControllerButton.L4, ControllerButton.R4, ControllerButton.L5, ControllerButton.R5, ControllerButton.Touchpad];
    private readonly Func<ControllerButton, string?> taken;
    private readonly TextBlock hint;
    public ControllerButton? SelectedButton { get; private set; }

    /// <param name="bound">What is bound now, as (what it does, button) pairs; a control without a button has null.</param>
    /// <param name="taken">Why a button cannot be used for this, or null when it can.</param>
    /// <param name="current">The button chosen now, outlined green; null for none.</param>
    public ControllerCaptureWindow(Window owner, string purpose, IReadOnlyList<(string What, ControllerButton? Button)> bound,
        Func<ControllerButton, string?> taken, bool connected, ControllerButton? current)
    {
        this.taken = taken;
        OwnerWindow = owner; Title = "Choose a controller button"; SizeToContent = SizeToContent.WidthAndHeight; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(20), Width = 440 }; Content = panel;
        panel.Children.Add(new TextBlock { Text = purpose, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        hint = new TextBlock
        {
            Text = (connected ? "Press the button on the controller, or click it below." : "No controller is connected: click the button below, or connect one and press it.")
                + " Red buttons already do something; hover one to see what.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12)
        };
        panel.Children.Add(hint);

        var boundTo = bound.Where(b => b.Button is not null).ToDictionary(b => b.Button!.Value, b => b.What);
        var picture = new ControllerPicture { Height = 330, Selected = current, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (button, what) in boundTo) picture.Taken(button, what);
        picture.Clicked += Press;
        panel.Children.Add(picture);

        panel.Children.Add(new TextBlock { Text = "Steam Deck", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var extras = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var button in Extras)
        {
            var choice = new Button { Content = ControllerButtons.Label(button), MinWidth = 52, Margin = new Thickness(0, 0, 6, 0) };
            choice.SetButtonStyle(button == current ? "ChosenButton" : boundTo.ContainsKey(button) ? "NeedsChoiceButton" : "StandardButton");
            ToolTip.SetTip(choice, boundTo.TryGetValue(button, out var what) ? what : button == ControllerButton.Touchpad ? "Pressing the touchpad" : "Back button");
            choice.Click += (_, _) => Press(button);
            extras.Children.Add(choice);
        }
        panel.Children.Add(extras);

        var cancel = CancelButton(); cancel.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(cancel);
    }

    /// <summary>A controller button was pressed or clicked (UI thread). Takes it, or says why not and keeps waiting.</summary>
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
