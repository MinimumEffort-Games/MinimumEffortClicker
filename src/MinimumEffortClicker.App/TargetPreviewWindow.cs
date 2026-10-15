using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

/// <summary>
/// Set Location: an own non-activating window over the Click target that intercepts one left click and returns where it
/// was. Nothing reaches the target. Right-click, Stop All, 30 seconds or a target change cancel it.
/// </summary>
internal sealed class TargetPreviewWindow : Window
{
    private readonly DesktopRuntime runtime;
    private readonly TargetClientFrame calibration;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Diagnostics.Stopwatch lifetime = System.Diagnostics.Stopwatch.StartNew();
    private readonly TaskCompletionSource<ClientPoint?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool reserved, pressed, closed;
    private ClientPoint? result;
    private string finishReason = "Pick closed. Nothing was sent.";
    public Task<ClientPoint?> Finished => completion.Task;

    public TargetPreviewWindow(DesktopRuntime runtime)
    {
        this.runtime = runtime; calibration = runtime.GetTargetFrame();
        Title = "Pick a click location · Stop All cancels";
        Width = calibration.Width; Height = calibration.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(35, 0, 0, 0));
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Focusable = false;
        Cursor = Cursors.Cross;
        Content = new HintSurface { ClipToBounds = true };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            long style = ProbeNative.GetWindowLongPtr(handle, -20).ToInt64();
            // WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: it takes the picking click but never focus.
            ProbeNative.SetWindowLongPtr(handle, -20, (nint)(style | 0x80000L | 0x08000000L | 0x80L));
            HwndSource.FromHwnd(handle)?.AddHook(NonActivatingMessage);
        };
        MouseLeftButtonDown += (_, e) => { pressed = true; e.Handled = true; };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            e.Handled = true;
            if (runtime.Current?.IsAvailable != true || !ProbeNative.GetCursorPos(out var pointer)) { Finish("Target unavailable. Pick cancelled."); return; }
            try
            {
                var frame = runtime.GetTargetFrame();
                if (frame != calibration) { Finish("Target moved or changed during selection. Pick again."); return; }
                var point = runtime.IsDesktopMode
                    ? new ClientPoint(pointer.X, pointer.Y, frame.Width, frame.Height, frame.Dpi, CoordinateSpace.Desktop)
                    : new ClientPoint(pointer.X - frame.X, pointer.Y - frame.Y, frame.Width, frame.Height, frame.Dpi);
                if (point.IsValid) { result = point; Finish("Point selected for review. No target input was sent."); }
                else Finish("Point outside the target. Selection cancelled.");
            }
            catch (InvalidOperationException ex) { Finish(ex.Message); }
        };
        MouseRightButtonUp += (_, e) => { e.Handled = true; Finish("Point selection cancelled."); };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (lifetime.Elapsed > TimeSpan.FromSeconds(30) || runtime.Current?.IsAvailable != true || runtime.GetTargetFrame() != calibration)
                    Finish("Pick ended after 30 seconds or a target change. Nothing was sent.");
            }
            catch (InvalidOperationException ex) { Finish(ex.Message); }
        };
        Closed += (_, _) =>
        {
            closed = true; timer.Stop(); if (reserved) runtime.EndTargetPreview(finishReason);
            completion.TrySetResult(result);
        };
    }
    public void Start()
    {
        runtime.BeginTargetPreview(message => Dispatcher.BeginInvoke(() => Finish(message)));
        reserved = true;
        try
        {
            Show();
            if (!ProbeNative.SetWindowPos(new WindowInteropHelper(this).Handle, -1, calibration.X, calibration.Y, calibration.Width, calibration.Height, 0x10 | 0x40))
                throw new InvalidOperationException("The pick window could not be positioned.");
            timer.Start();
        }
        catch { Finish("The pick window could not start."); throw; }
    }
    private void Finish(string reason) { if (closed) return; finishReason = reason; Close(); }
    private static nint NonActivatingMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; } // WM_MOUSEACTIVATE: MA_NOACTIVATE, retain the selecting click.
        return 0;
    }

    /// <summary>The one-line hint in the corner of the pick window.</summary>
    private sealed class HintSurface : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            var dpi = VisualTreeHelper.GetDpi(this);
            Brush ink = Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White;
            Brush surface = Application.Current.TryFindResource("Surface") as Brush ?? Brushes.Black;
            var hint = new FormattedText("Pick a point · click is intercepted · right-click or Stop All cancels", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 14, ink, dpi.PixelsPerDip);
            drawing.DrawRectangle(surface, new Pen(ink, 2), new Rect(12, 12, Math.Max(0, Math.Min(ActualWidth - 24, hint.Width + 24)), 40));
            drawing.DrawText(hint, new Point(24, 23));
        }
    }
}
