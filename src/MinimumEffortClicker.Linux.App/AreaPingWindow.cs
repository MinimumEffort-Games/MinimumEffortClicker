using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

/// <summary>
/// A pulsing outline around the area a colour wait watches: red a few times for Ping scan area, green or red once after
/// testing the wait (colour there or not). Click-through and never focused, like the click ring, and named like the
/// overlay so the clicker's own hit tests and captures look through it.
/// </summary>
internal sealed class AreaPingWindow : Window
{
    // A small gap keeps the outline itself off the watched pixels.
    private const int Gap = 6;

    public AreaPingWindow(int left, int top, int width, int height, Color colour, int times = 1)
    {
        Title = DesktopRuntime.OverlayTitle; WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; CanResize = false; Focusable = false; IsHitTestVisible = false;
        Width = width + 2 * Gap; Height = height + 2 * Gap;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(left - Gap, top - Gap);
        // A dark outline under a bright one stays visible on light and dark backgrounds.
        var outline = new Grid();
        outline.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), BorderThickness = new Thickness(5), CornerRadius = new CornerRadius(4) });
        outline.Children.Add(new Border { BorderBrush = new SolidColorBrush(colour), BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4), Margin = new Thickness(1) });
        Content = outline;
        Opened += (_, _) =>
        {
            X11Windows.SetNoFocus(this, true); X11Windows.SetClickThrough(this, true); X11Windows.SetAbove(this);
            var started = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                double elapsed = (DateTime.UtcNow - started) / ClickPingWindow.Pulse; // in pulses
                double t = elapsed - Math.Floor(elapsed);
                // Fades out and back in once per pulse, with the same length as the click ring's pulse.
                outline.Opacity = 0.15 + 0.85 * Math.Abs(1 - 2 * t);
                if (elapsed >= times) { timer.Stop(); Close(); }
            };
            timer.Start();
        };
    }

    /// <summary>Shows the outline around a desktop rectangle; failures are ignored because it is only a hint.</summary>
    public static void Around(int left, int top, int width, int height, Color colour, int times = 1)
    {
        try { new AreaPingWindow(left, top, width, height, colour, times).Show(); }
        catch (InvalidOperationException) { }
    }
}
