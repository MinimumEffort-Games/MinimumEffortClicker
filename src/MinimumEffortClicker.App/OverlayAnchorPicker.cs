using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// The Settings picture of the overlay's anchor: a window with the overlay drawn where it sits. A click anywhere on it
/// pins the overlay to the nearest point of the edge (corners and edge middles snap); while the pointer moves, an outline
/// shows where it would go. The arrow keys move it the way they point, through the corners and edge middles. An anchor from a drag beside the
/// window shows the overlay just outside it.
/// </summary>
internal sealed class OverlayAnchorPicker : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = Register(nameof(Fill), Brushes.Transparent);
    public static readonly DependencyProperty StrokeProperty = Register(nameof(Stroke), Brushes.Gray);
    public static readonly DependencyProperty HighlightProperty = Register(nameof(Highlight), Brushes.LightGreen);
    private const double Margin_ = 22, Inset = 6, OverlayWidth = 58, OverlayHeight = 16;
    private (double X, double Y) anchor = (1, 0);
    private (double X, double Y)? hover;

    /// <summary>A point on the edge was chosen with the mouse or the keyboard.</summary>
    public event Action<double, double>? AnchorChosen;

    public OverlayAnchorPicker()
    {
        Width = 268; Height = 168; Focusable = true; Cursor = Cursors.Hand; FocusVisualStyle = null;
        AutomationProperties.SetName(this, "Overlay anchor point");
    }

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush Highlight { get => (Brush)GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }

    /// <summary>Shows the saved anchor; raises nothing.</summary>
    public void Show(double x, double y) { anchor = (x, y); InvalidateVisual(); }

    private static DependencyProperty Register(string name, Brush fallback)
        => DependencyProperty.Register(name, typeof(Brush), typeof(OverlayAnchorPicker), new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    private Rect Window => new(Margin_, Margin_, Width - 2 * Margin_, Height - 2 * Margin_);

    private (double X, double Y) EdgePoint(Point point)
    {
        var window = Window;
        return OverlayAnchor.OnEdge((point.X - window.X) / window.Width, (point.Y - window.Y) / window.Height);
    }
    protected override void OnMouseMove(MouseEventArgs e) { hover = EdgePoint(e.GetPosition(this)); InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { hover = null; InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true; Focus();
        Choose(EdgePoint(e.GetPosition(this)));
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var (dx, dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0) };
        if (dx == 0 && dy == 0) return;
        e.Handled = true;
        Choose(OverlayAnchor.Step(anchor.X, anchor.Y, dx, dy));
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) => InvalidateVisual();
    private void Choose((double X, double Y) point)
    {
        anchor = point; InvalidateVisual();
        AnchorChosen?.Invoke(point.X, point.Y);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var window = Window;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height)); // The margin takes clicks too.
        var edge = new Pen(Stroke, 1.5);
        dc.DrawRoundedRectangle(Fill, IsKeyboardFocused ? new Pen(Highlight, 2) : edge, window, 4, 4);
        if (hover is { } where && where != anchor)
            dc.DrawRoundedRectangle(null, new Pen(Highlight, 1.5) { DashStyle = DashStyles.Dash }, OverlayAt(where), 3, 3);
        // The little overlay: a bar in the window's line colour with its three buttons at the right end.
        var overlay = OverlayAt(anchor);
        dc.DrawRoundedRectangle(Stroke, null, overlay, 3, 3);
        for (int i = 0; i < 3; i++) dc.DrawRectangle(Fill, null, new Rect(overlay.Right - 7 - i * 7, overlay.Top + 5, 5, 6));
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
