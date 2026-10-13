using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MinimumEffortClicker.App;

/// <summary>Shared app-owned notice/review surface. New popups should use this instead of a system message box.</summary>
public static class AppDialog
{
    public static MessageBoxResult Show(string message, string title = "Minimum Effort Clicker",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None) => Show(null, message, title, buttons, image, defaultResult);

    public static MessageBoxResult Show(Window? owner, string message, string title = "Minimum Effort Clicker",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var fallback = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
        if (defaultResult == MessageBoxResult.None) defaultResult = buttons == MessageBoxButton.OK ? MessageBoxResult.OK : fallback;
        var result = fallback;
        var dialog = new DialogWindow { Title = title, Width = 500, SizeToContent = SizeToContent.Height, CanResize = false,
            ShowInTaskbar = false, FontSize = 14, OwnerWindow = owner,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) });
        var body = new Border { Padding = new(14), CornerRadius = new(8), BorderThickness = new(1) };
        body.SetResourceReference(Border.BackgroundProperty, "Surface"); body.SetResourceReference(Border.BorderBrushProperty, "Border");
        body.Child = new ScrollViewer { MaxHeight = 420, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } };
        panel.Children.Add(body);
        var row = new Grid { Margin = new(0, 18, 0, 0) };
        row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new());
        if (buttons == MessageBoxButton.YesNoCancel) row.ColumnDefinitions.Add(new());
        void Add(string label, MessageBoxResult choice, int column, bool positive)
        {
            var button = new Button { Content = label, MinWidth = 90, Margin = new(0),
                HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                IsDefault = defaultResult == choice || buttons == MessageBoxButton.OK,
                IsCancel = choice == fallback };
            button.SetButtonStyle(positive ? "GreenButton" : "RedButton");
            button.Click += (_, _) => { result = choice; dialog.DialogResult = positive; };
            Grid.SetColumn(button, column); row.Children.Add(button);
        }
        if (buttons != MessageBoxButton.OK) Add(buttons == MessageBoxButton.YesNo ? "No" : "Cancel", fallback, 0, false);
        Add(buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? "Yes" : "OK",
            buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK,
            buttons == MessageBoxButton.YesNoCancel ? 2 : 1, true);
        if (buttons == MessageBoxButton.YesNoCancel) Add("No", MessageBoxResult.No, 1, false);
        panel.Children.Add(row); dialog.Content = panel;
        // Escape keeps the cancel/no default.
        dialog.ShowDialog(); return result;
    }
}
