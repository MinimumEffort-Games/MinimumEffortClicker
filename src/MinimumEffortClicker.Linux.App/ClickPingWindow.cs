using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

/// <summary>
/// A one-second ring at a desktop point, so the user sees where a tested click landed.
/// Click-through and never focused (empty X11 input region, no-input hint), so it cannot receive or redirect input.
/// </summary>
internal sealed class ClickPingWindow : Window
{
    private const int SizePixels = 120;

    public ClickPingWindow(int desktopX, int desktopY)
    {
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
        rings.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x84)), StrokeThickness = 5, Margin = new Thickness(2) });
        Content = new Viewbox { Child = rings };
        Opened += (_, _) =>
        {
            X11Windows.SetNoFocus(this, true); X11Windows.SetClickThrough(this, true);
            var started = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                double t = Math.Min(1, (DateTime.UtcNow - started).TotalSeconds);
                // Grows with an ease-out and fades with an ease-in over one second.
                double grow = 0.15 + 0.85 * (1 - Math.Pow(1 - t, 3));
                scale.ScaleX = scale.ScaleY = grow;
                rings.Opacity = 1 - t * t;
                if (t >= 1) { timer.Stop(); Close(); }
            };
            timer.Start();
        };
    }

    /// <summary>Shows a ring at a desktop point; failures are ignored because the ring is only a hint.</summary>
    public static void At(int desktopX, int desktopY)
    {
        try { new ClickPingWindow(desktopX, desktopY).Show(); }
        catch (Exception) { }
    }
}
