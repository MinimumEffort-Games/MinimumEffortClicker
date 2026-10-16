using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

/// <summary>
/// Pick a pixel or drag an area on a still picture: of the Click target window, or in Desktop mode of a chosen monitor.
/// Window areas are stored relative to the window; monitor areas in absolute desktop pixels.
/// </summary>
internal sealed class RegionPicker : DialogWindow
{
    // The monitor picked last, so the next pick opens on the same one.
    private static int lastMonitor = 1;
    private WindowSnapshot snapshot;
    private readonly TargetClientFrame frame;
    private readonly int maximumSide;
    private readonly Canvas canvas = new() { Cursor = new Cursor(StandardCursorType.Cross) };
    private readonly Rectangle rectangle = new() { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Avalonia.Media.Color.FromArgb(50, 0, 180, 255)), IsHitTestVisible = false };
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
        OwnerWindow = owner; Title = maximumSide == 1 ? "Pick a colour — click a pixel" : "Select an area — drag on the picture";
        var screen = owner.Screens.ScreenFromWindow(owner);
        var work = screen?.WorkingArea;
        Width = Math.Min(980, (work?.Width ?? 1020) - 40); Height = Math.Min(720, (work?.Height ?? 760) - 40);
        // User (2026-10-16): opens maximised, on the monitor the clicker is on, so the picture is big enough to drag on.
        // The window manager maximises a window on the monitor it is placed on, so it is placed on the clicker's monitor first.
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (work is { } area) Position = new PixelPoint(area.X + 20, area.Y + 20);
        WindowState = WindowState.Maximized;
        var grid = new Grid { Margin = new Thickness(12) }; Content = grid;
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); grid.Children.Add(header);
        if (monitors is { Count: > 0 })
        {
            var choose = new ComboBox { ItemsSource = monitors, MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            Avalonia.Automation.AutomationProperties.SetName(choose, "Monitor");
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
        Point? origin = null;
        canvas.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            origin = e.GetPosition(canvas); e.Pointer.Capture(canvas); Select(origin.Value, origin.Value); e.Handled = true;
        };
        canvas.PointerMoved += (_, e) => { if (origin is { } from) Select(from, e.GetPosition(canvas)); };
        canvas.PointerReleased += (_, e) => { if (origin is { } from) Select(from, e.GetPosition(canvas)); e.Pointer.Capture(null); origin = null; e.Handled = true; };
        var footer = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(footer, 2); grid.Children.Add(footer);
        var accept = new Button { Content = maximumSide == 1 ? "Use this point" : "Use this area", Padding = new Thickness(16, 8, 16, 8) };
        accept.Click += (_, _) => { if (Region is not null) DialogResult = true; else hint.Text = "Select a point or drag an area first."; };
        var cancel = CancelButton(); cancel.Padding = new Thickness(16, 8, 16, 8); cancel.Margin = new Thickness(8, 0, 0, 0);
        footer.Children.Add(accept); footer.Children.Add(cancel);
    }

    private void ShowPicture(WindowSnapshot picture)
    {
        snapshot = picture; Region = null; rectangle.Width = rectangle.Height = 0;
        canvas.Width = picture.Width; canvas.Height = picture.Height;
        canvas.Background = new ImageBrush(Imaging.FromBgra(picture.Pixels, picture.Width, picture.Height));
        hint.Text = maximumSide == 1 ? "Click a pixel to sample its colour." : $"Drag a box (up to {maximumSide} × {maximumSide} pixels). Drag again to change it. No clicks reach your app.";
    }

    private void Select(Point origin, Point at)
    {
        int x = Math.Clamp((int)Math.Min(origin.X, at.X), 0, snapshot.Width - 1), y = Math.Clamp((int)Math.Min(origin.Y, at.Y), 0, snapshot.Height - 1);
        int width = Math.Clamp((int)Math.Abs(at.X - origin.X) + 1, 1, Math.Min(maximumSide, snapshot.Width - x));
        int height = Math.Clamp((int)Math.Abs(at.Y - origin.Y) + 1, 1, Math.Min(maximumSide, snapshot.Height - y));
        // A monitor area is stored in absolute desktop pixels, calibrated to the whole monitor layout.
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
