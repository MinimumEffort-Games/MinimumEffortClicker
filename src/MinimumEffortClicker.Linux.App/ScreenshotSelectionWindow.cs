using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

internal sealed class ScreenshotSelectionWindow : DialogWindow
{
    public ScreenshotBounds? Selection { get; private set; }
    public ScreenshotSelectionWindow(Bitmap image, ScreenshotBounds bounds)
    {
        Title = "Select screenshot area";
        WindowDecorations = WindowDecorations.None; CanResize = false;
        // The main window is hidden, so keep this task reachable from the dash.
        ShowInTaskbar = true; Topmost = true; Background = Brushes.Black;
        // The bounds are in the desktop helper's units; with display scaling X11 pixels are larger by DesktopPixels.Factor.
        Width = bounds.Width * DesktopPixels.Factor / Math.Max(1, DesktopScaling); Height = bounds.Height * DesktopPixels.Factor / Math.Max(1, DesktopScaling);
        Cursor = new Cursor(StandardCursorType.Cross);
        var canvas = new Canvas { Background = new ImageBrush(image) { Stretch = Stretch.Fill } };
        Content = canvas;
        var shade = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(65, 0, 0, 0)), IsHitTestVisible = false };
        canvas.Children.Add(shade);
        var outline = new Rectangle { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false, IsVisible = false };
        canvas.Children.Add(outline);
        var hintText = new TextBlock { Text = "Drag an area. Release to save. Escape cancels.", Foreground = Brushes.White, FontSize = 16 };
        var hint = new Border { Background = new SolidColorBrush(Color.FromRgb(25, 25, 25)), CornerRadius = new(6), Padding = new(14, 9, 14, 9), IsHitTestVisible = false, Child = hintText };
        canvas.Children.Add(hint); Canvas.SetLeft(hint, 20); Canvas.SetTop(hint, 20);
        canvas.SizeChanged += (_, _) => { shade.Width = canvas.Bounds.Width; shade.Height = canvas.Bounds.Height; };
        Point? start = null;
        void Update(Point point)
        {
            if (start is not { } origin || canvas.Bounds.Width <= 0 || canvas.Bounds.Height <= 0) return;
            int X(double x) => bounds.X + (int)Math.Round(Math.Clamp(x / canvas.Bounds.Width, 0, 1) * bounds.Width);
            int Y(double y) => bounds.Y + (int)Math.Round(Math.Clamp(y / canvas.Bounds.Height, 0, 1) * bounds.Height);
            Selection = bounds.Selection(X(origin.X), Y(origin.Y), X(point.X), Y(point.Y));
            if (Selection is not { } area) { outline.IsVisible = false; return; }
            outline.IsVisible = true;
            double sx = canvas.Bounds.Width / bounds.Width, sy = canvas.Bounds.Height / bounds.Height;
            Canvas.SetLeft(outline, area.X * sx); Canvas.SetTop(outline, area.Y * sy);
            outline.Width = area.Width * sx; outline.Height = area.Height * sy;
            hintText.Text = $"{area.Width} × {area.Height} px · release to save · Escape cancels";
        }
        canvas.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(canvas); e.Pointer.Capture(canvas); Update(start.Value);
            e.Handled = true;
        };
        canvas.PointerMoved += (_, e) => { if (start is not null) Update(e.GetPosition(canvas)); };
        canvas.PointerReleased += (_, e) =>
        {
            if (start is not null)
            {
                Update(e.GetPosition(canvas)); e.Pointer.Capture(null); start = null;
                if (Selection is not null) DialogResult = true;
            }
            e.Handled = true;
        };
        Opened += (_, _) =>
        {
            Position = DesktopPixels.ToWindow(bounds.X, bounds.Y);
            // One monitor: full screen also covers GNOME's top bar. Several monitors keep the spanning window.
            if (Screens.ScreenCount == 1) WindowState = WindowState.FullScreen;
            Activate(); Focus();
        };
    }
    public void Cancel() { if (IsVisible) DialogResult = false; }
}
