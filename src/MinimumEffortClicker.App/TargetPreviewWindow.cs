using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

/// <summary>Own non-activating window: either input-free click-through markers or an explicit click-intercepting picker.</summary>
internal sealed class TargetPreviewWindow : Window
{
    private readonly DesktopRuntime runtime;
    private readonly TargetClientFrame calibration;
    private readonly bool picking;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Diagnostics.Stopwatch lifetime = System.Diagnostics.Stopwatch.StartNew();
    private readonly TaskCompletionSource<ClientPoint?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MarkerSurface markers;
    private bool reserved, pressed, closed;
    private ClientPoint? result;
    private string finishReason = "Target preview closed. Nothing was sent.";
    public Task<ClientPoint?> Finished => completion.Task;

    public TargetPreviewWindow(DesktopRuntime runtime, IReadOnlyList<NamedPoint> points, bool picking, TimeSpan? timeout = null, bool clickLocation = false)
    {
        this.runtime = runtime; this.picking = picking; calibration = runtime.GetTargetFrame();
        if (points.Any(p => !p.Position.Matches(calibration.Width, calibration.Height, calibration.Dpi)))
            throw new InvalidOperationException("A named point needs recalibration for this window size/DPI.");
        Title = picking ? "Pick a click location · Stop All cancels" : clickLocation ? "Click location · no input" : "Named point preview · no input";
        Width = calibration.Width; Height = calibration.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = picking ? new SolidColorBrush(Color.FromArgb(35, 0, 0, 0)) : Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Focusable = false;
        Cursor = picking ? Cursors.Cross : Cursors.Arrow;
        var projected = points.Select(p =>
        {
            var screen = p.Position.Resolve(calibration.X, calibration.Y, calibration.Width, calibration.Height, calibration.Dpi);
            var client = new ClientPoint(screen.X - calibration.X, screen.Y - calibration.Y, calibration.Width, calibration.Height, calibration.Dpi);
            if (!client.IsValid) throw new InvalidOperationException("A fixed desktop point is outside the selected target. Review its coordinates before previewing.");
            return p with { Position = client };
        }).ToArray();
        markers = new MarkerSurface(projected, picking, clickLocation) { ClipToBounds = true };
        Content = markers;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            long style = ProbeNative.GetWindowLongPtr(handle, -20).ToInt64();
            ProbeNative.SetWindowLongPtr(handle, -20, (nint)(style | 0x80000L | 0x08000000L | 0x80L | (picking ? 0L : 0x20L)));
            HwndSource.FromHwnd(handle)?.AddHook(NonActivatingMessage);
        };
        MouseLeftButtonDown += (_, e) => { if (picking) { pressed = true; e.Handled = true; } };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!picking || !pressed) return;
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
        MouseRightButtonUp += (_, e) => { if (picking) { e.Handled = true; Finish("Point selection cancelled."); } };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (clickLocation) { markers.Pulse = lifetime.Elapsed.TotalSeconds; markers.InvalidateVisual(); }
                if (lifetime.Elapsed > (timeout ?? TimeSpan.FromSeconds(picking ? 30 : 10)) || runtime.Current?.IsAvailable != true || runtime.GetTargetFrame() != calibration)
                    Finish("Target preview ended after timeout or target change. Nothing was sent.");
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
                throw new InvalidOperationException("Target preview could not be positioned.");
            timer.Start();
        }
        catch { Finish("Target preview could not start."); throw; }
    }
    private void Finish(string reason) { if (closed) return; finishReason = reason; Close(); }
    private static nint NonActivatingMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; } // WM_MOUSEACTIVATE: MA_NOACTIVATE, retain the selecting click.
        return 0;
    }

    private sealed class MarkerSurface(IReadOnlyList<NamedPoint> points, bool picking, bool clickLocation) : FrameworkElement
    {
        public double Pulse { get; set; }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            var dpi = VisualTreeHelper.GetDpi(this);
            Brush ink = Application.Current.TryFindResource("TextPrimary") as Brush ?? Brushes.White;
            Brush surface = Application.Current.TryFindResource("Surface") as Brush ?? Brushes.Black;
            var pen = new Pen(ink, 2);
            int number = 0;
            foreach (var point in points)
            {
                var centre = new Point(point.Position.X / dpi.DpiScaleX, point.Position.Y / dpi.DpiScaleY);
                if (clickLocation)
                {
                    // Attention target: bright centre, contrasting outlines and slowly shrinking rings.
                    var red = Brushes.Red;
                    for (int ring = 0; ring < 2; ring++)
                    {
                        double phase = (Pulse / 1.2 + ring * 0.5) % 1;
                        double radius = 14 + 46 * (1 - phase);
                        drawing.PushOpacity(0.35 + phase * 0.65);
                        drawing.DrawEllipse(null, new Pen(Brushes.White, 6), centre, radius, radius);
                        drawing.DrawEllipse(null, new Pen(red, 3), centre, radius, radius);
                        drawing.Pop();
                    }
                    drawing.DrawLine(new Pen(Brushes.White, 5), new(centre.X - 22, centre.Y), new(centre.X + 22, centre.Y));
                    drawing.DrawLine(new Pen(Brushes.White, 5), new(centre.X, centre.Y - 22), new(centre.X, centre.Y + 22));
                    drawing.DrawLine(new Pen(red, 2), new(centre.X - 22, centre.Y), new(centre.X + 22, centre.Y));
                    drawing.DrawLine(new Pen(red, 2), new(centre.X, centre.Y - 22), new(centre.X, centre.Y + 22));
                    drawing.DrawEllipse(red, new Pen(Brushes.White, 2), centre, 7, 7);
                    continue;
                }
                drawing.DrawEllipse(surface, pen, centre, 13, 13);
                var label = new FormattedText($"{++number}  {point.Name}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 13, ink, dpi.PixelsPerDip);
                drawing.DrawRectangle(surface, null, new Rect(centre.X + 17, centre.Y - 12, label.Width + 12, 26));
                drawing.DrawText(label, new Point(centre.X + 23, centre.Y - 9));
            }
            if (clickLocation) return;
            var hint = new FormattedText(picking ? "Pick a point · click is intercepted · right-click or Stop All cancels" : "Named points · no input · click-through · Stop All closes", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 14, ink, dpi.PixelsPerDip);
            drawing.DrawRectangle(surface, pen, new Rect(12, 12, Math.Max(0, Math.Min(ActualWidth - 24, hint.Width + 24)), 40));
            drawing.DrawText(hint, new Point(24, 23));
        }
    }
}
