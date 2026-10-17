using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// Chooses a key, or also a mouse button, by clicking it: a keyboard of keycap pictures laid out like a US keyboard,
/// and when asked the labelled mouse beside the keyboard. Keys without a keycap (F13–F24) are not offered. The
/// current choice has the green outline. Keys the caller cannot use are greyed, with the reason on hover.
/// </summary>
internal sealed class KeyPickerWindow : Window
{
    private const double Tile = 42, Gap = 14;

    // Each block is a list of rows; a row lists key names (SequenceKeys) left to right, "" for an empty tile.
    private static readonly string[][] MainBlock =
    [
        ["Escape", "", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"],
        ["Backtick", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "Minus", "Equals", "Backspace"],
        ["Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "Left bracket", "Right bracket", "Backslash"],
        ["Caps Lock", "A", "S", "D", "F", "G", "H", "J", "K", "L", "Semicolon", "Quote", "Enter"],
        ["Left Shift", "Z", "X", "C", "V", "B", "N", "M", "Comma", "Period", "Slash", "Right Shift"],
        ["Left Ctrl", "Left Win", "Left Alt", "Space", "Right Alt", "Right Win", "Menu", "Right Ctrl"]
    ];
    private static readonly string[][] NavigationBlock =
    [
        ["Print Screen", "Scroll Lock", "Pause"],
        ["Insert", "Home", "Page Up"],
        ["Delete", "End", "Page Down"],
        [],
        ["", "Up", ""],
        ["Left", "Down", "Right"]
    ];
    private static readonly string[][] NumpadBlock =
    [
        [],
        ["Num Lock", "Numpad /", "Numpad *", "Numpad -"],
        ["Numpad 7", "Numpad 8", "Numpad 9", "Numpad +"],
        ["Numpad 4", "Numpad 5", "Numpad 6"],
        ["Numpad 1", "Numpad 2", "Numpad 3", "Numpad Enter"],
        ["Numpad 0", "Numpad ."]
    ];

    private readonly InputControl? current;
    private readonly Func<InputControl, string?> unusable;
    public InputControl? Selected { get; private set; }

    /// <param name="purpose">The line above the keyboard: what the choice is for.</param>
    /// <param name="current">The current key or mouse button, outlined green; null when nothing is chosen yet.</param>
    /// <param name="unusable">Why a key or button cannot be chosen here (shown on hover), or null when it can.</param>
    /// <param name="withMouse">Also offer the mouse buttons, on the labelled mouse beside the keyboard.</param>
    public KeyPickerWindow(Window owner, string title, string purpose, InputControl? current, Func<InputControl, string?> unusable, bool withMouse)
    {
        this.current = current; this.unusable = unusable;
        Owner = owner; Title = title; SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "TextPrimary");
        var panel = new StackPanel { Margin = new Thickness(16) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = purpose, Margin = new Thickness(0, 0, 0, 10) });

        var keyboard = new StackPanel { Orientation = Orientation.Horizontal };
        keyboard.Children.Add(Block(MainBlock));
        keyboard.Children.Add(Block(NavigationBlock, Gap));
        keyboard.Children.Add(Block(NumpadBlock, Gap));
        if (withMouse)
        {
            // Beside the keyboard, as on a desk, and as tall as the keyboard.
            var mouse = new MouseButtonPicker { Height = Tile * MainBlock.Length, Margin = new Thickness(Gap * 2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            if (current is { Kind: InputKind.MouseButton } button) mouse.Selected = (PointerButton)button.Code;
            mouse.SelectedChanged += (_, _) => { if (mouse.Selected is { } chosen) Choose(InputControl.Mouse(chosen)); };
            keyboard.Children.Add(mouse);
        }
        panel.Children.Add(keyboard);

        var cancel = new Button { Content = "Cancel", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        panel.Children.Add(cancel);
    }

    /// <summary>The key name (SequenceKeys) of a chosen key, for callers that keep names.</summary>
    public static string? KeyName(InputControl input) =>
        input.Kind == InputKind.ScanCode ? SequenceKeys.Supported.FirstOrDefault(k => k.Value == input.Code).Key : null;

    private Grid Block(string[][] rows, double left = 0)
    {
        var grid = new Grid { Margin = new Thickness(left, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        int columns = rows.Max(r => r.Length);
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(Tile) });
        for (int r = 0; r < rows.Length; r++)
        {
            grid.RowDefinitions.Add(new() { Height = new GridLength(Tile) });
            for (int c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c].Length == 0) continue;
                var key = KeyTile(rows[r][c]);
                Grid.SetRow(key, r); Grid.SetColumn(key, c);
                grid.Children.Add(key);
            }
        }
        return grid;
    }

    private Button KeyTile(string name)
    {
        var button = new Button { ToolTip = name };
        button.SetResourceReference(StyleProperty, "KeycapButton");
        button.Content = UiArt.Key(name) is { } art
            ? new Image { Source = art, Stretch = Stretch.Uniform }
            : new TextBlock { Text = name, FontSize = 10, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        RenderOptions.SetBitmapScalingMode(button, BitmapScalingMode.HighQuality);
        // Windows, Menu and Pause are drawn for the keyboard's shape; the clicker cannot press them.
        string? why = SequenceKeys.Supported.TryGetValue(name, out int code) ? unusable(InputControl.Key(code)) : "The clicker cannot press this key.";
        if (why is not null)
        {
            button.IsEnabled = false;
            ToolTipService.SetShowOnDisabled(button, true);
            button.ToolTip = $"{name}: {why}";
            return button;
        }
        var input = InputControl.Key(code);
        if (input == current) button.SetResourceReference(BorderBrushProperty, "SuccessBorder");
        button.Click += (_, _) => Choose(input);
        return button;
    }

    private void Choose(InputControl input) { Selected = input; DialogResult = true; }
}
