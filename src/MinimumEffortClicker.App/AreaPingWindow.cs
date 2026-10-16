using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

/// <summary>
/// A pulsing outline around the area a colour wait watches: red a few times for Ping scan area, green or red once after
/// testing the wait (colour there or not). Click-through and never focused, like the click ring.
/// </summary>
internal sealed class AreaPingWindow : Window
{
    // A small margin keeps the outline itself off the watched pixels.
    private const int Gap = 6;

    public AreaPingWindow(int left, int top, int width, int height, Color colour, int times = 1)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        Focusable = false; IsHitTestVisible = false;
        // A dark outline under a bright one stays visible on light and dark backgrounds.
        var outline = new Grid();
        outline.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), BorderThickness = new Thickness(5), CornerRadius = new CornerRadius(4) });
        outline.Children.Add(new Border { BorderBrush = new SolidColorBrush(colour), BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4), Margin = new Thickness(1) });
        Content = outline;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            long style = ProbeNative.GetWindowLongPtr(handle, -20).ToInt64();
            // WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: visible, click-through, never focused.
            ProbeNative.SetWindowLongPtr(handle, -20, (nint)(style | 0x80000 | 0x20 | 0x08000000 | 0x80));
            // SWP_NOACTIVATE; physical pixels, so the outline matches the area on scaled displays too.
            ProbeNative.SetWindowPos(handle, -1, left - Gap, top - Gap, width + 2 * Gap, height + 2 * Gap, 0x10);
        };
        Loaded += (_, _) =>
        {
            // Fades in and out once per pulse, with the same length as the click ring's pulse.
            var fade = new DoubleAnimation(1, 0.15, new Duration(ClickPingWindow.Pulse / 2)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(Math.Max(1, times)) };
            fade.Completed += (_, _) => Close();
            outline.BeginAnimation(OpacityProperty, fade);
        };
    }

    /// <summary>Shows the outline around a physical screen rectangle; failures are ignored because it is only a hint.</summary>
    public static void Around(int left, int top, int width, int height, Color colour, int times = 1)
    {
        try { new AreaPingWindow(left, top, width, height, colour, times).Show(); }
        catch (InvalidOperationException) { }
    }
}
