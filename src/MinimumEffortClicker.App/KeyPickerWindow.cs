using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// Chooses the key for a Key Press: a keyboard of keycap pictures, laid out like a US keyboard, plus the keys that have
/// no keycap (F13–F24) as plain buttons. The current key has the green outline. Keys the clicker cannot press here are
/// greyed: Ctrl, Shift and Alt have their own buttons next to the key, and Windows, Menu and Pause are not supported.
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

    private readonly string current;
    public string? SelectedKey { get; private set; }

    public KeyPickerWindow(Window owner, string current)
    {
        this.current = current;
        Owner = owner; Title = "Choose a key"; SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "TextPrimary");
        var panel = new StackPanel { Margin = new Thickness(16) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Click the key to press. Ctrl, Shift and Alt are the buttons next to the key.", Margin = new Thickness(0, 0, 0, 10) });

        var keyboard = new StackPanel { Orientation = Orientation.Horizontal };
        keyboard.Children.Add(Block(MainBlock));
        keyboard.Children.Add(Block(NavigationBlock, Gap));
        keyboard.Children.Add(Block(NumpadBlock, Gap));
        panel.Children.Add(keyboard);

        // Keys the clicker can press that have no keycap picture.
        var others = SequenceKeys.Supported.Keys.Where(k => UiArt.Key(k) is null && CanChoose(k)).ToList();
        if (others.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "More keys", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            var more = new WrapPanel();
            foreach (var name in others)
            {
                var button = new Button { Content = name, Margin = new Thickness(0, 0, 6, 6), MinWidth = 52 };
                if (name == current) button.SetResourceReference(StyleProperty, "ChosenButton");
                button.Click += (_, _) => Choose(name);
                more.Children.Add(button);
            }
            panel.Children.Add(more);
        }

        var cancel = new Button { Content = "Cancel", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        panel.Children.Add(cancel);
    }

    /// <summary>The keys the Key Press action takes: supported, and not a modifier (those are the toggles).</summary>
    private static bool CanChoose(string name) => SequenceKeys.Supported.TryGetValue(name, out int code) && !SequenceKeys.IsModifier(InputControl.Key(code));

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
        if (!CanChoose(name))
        {
            button.IsEnabled = false;
            ToolTipService.SetShowOnDisabled(button, true);
            button.ToolTip = SequenceKeys.Supported.ContainsKey(name) ? $"{name}: use the Ctrl, Shift and Alt buttons next to the key." : $"{name} cannot be pressed by the clicker.";
        }
        else if (name == current) button.SetResourceReference(BorderBrushProperty, "SuccessBorder");
        button.Click += (_, _) => Choose(name);
        return button;
    }

    private void Choose(string name) { SelectedKey = name; DialogResult = true; }
}
