using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

internal interface IOverlayPresenter
{
    void Update(RuntimeSnapshot state, string stopKey, string startKey = "F8", IReadOnlyList<OverlayToggle>? toggles = null, string recordKey = "F10", bool actionsEmpty = false);
    void Configure(OverlayOptions options);
}

/// <summary>
/// Status window pinned to the Click target at the anchor chosen in Settings. It never takes focus. Locked, it is
/// click-through except for its three buttons (lock, bring the clicker back, quit), which take only a real mouse click:
/// the clicker's own clicks pass them by. Unlocked, all of it takes the mouse, so it can be dragged.
/// </summary>
internal sealed class StatusOverlay : Window, IOverlayPresenter
{
    private static readonly Brush ButtonNormal = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), ButtonHover = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
    // The X is red like a window's close button, so it reads as "quit".
    private static readonly Brush QuitNormal = new SolidColorBrush(Color.FromRgb(196, 43, 28)), QuitHover = new SolidColorBrush(Color.FromRgb(232, 17, 35));
    // Light red keeps the stop hint readable on the dark idle background and the green running one.
    private static readonly Brush StopRed = new SolidColorBrush(Color.FromRgb(255, 138, 128));
    // Light blue keeps "On" readable on both the dark idle background and the green running one.
    private static readonly Brush ToggleOn = new SolidColorBrush(Color.FromRgb(140, 200, 255));
    private readonly TextBlock label = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock stopLabel = new() { Foreground = StopRed, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock activityLabel = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel body = new();
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DockPanel row = new();
    private readonly List<(Border Button, System.Windows.Shapes.Shape? Glyph, Brush Normal, Brush Hover)> buttons = [];
    // The padlock fills its button with no square behind it; the show and quit buttons keep theirs.
    private readonly Image lockPicture = new() { Width = 20, Height = 20 };
    private readonly Border lockButton;
    // The window is click-through as a whole; while the pointer is over a button (or over any of it while unlocked) it
    // takes clicks, so the rest never does.
    private readonly System.Windows.Threading.DispatcherTimer pointerWatch = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool clickable;
    private OverlayOptions options = new();
    private bool configured;
    private bool recording, active;
    // The Click target it was last placed on, in screen pixels: a drag is turned into an anchor relative to it.
    private (double Left, double Top, double Width, double Height)? target;
    private bool dragging;

    /// <summary>The overlay's square: bring the clicker window to the front.</summary>
    public event Action? ShowRequested;
    /// <summary>The overlay's X: stop whatever runs and quit the clicker.</summary>
    public event Action? ExitRequested;
    /// <summary>The overlay's padlock: lock it to its anchor, or unlock it to drag it.</summary>
    public event Action? LockToggled;
    /// <summary>The unlocked overlay was dropped: the anchor that keeps it there relative to the Click target.</summary>
    public event Action<double, double>? AnchorDragged;

    public StatusOverlay()
    {
        Title = "Minimum Effort status"; SizeToContent = SizeToContent.WidthAndHeight; MaxWidth = 580;
        Left = SystemParameters.WorkArea.Right - 320; Top = SystemParameters.WorkArea.Top + 20;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; Focusable = false;
        FontFamily = new FontFamily("Segoe UI");
        lockButton = AddButton("", lockPicture, null, () => LockToggled?.Invoke(), Brushes.Transparent, Brushes.Transparent);
        ShowLock();
        AddButton("Show Minimum Effort Clicker", "show", new System.Windows.Shapes.Rectangle { Width = 9, Height = 9, StrokeThickness = 1.5 }, () => ShowRequested?.Invoke(), ButtonNormal, ButtonHover);
        AddButton("Quit Minimum Effort Clicker (stops anything running)", "quit", new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L9,9 M9,0 L0,9"), StrokeThickness = 1.5, Width = 9, Height = 9 }, () => ExitRequested?.Invoke(), QuitNormal, QuitHover);
        // The shortcut line stays on top in every state: the start key on the left, the stop that works now in red
        // beside the buttons. Status and toggle lines follow below.
        DockPanel.SetDock(actions, Dock.Right); DockPanel.SetDock(stopLabel, Dock.Right);
        row.Children.Add(actions); row.Children.Add(stopLabel); row.Children.Add(label);
        body.Children.Add(row); body.Children.Add(activityLabel);
        var frame = new Border { Background = new SolidColorBrush(Color.FromRgb(17, 16, 16)), BorderBrush = new SolidColorBrush(Color.FromRgb(163, 158, 147)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 4, 6, 4), Child = body };
        // Unlocked, a press anywhere but on a button drags the overlay.
        frame.MouseLeftButtonDown += (_, e) => { if (!options.Unlocked) return; e.Handled = true; Drag(); };
        Content = frame;
        ApplyContrast();
        SystemParameters.StaticPropertyChanged += ContrastChanged;
        pointerWatch.Tick += (_, _) => WatchPointer();
        IsVisibleChanged += (_, _) => { if (IsVisible) pointerWatch.Start(); else { pointerWatch.Stop(); SetClickable(false); } };
        Closed += (_, _) => { pointerWatch.Stop(); SystemParameters.StaticPropertyChanged -= ContrastChanged; };
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
            ApplyStyles();
        };
    }

    /// <summary>A button with its overlay picture; if the picture is missing, the drawn glyph stands in.</summary>
    private void AddButton(string tip, string picture, System.Windows.Shapes.Shape glyph, Action click, Brush normal, Brush hover)
    {
        if (UiPictures.Overlay(picture) is { } source) AddButton(tip, new Image { Source = source, Width = 12, Height = 12 }, null, click, normal, hover);
        else AddButton(tip, new Viewbox { Width = 11, Height = 11, Child = glyph }, glyph, click, normal, hover);
    }
    private Border AddButton(string tip, FrameworkElement content, System.Windows.Shapes.Shape? glyph, Action click, Brush normal, Brush hover)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center; content.VerticalAlignment = VerticalAlignment.Center;
        var button = new Border { Width = 20, Height = 20, Margin = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(3), Background = normal, Child = content, Cursor = Cursors.Arrow };
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        button.ToolTip = tip;
        button.MouseLeftButtonDown += (_, e) => e.Handled = true; // A press on a button never starts a drag.
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        buttons.Add((button, glyph, normal, hover)); actions.Children.Add(button);
        return button;
    }
    /// <summary>The padlock shows the state it is in: closed while locked to the anchor, open while it can be dragged.</summary>
    private void ShowLock()
    {
        lockPicture.Source = UiPictures.Lock(!options.Unlocked);
        string tip = options.Unlocked ? "Unlocked: drag the overlay anywhere. Click to lock it there" : "Locked to its anchor point. Click to unlock it and drag it anywhere";
        lockButton.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(lockButton, options.Unlocked ? "Lock the overlay" : "Unlock the overlay");
        if (Content is Border frame) frame.Cursor = options.Unlocked ? Cursors.SizeAll : null;
    }

    /// <summary>Lets clicks reach the overlay only while the pointer is over one of its buttons (or over any of it while
    /// unlocked), and shows the hover.</summary>
    private void WatchPointer()
    {
        if (!ProbeNative.GetCursorPos(out var pointer)) return;
        Border? over = null;
        bool inside = false;
        try
        {
            foreach (var (button, _, _, _) in buttons)
            {
                var from = button.PointToScreen(new Point(0, 0)); var to = button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight));
                if (pointer.X >= from.X && pointer.X < to.X && pointer.Y >= from.Y && pointer.Y < to.Y) { over = button; break; }
            }
            if (options.Unlocked && ProbeNative.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect))
                inside = pointer.X >= rect.Left && pointer.X < rect.Right && pointer.Y >= rect.Top && pointer.Y < rect.Bottom;
        }
        catch (InvalidOperationException) { } // Not on screen yet.
        foreach (var (button, _, normal, hover) in buttons) button.Background = button == over ? hover : normal;
        SetClickable(over is not null || inside || dragging);
    }
    private void Drag()
    {
        dragging = true;
        try { DragMove(); }
        catch (InvalidOperationException) { return; } // The button was already up.
        finally { dragging = false; }
        var hwnd = new WindowInteropHelper(this).Handle;
        if (target is not { } on || !ProbeNative.GetWindowRect(hwnd, out var rect)) return;
        var anchor = OverlayAnchor.From(on.Left, on.Top, on.Width, on.Height, rect.Right - rect.Left, rect.Bottom - rect.Top, rect.Left, rect.Top, options.AnchorX, options.AnchorY);
        AnchorDragged?.Invoke(anchor.X, anchor.Y);
    }
    private void SetClickable(bool on)
    {
        if (on == clickable) return;
        var hwnd = new WindowInteropHelper(this).Handle; if (hwnd == 0) return;
        clickable = on;
        long style = ProbeNative.GetWindowLongPtr(hwnd, -20).ToInt64();
        ProbeNative.SetWindowLongPtr(hwnd, -20, (nint)(on ? style & ~0x20L : style | 0x20L)); // WS_EX_TRANSPARENT
    }
    private void ContrastChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast) && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(ApplyContrast);
    }
    private void ApplyContrast()
    {
        bool contrast = SystemParameters.HighContrast;
        label.Foreground = activityLabel.Foreground = contrast ? SystemColors.WindowTextBrush : Brushes.White;
        stopLabel.Foreground = contrast ? SystemColors.WindowTextBrush : StopRed;
        foreach (var (_, glyph, _, _) in buttons) if (glyph is not null) glyph.Stroke = contrast ? SystemColors.WindowTextBrush : Brushes.White;
        if (Content is Border border)
        {
            border.Background = contrast ? SystemColors.WindowBrush : recording ? Brushes.DarkRed : active ? new SolidColorBrush(Color.FromRgb(28, 96, 50)) : new SolidColorBrush(Color.FromRgb(17, 16, 16));
            border.BorderBrush = contrast ? SystemColors.WindowTextBrush : recording ? Brushes.IndianRed : active ? Brushes.MediumSeaGreen : new SolidColorBrush(Color.FromRgb(163, 158, 147));
        }
        Opacity = contrast ? 1 : options.Opacity;
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; } // WM_MOUSEACTIVATE → MA_NOACTIVATE
        // A left click the clicker itself sent (a macro click that lands on the overlay) never presses its buttons.
        if (message is 0x201 or 0x202 or 0x203 && ProbeNative.GetMessageExtraInfo() == (nint)ProbeNative.InputTag) { handled = true; return 0; }
        return 0;
    }
    private void ApplyStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle; if (hwnd == 0) return;
        long style = ProbeNative.GetWindowLongPtr(hwnd, -20).ToInt64();
        // WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: visible, click-through, never focused.
        style = (style | 0x80000 | 0x20 | 0x08000000 | 0x80) & ~0x40000L;
        ProbeNative.SetWindowLongPtr(hwnd, -20, (nint)style);
        ProbeNative.SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x37); // frame changed, no move/size/z-order/activation
    }
    public void Configure(OverlayOptions value)
    {
        if (!value.Valid) throw new ArgumentException("Invalid overlay preferences.");
        if (configured && value == options) return;
        configured = true;
        options = value; Opacity = SystemParameters.HighContrast ? 1 : value.Opacity;
        MaxWidth = 580 * value.Scale;
        body.LayoutTransform = new ScaleTransform(value.Scale, value.Scale);
        ShowLock();
    }
    public void Update(RuntimeSnapshot state, string stopKey, string startKey = "F8", IReadOnlyList<OverlayToggle>? toggles = null, string recordKey = "F10", bool actionsEmpty = false)
    {
        bool capturing = state.Recording;
        bool running = state.PlaybackActive && !state.CleanupPending;
        if (capturing != recording || running != active) { recording = capturing; active = running; ApplyContrast(); }
        if (capturing)
        {
            ShowShortcuts($"{recordKey} · Stop recording · {stopKey} · Stop all", null);
            ShowLines(state.State, []);
            activityLabel.ToolTip = state.Reason;
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
    private void ShowLines(string? heading, IReadOnlyList<OverlayToggle> toggles)
    {
        activityLabel.Inlines.Clear();
        var plain = new List<string>();
        if (heading is not null) { activityLabel.Inlines.Add(new Run(heading)); plain.Add(heading); }
        foreach (var toggle in toggles)
        {
            if (plain.Count > 0) activityLabel.Inlines.Add(new LineBreak());
            activityLabel.Inlines.Add(new Run($"{toggle.Name} · {toggle.Key} · "));
            var state = new Run(toggle.State);
            if (toggle.On && !SystemParameters.HighContrast) state.Foreground = ToggleOn;
            activityLabel.Inlines.Add(state);
            plain.Add($"{toggle.Name} · {toggle.Key} · {toggle.State}");
        }
        activityLabel.Visibility = plain.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        activityLabel.ToolTip = string.Join("\n", plain);
    }
    /// <summary>The top line: the start key (white, left) and the stop that works now (red, beside the buttons).</summary>
    private void ShowShortcuts(string start, string? stop)
    {
        label.Text = start;
        stopLabel.Visibility = stop is null ? Visibility.Collapsed : Visibility.Visible;
        stopLabel.Text = stop ?? "";
    }
    /// <summary>Places the overlay on the Click target (screen pixels) at its anchor, kept on the monitor's work area.</summary>
    public void PlaceOn(double left, double top, double width, double height)
    {
        target = (left, top, width, height);
        if (dragging) return; // The drag owns the position until the button is let go.
        UpdateLayout();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || !ProbeNative.GetWindowRect(hwnd, out var rect)) return;
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        var (x, y) = OverlayAnchor.Place(left, top, width, height, w, h, options.AnchorX, options.AnchorY);
        PlaceWithinMonitor((int)Math.Round(x), (int)Math.Round(y), w, h);
    }
    public void Constrain()
    {
        if (dragging) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0 && ProbeNative.GetWindowRect(hwnd, out var rect)) PlaceWithinMonitor(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    private void PlaceWithinMonitor(int x, int y, int width, int height)
    {
        var info = new ProbeNative.MonitorInfo { Size = Marshal.SizeOf<ProbeNative.MonitorInfo>() };
        if (!ProbeNative.GetMonitorInfo(ProbeNative.MonitorFromPoint(new() { X = x, Y = y }, 2), ref info)) return;
        x = Math.Clamp(x, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
        y = Math.Clamp(y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
        var hwnd = new WindowInteropHelper(this).Handle;
        if (ProbeNative.GetWindowRect(hwnd, out var current) && current.Left == x && current.Top == y)
        {
            // Reassert topmost without moving or resizing after another topmost app takes focus.
            ProbeNative.SetWindowPos(hwnd, new nint(-1), 0, 0, 0, 0, 0x13); return;
        }
        ProbeNative.SetWindowPos(hwnd, new nint(-1), x, y, 0, 0, 0x11);
    }
}
