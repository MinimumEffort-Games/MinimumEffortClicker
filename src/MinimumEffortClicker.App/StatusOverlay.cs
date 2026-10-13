using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

internal interface IOverlayPresenter
{
    void Update(RuntimeSnapshot state, string stopKey, string startKey = "F8", IReadOnlyList<string>? toggles = null, string recordKey = "F10", bool actionsEmpty = false);
    void Configure(OverlayOptions options);
}

/// <summary>Click-through status window pinned beside the Click target. It never takes focus or input.</summary>
internal sealed class StatusOverlay : Window, IOverlayPresenter
{
    private readonly TextBlock label = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock activityLabel = new() { Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 40 };
    private readonly StackPanel body = new();
    private OverlayOptions options = new();
    private bool configured;
    private bool recording, active;

    public StatusOverlay()
    {
        Title = "Minimum Effort status"; SizeToContent = SizeToContent.WidthAndHeight; MaxWidth = 580;
        Left = SystemParameters.WorkArea.Right - 320; Top = SystemParameters.WorkArea.Top + 20;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; Focusable = false;
        FontFamily = new FontFamily("Segoe UI");
        body.Children.Add(activityLabel); body.Children.Add(label);
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(17, 16, 16)), BorderBrush = new SolidColorBrush(Color.FromRgb(163, 158, 147)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 4, 9, 4), Child = body };
        ApplyContrast();
        SystemParameters.StaticPropertyChanged += ContrastChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= ContrastChanged;
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
            ApplyStyles();
        };
    }
    private void ContrastChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast) && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(ApplyContrast);
    }
    private void ApplyContrast()
    {
        bool contrast = SystemParameters.HighContrast;
        label.Foreground = activityLabel.Foreground = contrast ? SystemColors.WindowTextBrush : Brushes.White;
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
    }
    public void Update(RuntimeSnapshot state, string stopKey, string startKey = "F8", IReadOnlyList<string>? toggles = null, string recordKey = "F10", bool actionsEmpty = false)
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
        bool toggleOnly = toggles?.Count > 0;
        if (!state.Busy && !state.CleanupPending)
        {
            activityLabel.Text = (actionsEmpty ? "Actionlist Empty" : $"{startKey} to start/stop")
                + (toggles?.Count > 0 ? "\n" + string.Join("\n", toggles) : "");
            activityLabel.ToolTip = activityLabel.Text;
            label.Visibility = Visibility.Collapsed;
            return;
        }
        activityLabel.MaxHeight = double.PositiveInfinity;
        activityLabel.Text = toggleOnly ? (state.CleanupPending ? "Input release pending\n" : state.ActionsActive ? $"{state.State} · {state.Activity}\n" : "") + string.Join("\n", toggles!)
            : $"{state.State} · {(state.Activity is "Minimum Effort" or "Named points" ? "Actions" : state.Activity)}";
        activityLabel.ToolTip = activityLabel.Text;
        label.Visibility = toggleOnly && !state.ActionsActive && !state.CleanupPending ? Visibility.Collapsed : Visibility.Visible;
        label.Text = state.CleanupPending ? $"Stop all: {stopKey}" : $"{startKey} to start/stop";
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
