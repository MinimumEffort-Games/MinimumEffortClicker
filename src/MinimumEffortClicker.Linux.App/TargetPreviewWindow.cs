using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Linux;

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
        // The picker shares the overlay's title so playback hit tests and the window list ignore it.
        Title = picking ? DesktopRuntime.OverlayTitle : clickLocation ? "Click location · no input" : "Named point preview · no input";
        Width = calibration.Width; Height = calibration.Height;
        WindowDecorations = WindowDecorations.None; CanResize = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        // A fully transparent X11 window can still receive clicks; the picker keeps a faint tint so it is visible.
        Background = picking ? new SolidColorBrush(Color.FromArgb(35, 0, 0, 0)) : Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Cursor = picking ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
        var projected = points.Select(p =>
        {
            var screen = p.Position.Resolve(calibration.X, calibration.Y, calibration.Width, calibration.Height, calibration.Dpi);
            var client = new ClientPoint(screen.X - calibration.X, screen.Y - calibration.Y, calibration.Width, calibration.Height, calibration.Dpi);
            if (!client.IsValid) throw new InvalidOperationException("A fixed desktop point is outside the selected target. Review its coordinates before previewing.");
            return p with { Position = client };
        }).ToArray();
        markers = new MarkerSurface(projected, picking, clickLocation) { ClipToBounds = true };
        Content = markers;
        // Never take focus from the target: markers are also click-through; the picker takes clicks only.
        Opened += (_, _) => { X11Windows.SetNoFocus(this, true); if (!picking) X11Windows.SetClickThrough(this, true); };
        PointerPressed += (_, e) => { if (picking && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { pressed = true; e.Handled = true; } };
        PointerReleased += (_, e) =>
        {
            if (!picking) return;
            if (e.InitialPressMouseButton == MouseButton.Right) { e.Handled = true; Finish("Point selection cancelled."); return; }
            if (!pressed || e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            if (runtime.Current?.IsAvailable != true || runtime.PointerPosition is not { } pointer) { Finish("Target unavailable. Pick cancelled."); return; }
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
        runtime.BeginTargetPreview(message => Dispatcher.Post(() => Finish(message)));
        reserved = true;
        try
        {
            Position = new PixelPoint(calibration.X, calibration.Y);
            Show();
            Position = new PixelPoint(calibration.X, calibration.Y);
            timer.Start();
        }
        catch { Finish("Target preview could not start."); throw; }
    }
    private void Finish(string reason) { if (closed) return; finishReason = reason; Close(); }

    private sealed class MarkerSurface(IReadOnlyList<NamedPoint> points, bool picking, bool clickLocation) : Control
    {
        public double Pulse { get; set; }
        public override void Render(DrawingContext drawing)
        {
            base.Render(drawing);
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            IBrush ink = Application.Current?.FindResource("TextPrimary") as IBrush ?? Brushes.White;
            IBrush surface = Application.Current?.FindResource("Surface") as IBrush ?? Brushes.Black;
            var pen = new Pen(ink, 2);
            var typeface = new Typeface(FontFamily.Default);
            int number = 0;
            foreach (var point in points)
            {
                var centre = new Point(point.Position.X / scale, point.Position.Y / scale);
                if (clickLocation)
                {
                    // Attention target: bright centre, contrasting outlines and slowly shrinking rings.
                    var red = Brushes.Red;
                    for (int ring = 0; ring < 2; ring++)
                    {
                        double phase = (Pulse / 1.2 + ring * 0.5) % 1;
                        double radius = 14 + 46 * (1 - phase);
                        using (drawing.PushOpacity(0.35 + phase * 0.65))
                        {
                            drawing.DrawEllipse(null, new Pen(Brushes.White, 6), centre, radius, radius);
                            drawing.DrawEllipse(null, new Pen(red, 3), centre, radius, radius);
                        }
                    }
                    drawing.DrawLine(new Pen(Brushes.White, 5), new(centre.X - 22, centre.Y), new(centre.X + 22, centre.Y));
                    drawing.DrawLine(new Pen(Brushes.White, 5), new(centre.X, centre.Y - 22), new(centre.X, centre.Y + 22));
                    drawing.DrawLine(new Pen(red, 2), new(centre.X - 22, centre.Y), new(centre.X + 22, centre.Y));
                    drawing.DrawLine(new Pen(red, 2), new(centre.X, centre.Y - 22), new(centre.X, centre.Y + 22));
                    drawing.DrawEllipse(red, new Pen(Brushes.White, 2), centre, 7, 7);
                    continue;
                }
                drawing.DrawEllipse(surface, pen, centre, 13, 13);
                var label = new FormattedText($"{++number}  {point.Name}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 13, ink);
                drawing.DrawRectangle(surface, null, new Rect(centre.X + 17, centre.Y - 12, label.Width + 12, 26));
                drawing.DrawText(label, new Point(centre.X + 23, centre.Y - 9));
            }
            if (clickLocation) return;
            var hint = new FormattedText(picking ? "Pick a point · click is intercepted · right-click or Stop All cancels" : "Named points · no input · click-through · Stop All closes", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 14, ink);
            drawing.DrawRectangle(surface, pen, new Rect(12, 12, Math.Max(0, Math.Min(Bounds.Width - 24, hint.Width + 24)), 40));
            drawing.DrawText(hint, new Point(24, 23));
        }
    }
}
