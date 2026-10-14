using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

internal interface IOverlayPresenter
{
    void Update(RuntimeSnapshot state, string stopKey, string startKey = "F8", IReadOnlyList<string>? toggles = null, string recordKey = "F10", bool actionsEmpty = false);
    void Configure(OverlayOptions options);
}

/// <summary>
/// Always-on-top status window. It is click-through (an empty X11 input region), never takes focus, and the
/// runtime's hit tests look through it by title.
/// </summary>
internal sealed class StatusOverlay : Window, IOverlayPresenter
{
    private static readonly IBrush Idle = new SolidColorBrush(Color.FromRgb(17, 16, 16)), IdleBorder = new SolidColorBrush(Color.FromRgb(163, 158, 147));
    private static readonly IBrush Active = new SolidColorBrush(Color.FromRgb(28, 96, 50));
    private readonly TextBlock label = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock activityLabel = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 40 };
    private readonly StackPanel body = new();
    private readonly LayoutTransformControl scaler = new();
    private readonly Border frame;
    private OverlayOptions options = new();
    private bool configured;
    private bool recording, active;
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private PixelRect[] clickable = [];

    /// <summary>The overlay's square: bring the clicker window to the front.</summary>
    public event Action? ShowRequested;
    /// <summary>The overlay's X: stop whatever runs and quit the clicker.</summary>
    public event Action? ExitRequested;

    public StatusOverlay()
    {
        Title = DesktopRuntime.OverlayTitle; SizeToContent = SizeToContent.WidthAndHeight; MaxWidth = 580;
        WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; CanResize = false; Focusable = false;
        body.Children.Add(activityLabel); body.Children.Add(label);
        // On GNOME the clicker has no taskbar entry while it is hidden or minimised, so the overlay offers a way back and out.
        actions.Children.Add(OverlayButton("Show Minimum Effort Clicker", new Rectangle { Width = 9, Height = 9, Stroke = Brushes.White, StrokeThickness = 1.5 }, () => ShowRequested?.Invoke()));
        actions.Children.Add(OverlayButton("Quit Minimum Effort Clicker (stops anything running)", new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,0 L9,9 M9,0 L0,9"), Stroke = Brushes.White, StrokeThickness = 1.5, Width = 9, Height = 9 }, () => ExitRequested?.Invoke()));
        var row = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions); row.Children.Add(body);
        frame = new Border { Background = Idle, BorderBrush = IdleBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 4, 6, 4), Child = row };
        scaler.Child = frame;
        Content = scaler;
        ApplyColours();
        Opened += (_, _) => ApplyStyles();
        LayoutUpdated += (_, _) => UpdateClickable();
    }

    private static Border OverlayButton(string tip, Control glyph, Action click)
    {
        var normal = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)); var hover = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
        var button = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(3), Background = normal, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Viewbox { Width = 11, Height = 11, Child = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.PointerEntered += (_, _) => button.Background = hover;
        button.PointerExited += (_, _) => button.Background = normal;
        button.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) { e.Handled = true; click(); } };
        return button;
    }

    /// <summary>Clicks reach only the two buttons; the rest of the overlay stays click-through.</summary>
    private void UpdateClickable()
    {
        double scale = RenderScaling;
        var areas = actions.Children.Select(c => c.TranslatePoint(new Point(0, 0), this) is { } p
            ? new PixelRect((int)Math.Floor(p.X * scale), (int)Math.Floor(p.Y * scale), (int)Math.Ceiling(c.Bounds.Width * scale), (int)Math.Ceiling(c.Bounds.Height * scale))
            : default).Where(r => r.Width > 0).ToArray();
        if (areas.SequenceEqual(clickable)) return;
        clickable = areas;
        X11Windows.SetInputRegion(this, areas);
    }

    private void ApplyColours()
    {
        frame.Background = recording ? Brushes.DarkRed : active ? Active : Idle;
        frame.BorderBrush = recording ? Brushes.IndianRed : active ? Brushes.MediumSeaGreen : IdleBorder;
        Opacity = options.Opacity;
    }

    private void ApplyStyles()
    {
        clickable = [];
        UpdateClickable();
        X11Windows.SetNoFocus(this, true);
        X11Windows.SetAbove(this);
    }

    /// <summary>GNOME forgets "always on top" when a window is hidden, so it is asked again each time the overlay shows.</summary>
    public void KeepAbove() => Avalonia.Threading.Dispatcher.UIThread.Post(() => X11Windows.SetAbove(this), Avalonia.Threading.DispatcherPriority.Background);

    public void Configure(OverlayOptions value)
    {
        if (!value.Valid) throw new ArgumentException("Invalid overlay preferences.");
        if (configured && value == options) return;
        configured = true;
        options = value; Opacity = value.Opacity;
        MaxWidth = 580 * value.Scale;
        scaler.LayoutTransform = new ScaleTransform(value.Scale, value.Scale);
    }

    public void Update(RuntimeSnapshot state, string stopKey, string startKey = "F8", IReadOnlyList<string>? toggles = null, string recordKey = "F10", bool actionsEmpty = false)
    {
        bool capturing = state.Busy && state.State.StartsWith("Recording", StringComparison.Ordinal);
        bool running = state.PlaybackActive && !state.CleanupPending;
        if (capturing != recording || running != active) { recording = capturing; active = running; ApplyColours(); }
        if (capturing)
        {
            activityLabel.Text = state.State;
            ToolTip.SetTip(activityLabel, state.Reason);
            label.IsVisible = true;
            label.Text = $"{recordKey} · Stop recording · {stopKey} · Stop all";
            return;
        }
        bool toggleOnly = toggles?.Count > 0;
        if (!state.Busy && !state.CleanupPending)
        {
            activityLabel.Text = (actionsEmpty ? "Actionlist Empty" : $"{startKey} to start/stop")
                + (toggles?.Count > 0 ? "\n" + string.Join("\n", toggles) : "");
            ToolTip.SetTip(activityLabel, activityLabel.Text);
            label.IsVisible = false;
            return;
        }
        activityLabel.MaxHeight = double.PositiveInfinity;
        activityLabel.Text = toggleOnly ? (state.CleanupPending ? "Input release pending\n" : state.ActionsActive ? $"{state.State} · {state.Activity}\n" : "") + string.Join("\n", toggles!)
            : $"{state.State} · {(state.Activity is "Minimum Effort" or "Named points" ? "Actions" : state.Activity)}";
        ToolTip.SetTip(activityLabel, activityLabel.Text);
        label.IsVisible = !(toggleOnly && !state.ActionsActive && !state.CleanupPending);
        label.Text = state.CleanupPending ? $"Stop all: {stopKey}" : $"{startKey} to start/stop";
    }

    private PixelSize PixelFrame
    {
        get
        {
            var size = FrameSize ?? Bounds.Size;
            return PixelSize.FromSize(size, DesktopScaling);
        }
    }

    public void PlaceAt(double right, double top)
    {
        var size = PixelFrame;
        PlaceWithinMonitor((int)right - size.Width - 12, (int)top + 12, size.Width, size.Height);
    }
    public void Constrain()
    {
        var size = PixelFrame;
        PlaceWithinMonitor(Position.X, Position.Y, size.Width, size.Height);
    }
    private void PlaceWithinMonitor(int x, int y, int width, int height)
    {
        var screen = Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? Screens.Primary;
        if (screen is null) return;
        var work = screen.WorkingArea;
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - width));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - height));
        if (Position.X != x || Position.Y != y) Position = new PixelPoint(x, y);
        Topmost = true;
    }
}
