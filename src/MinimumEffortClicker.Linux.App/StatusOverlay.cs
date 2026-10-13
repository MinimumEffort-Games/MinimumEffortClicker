using Avalonia;
using Avalonia.Controls;
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
/// Always-on-top status window. Locked, it is click-through (an empty X11 input region) and the
/// runtime's hit tests look through it by title; unlocked, it can be dragged.
/// </summary>
internal sealed class StatusOverlay : Window, IOverlayPresenter
{
    private static readonly IBrush Idle = new SolidColorBrush(Color.FromRgb(17, 16, 16)), IdleBorder = new SolidColorBrush(Color.FromRgb(163, 158, 147));
    private static readonly IBrush Active = new SolidColorBrush(Color.FromRgb(28, 96, 50));
    private readonly TextBlock label = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock activityLabel = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 40 };
    private readonly StackPanel controls = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel body = new();
    private readonly LayoutTransformControl scaler = new();
    private readonly Border frame;
    private OverlayOptions options = new();
    private bool configured;
    private bool recording, active;
    public bool IsEditing { get; private set; }
    public event Action? LockRequested;
    public event Action? StopRequested;
    public event Action<int, int>? PlacementChanged;

    public StatusOverlay()
    {
        Title = DesktopRuntime.OverlayTitle; SizeToContent = SizeToContent.WidthAndHeight; MaxWidth = 580;
        WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; CanResize = false; Focusable = false;
        body.Children.Add(activityLabel); body.Children.Add(label); body.Children.Add(controls);
        var stop = new Button { Content = "Stop all", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        var finish = new Button { Content = "Lock overlay", Padding = new Thickness(10, 5, 10, 5) };
        stop.Click += (_, _) => StopRequested?.Invoke(); finish.Click += (_, _) => LockRequested?.Invoke();
        controls.Children.Add(stop); controls.Children.Add(finish); controls.IsVisible = false;
        frame = new Border { Background = Idle, BorderBrush = IdleBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 4, 9, 4), Child = body };
        scaler.Child = frame;
        Content = scaler;
        ApplyColours();
        body.PointerPressed += (_, e) =>
        {
            if (!IsEditing || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);
        };
        PositionChanged += (_, e) => { if (IsEditing) PlacementChanged?.Invoke(e.Point.X, e.Point.Y); };
        Opened += (_, _) => ApplyStyles();
    }

    private void ApplyColours()
    {
        frame.Background = recording ? Brushes.DarkRed : active ? Active : Idle;
        frame.BorderBrush = recording ? Brushes.IndianRed : active ? Brushes.MediumSeaGreen : IdleBorder;
        Opacity = options.Opacity;
    }

    private void ApplyStyles()
    {
        X11Windows.SetClickThrough(this, !IsEditing);
        X11Windows.SetNoFocus(this, !IsEditing);
    }

    public void SetEditing(bool editing)
    {
        IsEditing = editing; Focusable = editing;
        ShowInTaskbar = editing;
        controls.IsVisible = editing;
        body.Cursor = editing ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
        ApplyStyles();
        if (editing) Activate();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (IsEditing) { e.Cancel = true; Dispatcher.Post(() => LockRequested?.Invoke()); }
        base.OnClosing(e);
    }

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
        bool toggleOnly = toggles?.Count > 0 && !IsEditing;
        if (!state.Busy && !state.CleanupPending && !IsEditing)
        {
            activityLabel.Text = (actionsEmpty ? "Actionlist Empty" : $"{startKey} to start/stop")
                + (toggles?.Count > 0 ? "\n" + string.Join("\n", toggles) : "");
            ToolTip.SetTip(activityLabel, activityLabel.Text);
            label.IsVisible = false;
            return;
        }
        activityLabel.MaxHeight = double.PositiveInfinity;
        activityLabel.Text = IsEditing ? "Drag to move overlay" : toggleOnly ? (state.CleanupPending ? "Input release pending\n" : state.ActionsActive ? $"{state.State} · {state.Activity}\n" : "") + string.Join("\n", toggles!)
            : $"{state.State} · {(state.Activity is "Minimum Effort" or "Named points" ? "Actions" : state.Activity)}";
        ToolTip.SetTip(activityLabel, activityLabel.Text);
        label.IsVisible = !(toggleOnly && !state.ActionsActive && !state.CleanupPending);
        label.Text = IsEditing ? "Playback stopped" : state.CleanupPending ? $"Stop all: {stopKey}" : $"{startKey} to start/stop";
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
