using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

internal sealed class ShortcutCaptureWindow : Window
{
    public int? SelectedKey { get; private set; }
    public ShortcutCaptureWindow(Window owner, string purpose)
    {
        Owner = owner; Title = purpose; Width = 430; Height = 225; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "TextPrimary");
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        var hint = new TextBlock { Text = "Press a key, or click a mouse button inside this box.\nOne key or button is captured. Use Cancel to keep your current choice.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15) };
        panel.Children.Add(hint);
        var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => DialogResult = false; panel.Children.Add(cancel);
        void Select(int key)
        {
            if (!ShortcutKeys.Supported(key)) { hint.Text = "That key cannot be used here. Press another key or click Cancel."; return; }
            SelectedKey = key; DialogResult = true;
        }
        // Escape cancels, like in every other dialog, so it is never captured as a shortcut.
        PreviewKeyDown += (_, e) => { e.Handled = true; if (e.Key == Key.Escape) DialogResult = false; else if (!e.IsRepeat) Select(KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key)); };
        PreviewMouseDown += (_, e) =>
        {
            // The explicit cancel control is excluded from mouse binding capture.
            if (cancel.IsMouseOver) return;
            e.Handled = true;
            Select(e.ChangedButton switch { MouseButton.Left => 1, MouseButton.Right => 2, MouseButton.Middle => 4, MouseButton.XButton1 => 5, MouseButton.XButton2 => 6, _ => 0 });
        };
        Loaded += (_, _) => { Activate(); Focus(); Keyboard.Focus(this); };
    }
}
