using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

/// <summary>
/// Chooses a mouse button by clicking it on the labelled mouse picture. The chosen button's outline is green, the one
/// under the pointer is outlined in the focus colour. Which button a point belongs to comes from the button map that
/// scripts/Build-UiArt.ps1 traces from the picture, so the clickable shapes match the drawn ones.
/// </summary>
internal sealed class MouseButtonPicker : Grid
{
    // Same order and map values as the mouse section of scripts/Build-UiArt.ps1 (red channel 40, 80, ...).
    private static readonly (PointerButton Button, string Art, string Name)[] Buttons =
    [
        (PointerButton.Left, "left", "Left mouse button"), (PointerButton.Middle, "middle", "Middle mouse button"),
        (PointerButton.Right, "right", "Right mouse button"), (PointerButton.XButton1, "side1", "Side button 4 (back)"),
        (PointerButton.XButton2, "side2", "Side button 5 (forward)")
    ];
    private readonly Rectangle[] outlines = new Rectangle[Buttons.Length];
    private readonly (byte[] Pixels, int Width, int Height)? map = UiArt.MouseButtonMap();
    private PointerButton? selected, hovered;

    public event EventHandler? SelectedChanged;

    public MouseButtonPicker()
    {
        Background = Brushes.Transparent; Focusable = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Children.Add(new Image { Source = UiArt.MouseButtons, Stretch = Stretch.Uniform });
        for (int i = 0; i < Buttons.Length; i++)
        {
            outlines[i] = new Rectangle { Visibility = Visibility.Hidden, IsHitTestVisible = false, OpacityMask = new ImageBrush(UiArt.MouseButtonOutline(Buttons[i].Art)) { Stretch = Stretch.Uniform } };
            Children.Add(outlines[i]);
        }
        System.Windows.Automation.AutomationProperties.SetName(this, "Mouse button");
    }

    public PointerButton? Selected
    {
        get => selected;
        set { if (selected == value) return; selected = value; ShowOutlines(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var over = ButtonAt(e.GetPosition(this));
        if (over == hovered) return;
        hovered = over;
        Cursor = over is null ? null : Cursors.Hand;
        ToolTip = over is { } b ? Buttons.First(x => x.Button == b).Name : null;
        ShowOutlines();
    }

    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); hovered = null; ShowOutlines(); }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (ButtonAt(e.GetPosition(this)) is { } button) { Selected = button; e.Handled = true; }
    }

    private void ShowOutlines()
    {
        for (int i = 0; i < Buttons.Length; i++)
        {
            bool chosen = Buttons[i].Button == selected, over = Buttons[i].Button == hovered;
            outlines[i].Visibility = chosen || over ? Visibility.Visible : Visibility.Hidden;
            outlines[i].SetResourceReference(Shape.FillProperty, chosen ? "SuccessBorder" : "FocusRing");
        }
    }

    /// <summary>The button drawn at a point of this control, through the button map (the picture keeps its shape, so
    /// the map is placed the same way: uniformly scaled and centred).</summary>
    private PointerButton? ButtonAt(Point point)
    {
        if (map is not { } m || ActualWidth <= 0 || ActualHeight <= 0) return null;
        double scale = Math.Min(ActualWidth / m.Width, ActualHeight / m.Height);
        int x = (int)((point.X - (ActualWidth - m.Width * scale) / 2) / scale), y = (int)((point.Y - (ActualHeight - m.Height * scale) / 2) / scale);
        if (x < 0 || y < 0 || x >= m.Width || y >= m.Height) return null;
        int offset = (y * m.Width + x) * 4;
        if (m.Pixels[offset + 3] < 128) return null;
        int index = (m.Pixels[offset + 2] + 20) / 40 - 1;
        return index >= 0 && index < Buttons.Length ? Buttons[index].Button : null;
    }
}
