using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

/// <summary>
/// A growing ring at a desktop point: green once where a tested click landed, red a few times for Ping location.
/// Click-through and never focused (empty X11 input region, no-input hint), so it cannot receive or redirect input.
/// </summary>
internal sealed class ClickPingWindow : Window
{
    private const int SizePixels = 120;
    public static readonly Color Landed = Color.FromRgb(0x3D, 0xDC, 0x84), Location = Color.FromRgb(0xE5, 0x39, 0x35);
    private readonly int desktopX, desktopY;
    private readonly TranslateTransform shift = new();

    /// <summary>How long one grow-and-fade of the ring takes; Ping location waits for its pulses before the clicker comes back.</summary>
    public static readonly TimeSpan Pulse = TimeSpan.FromSeconds(1);

    public ClickPingWindow(int desktopX, int desktopY, Color colour, int times = 1)
    {
        this.desktopX = desktopX; this.desktopY = desktopY;
        Title = DesktopRuntime.OverlayTitle; WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; CanResize = false; Focusable = false; IsHitTestVisible = false;
        Width = SizePixels; Height = SizePixels;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(desktopX - SizePixels / 2, desktopY - SizePixels / 2);
        // A dark ring under a bright one stays visible on light and dark backgrounds.
        var scale = new ScaleTransform(0.15, 0.15);
        var rings = new Grid { Width = 100, Height = 100, RenderTransformOrigin = RelativePoint.Center, RenderTransform = scale };
        rings.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), StrokeThickness = 9 });
        rings.Children.Add(new Ellipse { Stroke = new SolidColorBrush(colour), StrokeThickness = 5, Margin = new Thickness(2) });
        // GNOME keeps a window below the top bar and on screen, so near an edge the window may sit elsewhere than asked;
        // the ring is then shifted inside it to stay centred on the point (the part beyond the window is cut off).
        Content = new Viewbox { Child = rings, RenderTransform = shift };
        Opened += (_, _) =>
        {
            X11Windows.SetNoFocus(this, true); X11Windows.SetClickThrough(this, true); X11Windows.SetAbove(this);
            Centre();
            var started = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                FollowPoint();
                double elapsed = (DateTime.UtcNow - started) / Pulse; // in pulses
                double t = Math.Min(1, elapsed - Math.Floor(Math.Min(elapsed, times - 0.001)));
                // Grows with an ease-out and fades with an ease-in over one second.
                double grow = 0.15 + 0.85 * (1 - Math.Pow(1 - t, 3));
                scale.ScaleX = scale.ScaleY = grow;
                rings.Opacity = 1 - t * t;
                if (elapsed >= times) { timer.Stop(); Close(); }
            };
            timer.Start();
        };
    }

    /// <summary>
    /// GNOME may place a new window itself, and the window's size in pixels is known only once it is open, so the
    /// centre is put on the point again after showing.
    /// </summary>
    private void Centre()
    {
        var size = PixelSize.FromSize(FrameSize ?? Bounds.Size, DesktopScaling);
        if (size.Width <= 0) size = new PixelSize(SizePixels, SizePixels);
        Position = new PixelPoint(desktopX - size.Width / 2, desktopY - size.Height / 2);
    }

    /// <summary>Moves the drawing so the ring's centre is on the point wherever GNOME put the window.</summary>
    private void FollowPoint()
    {
        var size = PixelSize.FromSize(FrameSize ?? Bounds.Size, DesktopScaling);
        double scale = DesktopScaling > 0 ? DesktopScaling : 1;
        shift.X = (desktopX - size.Width / 2 - Position.X) / scale;
        shift.Y = (desktopY - size.Height / 2 - Position.Y) / scale;
    }

    /// <summary>Shows the ring at a desktop point; failures are ignored because the ring is only a hint.</summary>
    public static void At(int desktopX, int desktopY, Color colour, int times = 1)
    {
        try { var ring = new ClickPingWindow(desktopX, desktopY, colour, times); ring.Show(); ring.Centre(); }
        catch (Exception) { }
    }
}
