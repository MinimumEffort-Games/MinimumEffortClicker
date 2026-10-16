using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Vision;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

/// <summary>
/// Pick a pixel or drag an area on a still picture: of the Click target window, or in Desktop mode of a chosen monitor.
/// Window areas are stored relative to the window; monitor areas in absolute screen pixels.
/// </summary>
internal sealed class RegionPicker : Window
{
    // The monitor picked last, so the next pick opens on the same one.
    private static int lastMonitor = 1;
    private WindowSnapshot snapshot;
    private readonly TargetClientFrame frame;
    private readonly int maximumSide;
    private readonly Canvas canvas = new() { Cursor = Cursors.Cross };
    private readonly Rectangle rectangle = new() { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 0, 180, 255)), IsHitTestVisible = false };
    private readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private DesktopMonitor? monitor;
    public CaptureRegion? Region { get; private set; }
    public int Color { get; private set; }

    /// <summary>A picture of the Click target window.</summary>
    public RegionPicker(Window owner, WindowSnapshot snapshot, TargetClientFrame frame, int maximumSide = 256)
        : this(owner, frame, maximumSide, null, [snapshot]) { }

    /// <summary>Desktop mode: one picture per monitor, with a dropdown to switch between them.</summary>
    public RegionPicker(Window owner, IReadOnlyList<DesktopMonitor> monitors, IReadOnlyList<WindowSnapshot> pictures, TargetClientFrame frame, int maximumSide = 256)
        : this(owner, frame, maximumSide, monitors, pictures) { }

    private RegionPicker(Window owner, TargetClientFrame frame, int maximumSide, IReadOnlyList<DesktopMonitor>? monitors, IReadOnlyList<WindowSnapshot> pictures)
    {
        this.frame = frame; this.maximumSide = maximumSide; snapshot = pictures[0];
        Owner = owner; Title = maximumSide == 1 ? "Pick a colour — click a pixel" : "Select an area — drag on the picture";
        Width = Math.Min(980, SystemParameters.WorkArea.Width - 40); Height = Math.Min(720, SystemParameters.WorkArea.Height - 40);
        // Opens maximised, on the monitor the clicker is on, so the picture is big enough to drag on.
        // Windows maximises a window on the monitor its restore position lies on, and CenterOwner is only applied after
        // that, so the restore position is put in the middle of the clicker by hand first.
        var ownerBounds = owner.WindowState == WindowState.Normal ? new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight) : owner.RestoreBounds;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = ownerBounds.Left + (ownerBounds.Width - Width) / 2; Top = ownerBounds.Top + (ownerBounds.Height - Height) / 2;
        WindowState = WindowState.Maximized;
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "TextPrimary");
        foreach (var type in new[] { typeof(Button), typeof(ComboBox) })
            if (owner.TryFindResource(type) is Style style) Resources[type] = style;
        var grid = new Grid { Margin = new Thickness(12) }; Content = grid;
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); grid.Children.Add(header);
        if (monitors is { Count: > 0 })
        {
            var choose = new ComboBox { ItemsSource = monitors, MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            System.Windows.Automation.AutomationProperties.SetName(choose, "Monitor");
            choose.SelectionChanged += (_, _) =>
            {
                if (choose.SelectedIndex < 0) return;
                monitor = monitors[choose.SelectedIndex]; lastMonitor = monitor.Number;
                ShowPicture(pictures[choose.SelectedIndex]);
            };
            header.Children.Add(new TextBlock { Text = "Monitor", Margin = new Thickness(0, 0, 0, 4) });
            header.Children.Add(choose);
            int start = monitors.ToList().FindIndex(m => m.Number == lastMonitor);
            choose.SelectedIndex = start >= 0 ? start : 0;
        }
        header.Children.Add(hint);
        canvas.Children.Add(rectangle);
        var view = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }; Grid.SetRow(view, 1); grid.Children.Add(view);
        if (monitor is null) ShowPicture(snapshot);
        Point? start2 = null;
        canvas.MouseLeftButtonDown += (_, e) => { start2 = e.GetPosition(canvas); canvas.CaptureMouse(); Select(start2.Value, start2.Value); e.Handled = true; };
        canvas.MouseMove += (_, e) => { if (canvas.IsMouseCaptured && start2 is { } origin) Select(origin, e.GetPosition(canvas)); };
        canvas.MouseLeftButtonUp += (_, e) => { if (start2 is { } origin) Select(origin, e.GetPosition(canvas)); canvas.ReleaseMouseCapture(); start2 = null; e.Handled = true; };
        var footer = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(footer, 2); grid.Children.Add(footer);
        var accept = new Button { Content = maximumSide == 1 ? "Use this point" : "Use this area", Padding = new Thickness(16, 8, 16, 8) };
        accept.Click += (_, _) => { if (Region is not null) DialogResult = true; else hint.Text = "Select a point or drag an area first."; };
        footer.Children.Add(accept); footer.Children.Add(new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0) });
    }

    private void ShowPicture(WindowSnapshot picture)
    {
        snapshot = picture; Region = null; rectangle.Width = rectangle.Height = 0;
        var bitmap = BitmapSource.Create(picture.Width, picture.Height, 96, 96, PixelFormats.Bgra32, null, picture.Pixels, picture.Width * 4);
        canvas.Width = picture.Width; canvas.Height = picture.Height; canvas.Background = new ImageBrush(bitmap);
        hint.Text = maximumSide == 1 ? "Click a pixel to sample its colour." : $"Drag a box (up to {maximumSide} × {maximumSide} pixels). Drag again to change it. No clicks reach your app.";
    }

    private void Select(Point origin, Point at)
    {
        int x = Math.Clamp((int)Math.Min(origin.X, at.X), 0, snapshot.Width - 1), y = Math.Clamp((int)Math.Min(origin.Y, at.Y), 0, snapshot.Height - 1);
        int width = Math.Clamp((int)Math.Abs(at.X - origin.X) + 1, 1, Math.Min(maximumSide, snapshot.Width - x));
        int height = Math.Clamp((int)Math.Abs(at.Y - origin.Y) + 1, 1, Math.Min(maximumSide, snapshot.Height - y));
        // A monitor area is stored in absolute screen pixels, calibrated to the whole monitor layout.
        Region = monitor is { } screen
            ? new(new(screen.X + x, screen.Y + y, frame.Width, frame.Height, frame.Dpi, CoordinateSpace.Desktop), width, height)
            : new(new(x, y, frame.Width, frame.Height, frame.Dpi), width, height);
        int pixel = (y * snapshot.Width + x) * 4;
        Color = snapshot.Pixels[pixel + 2] << 16 | snapshot.Pixels[pixel + 1] << 8 | snapshot.Pixels[pixel];
        Canvas.SetLeft(rectangle, x); Canvas.SetTop(rectangle, y); rectangle.Width = width; rectangle.Height = height;
        string where = monitor is { } m ? $"Monitor {m.Number} at {x}, {y}" : $"{x}, {y}";
        hint.Text = $"Selected: {where} · {width} × {height} px · colour at top-left #{Color:X6}. Drag again to change it.";
    }
}
