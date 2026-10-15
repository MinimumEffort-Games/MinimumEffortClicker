using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

internal interface IOverlayPresenter
{
    void Update(RuntimeSnapshot state, string stopKey, string startKey = "F8", IReadOnlyList<OverlayToggle>? toggles = null, string recordKey = "F10", bool actionsEmpty = false, EmergencyStopMode emergencyStop = EmergencyStopMode.BothMouseButtons);
    void Configure(OverlayOptions options);
}

/// <summary>
/// Status window pinned beside the Click target. It never takes focus. It is click-through except for its two buttons
/// (bring the clicker back, quit), which take only a real mouse click: the clicker's own clicks pass them by.
/// </summary>
internal sealed class StatusOverlay : Window, IOverlayPresenter
{
    private static readonly Brush ButtonNormal = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), ButtonHover = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
    // Light blue keeps "On" readable on both the dark idle background and the green running one.
    private static readonly Brush ToggleOn = new SolidColorBrush(Color.FromRgb(140, 200, 255));
    private readonly TextBlock label = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock activityLabel = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel body = new();
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly DockPanel row = new();
    private readonly List<(Border Button, System.Windows.Shapes.Shape Glyph)> buttons = [];
    // The window is click-through as a whole; while the pointer is over a button it takes clicks, so the rest never does.
    private readonly System.Windows.Threading.DispatcherTimer pointerWatch = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool clickable;
    private OverlayOptions options = new();
    private bool configured;
    private bool recording, active;

    /// <summary>The overlay's square: bring the clicker window to the front.</summary>
    public event Action? ShowRequested;
    /// <summary>The overlay's X: stop whatever runs and quit the clicker.</summary>
    public event Action? ExitRequested;

    public StatusOverlay()
    {
        Title = "Minimum Effort status"; SizeToContent = SizeToContent.WidthAndHeight; MaxWidth = 580;
        Left = SystemParameters.WorkArea.Right - 320; Top = SystemParameters.WorkArea.Top + 20;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; Focusable = false;
        FontFamily = new FontFamily("Segoe UI");
        body.Children.Add(activityLabel); body.Children.Add(label);
        AddButton("Show Minimum Effort Clicker", new System.Windows.Shapes.Rectangle { Width = 9, Height = 9, StrokeThickness = 1.5 }, () => ShowRequested?.Invoke());
        AddButton("Quit Minimum Effort Clicker (stops anything running)", new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L9,9 M9,0 L0,9"), StrokeThickness = 1.5, Width = 9, Height = 9 }, () => ExitRequested?.Invoke());
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions); row.Children.Add(body);
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(17, 16, 16)), BorderBrush = new SolidColorBrush(Color.FromRgb(163, 158, 147)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 4, 6, 4), Child = row };
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

    private void AddButton(string tip, System.Windows.Shapes.Shape glyph, Action click)
    {
        var button = new Border { Width = 20, Height = 20, Margin = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(3), Background = ButtonNormal,
            Child = new Viewbox { Width = 11, Height = 11, Child = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        button.ToolTip = tip;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        buttons.Add((button, glyph)); actions.Children.Add(button);
    }

    /// <summary>Lets clicks reach the overlay only while the pointer is over one of its buttons, and shows the hover.</summary>
    private void WatchPointer()
    {
        if (!ProbeNative.GetCursorPos(out var pointer)) return;
        Border? over = null;
        try
        {
            foreach (var (button, _) in buttons)
            {
                var from = button.PointToScreen(new Point(0, 0)); var to = button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight));
                if (pointer.X >= from.X && pointer.X < to.X && pointer.Y >= from.Y && pointer.Y < to.Y) { over = button; break; }
            }
        }
        catch (InvalidOperationException) { } // Not on screen yet.
        foreach (var (button, _) in buttons) button.Background = button == over ? ButtonHover : ButtonNormal;
        SetClickable(over is not null);
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
        foreach (var (_, glyph) in buttons) glyph.Stroke = contrast ? SystemColors.WindowTextBrush : Brushes.White;
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
        row.LayoutTransform = new ScaleTransform(value.Scale, value.Scale);
    }
    public void Update(RuntimeSnapshot state, string stopKey, string startKey = "F8", IReadOnlyList<OverlayToggle>? toggles = null, string recordKey = "F10", bool actionsEmpty = false, EmergencyStopMode emergencyStop = EmergencyStopMode.BothMouseButtons)
    {
        bool capturing = state.Recording;
        bool running = state.PlaybackActive && !state.CleanupPending;
        if (capturing != recording || running != active) { recording = capturing; active = running; ApplyContrast(); }
        if (capturing)
        {
            activityLabel.Text = state.State;
            activityLabel.ToolTip = state.Reason;
            label.Visibility = Visibility.Visible;
            label.Text = $"{recordKey} · Stop recording · {stopKey} · Stop all";
            return;
        }
        toggles ??= [];
        bool togglesActive = toggles.Any(t => t.Active);
        if (state.CleanupPending) { ShowLines("Input release pending", toggles); ShowHint($"Stop all: {stopKey}"); return; }
        if (!state.Busy) { ShowLines(actionsEmpty ? "Actionlist Empty" : OverlayHints.StartStop(startKey), toggles); ShowHint(null); return; }
        ShowLines(state.ActionsActive || !togglesActive ? $"{state.State} · {(state.Activity is "Minimum Effort" or "Named points" ? "Actions" : state.Activity)}" : null, toggles);
        ShowHint(OverlayHints.WhileRunning(startKey, stopKey, emergencyStop, state.ActionsActive, togglesActive));
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
        activityLabel.ToolTip = string.Join("\n", plain);
    }
    private void ShowHint(string? hint)
    {
        label.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
        if (hint is not null) label.Text = hint;
    }
    public void PlaceAt(double right, double top)
    {
        UpdateLayout();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0 && ProbeNative.GetWindowRect(hwnd, out var rect))
            PlaceWithinMonitor((int)right - (rect.Right - rect.Left) - 12, (int)top + 12, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    public void Constrain()
    {
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
