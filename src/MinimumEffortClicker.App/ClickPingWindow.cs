using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

/// <summary>
/// A growing ring at a physical screen point: green once where a tested click landed, red a few times for Ping click
/// location. Click-through and never focused, so it cannot receive or redirect input, and the app stays usable meanwhile.
/// </summary>
internal sealed class ClickPingWindow : Window
{
    private const int SizePixels = 120;
    public static readonly Color Landed = Color.FromRgb(0x3D, 0xDC, 0x84), Location = Color.FromRgb(0xE5, 0x39, 0x35);

    public ClickPingWindow(int screenX, int screenY, Color colour, int times = 1)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        Focusable = false; IsHitTestVisible = false; Width = SizePixels; Height = SizePixels;
        // A dark ring under a bright one stays visible on light and dark backgrounds.
        var scale = new ScaleTransform(0.15, 0.15);
        var rings = new Grid { Width = 100, Height = 100, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale };
        rings.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), StrokeThickness = 9 });
        rings.Children.Add(new Ellipse { Stroke = new SolidColorBrush(colour), StrokeThickness = 5, Margin = new Thickness(2) });
        // The Viewbox keeps the ring proportional when the window is sized in physical pixels on a scaled display.
        Content = new Viewbox { Child = rings };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            long style = ProbeNative.GetWindowLongPtr(handle, -20).ToInt64();
            // WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: visible, click-through, never focused.
            ProbeNative.SetWindowLongPtr(handle, -20, (nint)(style | 0x80000 | 0x20 | 0x08000000 | 0x80));
            // SWP_NOACTIVATE; the position is in physical pixels, centred on the click.
            ProbeNative.SetWindowPos(handle, -1, screenX - SizePixels / 2, screenY - SizePixels / 2, SizePixels, SizePixels, 0x10);
        };
        Loaded += (_, _) =>
        {
            // Grows with an ease-out and fades with an ease-in over one second, once per time asked.
            var duration = TimeSpan.FromSeconds(1);
            var repeat = new RepeatBehavior(Math.Max(1, times));
            var grow = new DoubleAnimation(0.15, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, RepeatBehavior = repeat };
            var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }, RepeatBehavior = repeat };
            fade.Completed += (_, _) => Close();
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            rings.BeginAnimation(OpacityProperty, fade);
        };
    }

    /// <summary>Shows the ring at a physical screen point; failures are ignored because the ring is only a hint.</summary>
    public static void At(int screenX, int screenY, Color colour, int times = 1)
    {
        try { new ClickPingWindow(screenX, screenY, colour, times).Show(); }
        catch (InvalidOperationException) { }
    }

    /// <summary>Shows a green ring at the current pointer position, for a click at the cursor.</summary>
    public static void AtCursor()
    {
        if (ProbeNative.GetCursorPos(out var point)) At(point.X, point.Y, Landed);
    }
}
