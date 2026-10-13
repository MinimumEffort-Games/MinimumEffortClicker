using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

internal sealed class RegionPicker : DialogWindow
{
    private readonly WindowSnapshot snapshot;
    public CaptureRegion? Region { get; private set; }
    public int Color { get; private set; }
    public RegionPicker(Window owner, WindowSnapshot snapshot, TargetClientFrame frame, int maximumSide = 256)
    {
        this.snapshot = snapshot; OwnerWindow = owner; Title = "Select an area — drag on the snapshot";
        var work = owner.Screens.ScreenFromWindow(owner)?.WorkingArea;
        Width = Math.Min(980, (work?.Width ?? 1020) - 40); Height = Math.Min(720, (work?.Height ?? 760) - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { Margin = new Thickness(12) }; Content = grid;
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var hint = new TextBlock { Text = maximumSide == 1 ? "Click a pixel to sample its colour." : $"Drag a box (up to {maximumSide} × {maximumSide} pixels). Drag again to change it. No clicks reach your app.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) }; grid.Children.Add(hint);
        var bitmap = Imaging.FromBgra(snapshot.Pixels, snapshot.Width, snapshot.Height);
        var canvas = new Canvas { Width = snapshot.Width, Height = snapshot.Height, Background = new ImageBrush(bitmap), Cursor = new Cursor(StandardCursorType.Cross) };
        var view = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }; Grid.SetRow(view, 1); grid.Children.Add(view);
        var rectangle = new Rectangle { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Avalonia.Media.Color.FromArgb(50, 0, 180, 255)), IsHitTestVisible = false }; canvas.Children.Add(rectangle);
        Point? start = null;
        void Select(Point at)
        {
            if (start is not { } origin) return;
            int x = Math.Clamp((int)Math.Min(origin.X, at.X), 0, snapshot.Width - 1), y = Math.Clamp((int)Math.Min(origin.Y, at.Y), 0, snapshot.Height - 1);
            int width = Math.Clamp((int)Math.Abs(at.X - origin.X) + 1, 1, Math.Min(maximumSide, snapshot.Width - x));
            int height = Math.Clamp((int)Math.Abs(at.Y - origin.Y) + 1, 1, Math.Min(maximumSide, snapshot.Height - y));
            Region = new(new(x, y, frame.Width, frame.Height, frame.Dpi), width, height);
            int pixel = (y * snapshot.Width + x) * 4;
            Color = snapshot.Pixels[pixel + 2] << 16 | snapshot.Pixels[pixel + 1] << 8 | snapshot.Pixels[pixel];
            Canvas.SetLeft(rectangle, x); Canvas.SetTop(rectangle, y); rectangle.Width = width; rectangle.Height = height;
            hint.Text = $"Selected: {x}, {y} · {width} × {height} px · colour at top-left #{Color:X6}. Drag again to change it.";
        }
        canvas.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(canvas); e.Pointer.Capture(canvas); Select(start.Value); e.Handled = true;
        };
        canvas.PointerMoved += (_, e) => { if (start is not null) Select(e.GetPosition(canvas)); };
        canvas.PointerReleased += (_, e) => { Select(e.GetPosition(canvas)); e.Pointer.Capture(null); start = null; e.Handled = true; };
        var footer = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(footer, 2); grid.Children.Add(footer);
        var accept = new Button { Content = maximumSide == 1 ? "Use this point" : "Use this area", Padding = new Thickness(16, 8, 16, 8) };
        accept.Click += (_, _) => { if (Region is not null) DialogResult = true; else hint.Text = "Select a point or drag an area first."; };
        var cancel = CancelButton(); cancel.Padding = new Thickness(16, 8, 16, 8); cancel.Margin = new Thickness(8, 0, 0, 0);
        footer.Children.Add(accept); footer.Children.Add(cancel);
    }
    public RasterSample SelectedPixels()
    {
        var region = Region ?? throw new InvalidOperationException("Select an area first.");
        byte[] pixels = new byte[region.Width * region.Height * 4];
        for (int y = 0; y < region.Height; y++)
            Array.Copy(snapshot.Pixels, ((region.Origin.Y + y) * snapshot.Width + region.Origin.X) * 4, pixels, y * region.Width * 4, region.Width * 4);
        return new(region.Width, region.Height, pixels);
    }
}
