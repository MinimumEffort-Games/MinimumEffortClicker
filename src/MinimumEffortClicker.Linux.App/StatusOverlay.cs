using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Linux;

namespace MinimumEffortClicker.App;

internal interface IOverlayPresenter
{
    void Update(RuntimeSnapshot state, string stopKey, string startKey = "F8", IReadOnlyList<OverlayToggle>? toggles = null, string recordKey = "F10", bool actionsEmpty = false, string? alert = null);
    void Configure(OverlayOptions options);
}

/// <summary>
/// Always-on-top status window pinned to the Click target at the anchor chosen in Settings. Locked, it is
/// click-through (an X11 input region that covers only its three buttons: lock, show, quit); unlocked, all of it takes
/// the mouse so it can be dragged. It never takes focus, and the runtime's hit tests look through it by title.
/// </summary>
internal sealed class StatusOverlay : Window, IOverlayPresenter
{
    private static readonly IBrush Idle = new SolidColorBrush(Color.FromRgb(17, 16, 16)), IdleBorder = new SolidColorBrush(Color.FromRgb(163, 158, 147));
    private static readonly IBrush Active = new SolidColorBrush(Color.FromRgb(28, 96, 50));
    // Light blue keeps "On" readable on both the dark idle background and the green running one.
    private static readonly IBrush ToggleOn = new SolidColorBrush(Color.FromRgb(140, 200, 255));
    // Light red keeps the stop hint readable on the dark idle background and the green running one.
    private static readonly IBrush StopRed = new SolidColorBrush(Color.FromRgb(255, 138, 128));
    private readonly TextBlock label = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock stopLabel = new() { Foreground = StopRed, FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock activityLabel = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly LayoutTransformControl scaler = new();
    private readonly Border frame;
    private OverlayOptions options = new();
    private bool configured;
    private bool recording, active;
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private PixelRect[] clickable = [];
    private static readonly IBrush ButtonNormal = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), ButtonHover = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
    // The padlock fills its button with no square behind it; the show and quit buttons keep theirs.
    private readonly Image lockPicture = new() { Width = 20, Height = 20 };
    private readonly Border lockButton;
    // The Click target it was last placed on, in screen pixels: a drag is turned into an anchor relative to it.
    private (double Left, double Top, double Width, double Height)? target;
    // The window manager moves the window during a drag and sends no release, so a move the overlay did not make itself
    // counts as a drag, and the drag is over once the position has stayed put this long.
    private readonly Avalonia.Threading.DispatcherTimer dragSettle = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private PixelPoint placed;
    private bool dragging;

    /// <summary>The overlay's square: bring the clicker window to the front.</summary>
    public event Action? ShowRequested;
    /// <summary>The overlay's X: stop whatever runs and quit the clicker.</summary>
    public event Action? ExitRequested;
    /// <summary>The overlay's padlock: lock it to its anchor, or unlock it to drag it.</summary>
    public event Action? LockToggled;
    /// <summary>The unlocked overlay was dropped here: its top-left corner in screen pixels.</summary>
    public event Action<int, int>? Dropped;

    public StatusOverlay()
    {
        Title = DesktopRuntime.OverlayTitle; SizeToContent = SizeToContent.WidthAndHeight; MaxWidth = 580;
        WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; CanResize = false; Focusable = false;
        lockButton = OverlayButton("", lockPicture, () => LockToggled?.Invoke(), Brushes.Transparent, Brushes.Transparent);
        actions.Children.Add(lockButton);
        // On GNOME the clicker has no taskbar entry while it is hidden or minimised, so the overlay offers a way back and out.
        actions.Children.Add(OverlayButton("Show Minimum Effort Clicker", "show", new Rectangle { Width = 9, Height = 9, Stroke = Brushes.White, StrokeThickness = 1.5 }, () => ShowRequested?.Invoke(),
            ButtonNormal, ButtonHover));
        // A red X, like a window's close button, so it reads as "quit".
        actions.Children.Add(OverlayButton("Quit Minimum Effort Clicker (stops anything running)", "quit", new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,0 L9,9 M9,0 L0,9"), Stroke = Brushes.White, StrokeThickness = 1.5, Width = 9, Height = 9 }, () => ExitRequested?.Invoke(),
            new SolidColorBrush(Color.FromRgb(196, 43, 28)), new SolidColorBrush(Color.FromRgb(232, 17, 35))));
        // The shortcut line stays on top in every state: the start key on the left, the stop that works now in red
        // beside the buttons. Status and toggle lines follow below.
        var row = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right); DockPanel.SetDock(stopLabel, Dock.Right);
        row.Children.Add(actions); row.Children.Add(stopLabel); row.Children.Add(label);
        var body = new StackPanel(); body.Children.Add(row); body.Children.Add(activityLabel);
        frame = new Border { Background = Idle, BorderBrush = IdleBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 4, 6, 4), Child = body };
        scaler.Child = frame;
        Content = scaler;
        ShowLock();
        ApplyColours();
        // Unlocked, a press anywhere but on a button drags the overlay.
        frame.PointerPressed += (_, e) =>
        {
            if (!options.Unlocked || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Handled = true; dragging = true; dragSettle.Stop(); dragSettle.Start();
            BeginMoveDrag(e);
        };
        PositionChanged += (_, e) =>
        {
            if (e.Point == placed || !options.Unlocked) return;
            dragging = true; dragSettle.Stop(); dragSettle.Start();
        };
        dragSettle.Tick += (_, _) => { dragSettle.Stop(); EndDrag(); };
        Opened += (_, _) => ApplyStyles();
        LayoutUpdated += (_, _) => UpdateClickable();
    }

    /// <summary>A button with its overlay picture; if the picture is missing, the drawn glyph stands in.</summary>
    private static Border OverlayButton(string tip, string picture, Control glyph, Action click, IBrush normal, IBrush hover)
        => UiArt.Overlay(picture) is { } source
            ? OverlayButton(tip, new Image { Source = source, Width = 12, Height = 12 }, click, normal, hover)
            : OverlayButton(tip, new Viewbox { Width = 11, Height = 11, Child = glyph }, click, normal, hover);
    private static Border OverlayButton(string tip, Control content, Action click, IBrush normal, IBrush hover)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center; content.VerticalAlignment = VerticalAlignment.Center;
        var button = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(3), Background = normal, Cursor = new Cursor(StandardCursorType.Hand), Child = content };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.PointerEntered += (_, _) => button.Background = hover;
        button.PointerExited += (_, _) => button.Background = normal;
        button.PointerPressed += (_, e) => e.Handled = true; // A press on a button never starts a drag.
        button.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) { e.Handled = true; click(); } };
        return button;
    }

    /// <summary>The padlock shows the state it is in: closed while locked to the anchor, open while it can be dragged.</summary>
    private void ShowLock()
    {
        lockPicture.Source = UiArt.Lock(!options.Unlocked);
        ToolTip.SetTip(lockButton, options.Unlocked ? "Unlocked: drag the overlay anywhere; it stays put on the screen. Click to lock it to the window where it is" : "Locked to its anchor point. Click to unlock it and drag it anywhere");
        Avalonia.Automation.AutomationProperties.SetName(lockButton, options.Unlocked ? "Lock the overlay" : "Unlock the overlay");
        frame.Cursor = options.Unlocked ? new Cursor(StandardCursorType.SizeAll) : null;
        clickable = []; if (IsVisible) UpdateClickable();
    }

    private void EndDrag()
    {
        dragging = false;
        if (Position == placed) return;
        placed = Position;
        Dropped?.Invoke(Position.X, Position.Y);
    }
    /// <summary>Where the overlay is now: its top-left corner in screen pixels.</summary>
    public (int X, int Y)? CurrentPosition() => (Position.X, Position.Y);
    /// <summary>The anchor that keeps the overlay where it is now on the Click target it was last placed on; locking uses it,
    /// so the overlay does not jump.</summary>
    public (double X, double Y)? CurrentAnchor()
    {
        if (target is not { } on) return null;
        var size = PixelFrame;
        return OverlayAnchor.From(on.Left, on.Top, on.Width, on.Height, size.Width, size.Height, Position.X, Position.Y, options.AnchorX, options.AnchorY);
    }

    /// <summary>Locked, clicks reach only the buttons and the rest of the overlay stays click-through; unlocked, all of it
    /// takes the mouse.</summary>
    private void UpdateClickable()
    {
        double scale = RenderScaling;
        var areas = options.Unlocked
            ? [new PixelRect(0, 0, (int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale))]
            : actions.Children.Select(c => c.TranslatePoint(new Point(0, 0), this) is { } p
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
        ShowLock();
    }

    /// <param name="alert">A short red line shown above the rest, for example why a toggle did not switch on; null for none.</param>
    public void Update(RuntimeSnapshot state, string stopKey, string startKey = "F8", IReadOnlyList<OverlayToggle>? toggles = null, string recordKey = "F10", bool actionsEmpty = false, string? alert = null)
    {
        alertLine = alert;
        bool capturing = state.Busy && state.State.StartsWith("Recording", StringComparison.Ordinal);
        bool running = state.PlaybackActive && !state.CleanupPending;
        if (capturing != recording || running != active) { recording = capturing; active = running; ApplyColours(); }
        if (capturing)
        {
            ShowShortcuts($"{recordKey} · Stop recording · {stopKey} · Stop all", null);
            ShowLines(state.State, []);
            ToolTip.SetTip(activityLabel, state.Reason);
            return;
        }
        toggles ??= [];
        bool togglesActive = toggles.Any(t => t.Active);
        if (state.CleanupPending) { ShowShortcuts(OverlayHints.StartStop(startKey), OverlayHints.StopAll(stopKey)); ShowLines("Input release pending", toggles); return; }
        if (!state.Busy) { ShowShortcuts(actionsEmpty ? "Actionlist Empty" : OverlayHints.StartStop(startKey), null); ShowLines(null, toggles); return; }
        // Stop All is the one stop: it ends the Action List and every toggle.
        ShowShortcuts(OverlayHints.StartStop(startKey), OverlayHints.StopAll(stopKey));
        ShowLines(state.ActionsActive || !togglesActive ? $"{state.State} · {(state.Activity is "Minimum Effort" or "Named points" ? "Actions" : OverlayHints.Activity(state.Activity))}" : null, toggles);
    }

    /// <summary>The heading line, then one line per toggle with its state word coloured: "On" blue, the rest white.</summary>
    private string? alertLine;
    private void ShowLines(string? heading, IReadOnlyList<OverlayToggle> toggles)
    {
        var inlines = new InlineCollection();
        var plain = new List<string>();
        if (alertLine is not null) { inlines.Add(new Run(alertLine) { Foreground = StopRed, FontWeight = FontWeight.SemiBold }); plain.Add(alertLine); }
        if (heading is not null) { if (plain.Count > 0) inlines.Add(new LineBreak()); inlines.Add(new Run(heading)); plain.Add(heading); }
        foreach (var toggle in toggles)
        {
            if (plain.Count > 0) inlines.Add(new LineBreak());
            inlines.Add(new Run($"{toggle.Name} · {toggle.Key} · "));
            inlines.Add(toggle.On ? new Run(toggle.State) { Foreground = ToggleOn } : new Run(toggle.State));
            plain.Add($"{toggle.Name} · {toggle.Key} · {toggle.State}");
        }
        activityLabel.Inlines = inlines;
        activityLabel.IsVisible = plain.Count > 0;
        ToolTip.SetTip(activityLabel, string.Join("\n", plain));
    }

    /// <summary>The top line: the start key (white, left) and the stop that works now (red, beside the buttons).</summary>
    private void ShowShortcuts(string start, string? stop)
    {
        label.Text = start;
        stopLabel.IsVisible = stop is not null;
        stopLabel.Text = stop ?? "";
    }

    private PixelSize PixelFrame
    {
        get
        {
            var size = FrameSize ?? Bounds.Size;
            return PixelSize.FromSize(size, DesktopScaling);
        }
    }

    /// <summary>Places the overlay at its anchor on the Click target (screen pixels), or, while unlocked and dropped somewhere,
    /// at that place on the screen: then it does not follow the target. Either way it is kept on the monitor's work area.</summary>
    public void PlaceOn(double left, double top, double width, double height)
    {
        // The frame comes from the desktop helper; the overlay is an X11 window, placed in X11 pixels (DesktopPixels).
        left *= DesktopPixels.Factor; top *= DesktopPixels.Factor; width *= DesktopPixels.Factor; height *= DesktopPixels.Factor;
        target = (left, top, width, height);
        if (dragging) return; // The drag owns the position until it settles.
        var size = PixelFrame;
        if (options.FreePlace is { } free) { PlaceWithinMonitor(free.X, free.Y, size.Width, size.Height); return; }
        var (x, y) = OverlayAnchor.Place(left, top, width, height, size.Width, size.Height, options.AnchorX, options.AnchorY);
        PlaceWithinMonitor((int)Math.Round(x), (int)Math.Round(y), size.Width, size.Height);
    }
    public void Constrain()
    {
        if (dragging) return;
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
        placed = new PixelPoint(x, y);
        if (Position != placed) Position = placed;
        Topmost = true;
    }
}
