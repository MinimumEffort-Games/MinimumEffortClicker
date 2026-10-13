using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

internal sealed class ScreenshotSelectionWindow : Window
{
    public ScreenshotBounds? Selection { get; private set; }
    public ScreenshotSelectionWindow(BitmapSource image, ScreenshotBounds bounds)
    {
        Title = "Select screenshot area";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        // The main window is hidden, so keep this task reachable through Alt+Tab/taskbar.
        ShowInTaskbar = true; Topmost = true; Background = Brushes.Black;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/App.ico"));
        Width = 800; Height = 600; Cursor = Cursors.Cross;
        var canvas = new Canvas { Background = new ImageBrush(image) { Stretch = Stretch.Fill } };
        Content = canvas;
        var shade = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(65, 0, 0, 0)), IsHitTestVisible = false };
        canvas.Children.Add(shade);
        var outline = new Rectangle { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false };
        canvas.Children.Add(outline);
        var hint = new Border { Background = new SolidColorBrush(Color.FromRgb(25, 25, 25)), CornerRadius = new(6), Padding = new(14, 9, 14, 9), IsHitTestVisible = false,
            Child = new TextBlock { Text = "Drag an area. Release to save. Escape cancels.", Foreground = Brushes.White, FontSize = 16 } };
        canvas.Children.Add(hint); Canvas.SetLeft(hint, 20); Canvas.SetTop(hint, 20);
        canvas.SizeChanged += (_, _) => { shade.Width = canvas.ActualWidth; shade.Height = canvas.ActualHeight; };
        Point? start = null;
        void Update(Point point)
        {
            if (start is not { } origin || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
            int X(double x) => bounds.X + (int)Math.Round(Math.Clamp(x / canvas.ActualWidth, 0, 1) * bounds.Width);
            int Y(double y) => bounds.Y + (int)Math.Round(Math.Clamp(y / canvas.ActualHeight, 0, 1) * bounds.Height);
            Selection = bounds.Selection(X(origin.X), Y(origin.Y), X(point.X), Y(point.Y));
            if (Selection is not { } area) { outline.Visibility = Visibility.Collapsed; return; }
            outline.Visibility = Visibility.Visible;
            double sx = canvas.ActualWidth / bounds.Width, sy = canvas.ActualHeight / bounds.Height;
            Canvas.SetLeft(outline, area.X * sx); Canvas.SetTop(outline, area.Y * sy);
            outline.Width = area.Width * sx; outline.Height = area.Height * sy;
            ((TextBlock)hint.Child).Text = $"{area.Width} × {area.Height} px · release to save · Escape cancels";
        }
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(canvas); canvas.CaptureMouse(); Update(start.Value);
            e.Handled = true;
        };
        canvas.MouseMove += (_, e) => { if (canvas.IsMouseCaptured) Update(e.GetPosition(canvas)); };
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (canvas.IsMouseCaptured)
            {
                Update(e.GetPosition(canvas)); canvas.ReleaseMouseCapture(); start = null;
                if (Selection is not null) DialogResult = true;
            }
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Cancel(); } };
        ContentRendered += (_, _) =>
        {
            // Native physical geometry avoids WPF's work-area/maximized and DPI-unit conversions.
            if (!ProbeNative.SetWindowPos(new WindowInteropHelper(this).Handle, -1, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040))
            { Cancel(); return; }
            Activate(); Focus(); Keyboard.Focus(this);
        };
    }
    public void Cancel() { if (IsVisible) DialogResult = false; }
}
