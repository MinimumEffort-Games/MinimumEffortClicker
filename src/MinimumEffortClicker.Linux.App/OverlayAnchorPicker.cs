using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// The Settings picture of the overlay's anchor: a window with the overlay drawn where it sits. A click anywhere on it
/// pins the overlay to the nearest point of the edge (corners and edge middles snap); while the pointer moves, an outline
/// shows where it would go. The arrow keys move it the way they point, through the corners and edge middles. An anchor from a drag beside the
/// window shows the overlay just outside it.
/// </summary>
internal sealed class OverlayAnchorPicker : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<OverlayAnchorPicker, IBrush?>(nameof(Fill), Brushes.Transparent);
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<OverlayAnchorPicker, IBrush?>(nameof(Stroke), Brushes.Gray);
    public static readonly StyledProperty<IBrush?> HighlightProperty = AvaloniaProperty.Register<OverlayAnchorPicker, IBrush?>(nameof(Highlight), Brushes.LightGreen);
    private const double Margin_ = 22, Inset = 6, OverlayWidth = 58, OverlayHeight = 16;
    private (double X, double Y) anchor = (1, 0);
    private (double X, double Y)? hover;

    static OverlayAnchorPicker() => AffectsRender<OverlayAnchorPicker>(FillProperty, StrokeProperty, HighlightProperty);

    /// <summary>A point on the edge was chosen with the mouse or the keyboard.</summary>
    public event Action<double, double>? AnchorChosen;

    public OverlayAnchorPicker()
    {
        Width = 268; Height = 168; Focusable = true; Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, "Overlay anchor point");
    }

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public IBrush? Highlight { get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }

    /// <summary>Shows the saved anchor; raises nothing.</summary>
    public void Show(double x, double y) { anchor = (x, y); InvalidateVisual(); }

    private Rect Window => new(Margin_, Margin_, Width - 2 * Margin_, Height - 2 * Margin_);

    private (double X, double Y) EdgePoint(Point point)
    {
        var window = Window;
        return OverlayAnchor.OnEdge((point.X - window.X) / window.Width, (point.Y - window.Y) / window.Height);
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); hover = EdgePoint(e.GetPosition(this)); InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); hover = null; InvalidateVisual(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true; Focus();
        Choose(EdgePoint(e.GetPosition(this)));
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var (dx, dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0) };
        if (dx == 0 && dy == 0) return;
        e.Handled = true;
        Choose(OverlayAnchor.Step(anchor.X, anchor.Y, dx, dy));
    }
    protected override void OnGotFocus(FocusChangedEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }
    private void Choose((double X, double Y) point)
    {
        anchor = point; InvalidateVisual();
        AnchorChosen?.Invoke(point.X, point.Y);
    }

    public override void Render(DrawingContext context)
    {
        var window = Window;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // The margin takes clicks too.
        var edge = new Pen(Stroke, 1.5);
        context.DrawRectangle(Fill, IsFocused ? new Pen(Highlight, 2) : edge, window, 4, 4);
        if (hover is { } where && where != anchor)
            context.DrawRectangle(null, new Pen(Highlight, 1.5, DashStyle.Dash), OverlayAt(where), 3, 3);
        // The little overlay: a bar in the window's line colour with its three buttons at the right end.
        var overlay = OverlayAt(anchor);
        context.DrawRectangle(Stroke, null, overlay, 3, 3);
        for (int i = 0; i < 3; i++) context.FillRectangle(Fill ?? Brushes.Transparent, new Rect(overlay.Right - 7 - i * 7, overlay.Top + 5, 5, 6));
    }
    /// <summary>The little overlay for an anchor; one dragged far outside the window is drawn just beyond its edge.</summary>
    private Rect OverlayAt((double X, double Y) at)
    {
        var window = Window;
        double reachX = (Margin_ - 4) / Math.Max(1, window.Width - OverlayWidth - 2 * Inset), reachY = (Margin_ - 4) / Math.Max(1, window.Height - OverlayHeight - 2 * Inset);
        var (x, y) = OverlayAnchor.Place(window.X, window.Y, window.Width, window.Height, OverlayWidth, OverlayHeight,
            Math.Clamp(at.X, -reachX, 1 + reachX), Math.Clamp(at.Y, -reachY, 1 + reachY), Inset);
        return new Rect(x, y, OverlayWidth, OverlayHeight);
    }
}
