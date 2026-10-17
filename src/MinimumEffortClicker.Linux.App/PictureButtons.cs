using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace MinimumEffortClicker.App;

/// <summary>
/// A picture whose buttons can be clicked: the labelled mouse or the controller. Each button's traced outline can be
/// shown: green for the chosen one, red for ones that are taken, the hover colour under the pointer. Which button a
/// point belongs to comes from the button map scripts/Build-UiArt.ps1 traces from the picture, so the clickable shapes
/// match the drawn ones.
/// </summary>
internal class PictureButtons : Panel
{
    /// <param name="Value">The button's value in the map (see Trace-Picture in scripts/Build-UiArt.ps1).</param>
    /// <param name="Art">The outline mask's name after the picture's name.</param>
    protected sealed record Part(int Value, string Art, string Name);

    private readonly Part[] parts;
    private readonly Rectangle[] outlines;
    private readonly (byte[] Pixels, int Width, int Height)? map;
    private readonly Dictionary<int, string> taken = [];
    private int? selected, hovered;

    /// <summary>A button was clicked (its map value); taken buttons are reported too, so the caller can say why not.</summary>
    public event Action<int>? ButtonClicked;

    protected PictureButtons(string picture, Part[] parts)
    {
        this.parts = parts;
        Background = Brushes.Transparent; Focusable = true;
        if (UiArt.Device(picture) is { } source)
        {
            var image = new Image { Source = source, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
            Children.Add(image);
        }
        outlines = new Rectangle[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            outlines[i] = new Rectangle { IsVisible = false, IsHitTestVisible = false };
            if (UiArt.Device($"{picture}-{parts[i].Art}") is { } mask) outlines[i].OpacityMask = new ImageBrush(mask) { Stretch = Stretch.Uniform };
            Children.Add(outlines[i]);
        }
        map = UiArt.DeviceMap(picture + "-map");
    }

    /// <summary>The chosen button's map value, outlined green; null for none.</summary>
    protected int? SelectedValue
    {
        get => selected;
        set { selected = value; ShowOutlines(); }
    }

    /// <summary>Marks a button as taken (red outline) with the reason shown on hover, or frees it with null.</summary>
    protected void SetTaken(int value, string? why)
    {
        if (why is null) taken.Remove(value); else taken[value] = why;
        ShowOutlines();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var over = ValueAt(e.GetPosition(this));
        if (over == hovered) return;
        hovered = over;
        Cursor = over is null ? null : new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, over is { } v ? parts.First(p => p.Value == v).Name + (taken.TryGetValue(v, out var why) ? ": " + why : "") : null);
        ShowOutlines();
    }

    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); hovered = null; ShowOutlines(); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left && ValueAt(e.GetPosition(this)) is { } value) { ButtonClicked?.Invoke(value); e.Handled = true; }
    }

    private void ShowOutlines()
    {
        for (int i = 0; i < parts.Length; i++)
        {
            int value = parts[i].Value;
            string? colour = value == hovered ? "FocusRing" : value == selected ? "SuccessBorder" : taken.ContainsKey(value) ? "StopBorder" : null;
            outlines[i].IsVisible = colour is not null;
            if (colour is not null) outlines[i].SetResourceReference(Shape.FillProperty, colour);
        }
    }

    /// <summary>The button drawn at a point of this control, through the button map (placed like the picture:
    /// uniformly scaled and centred).</summary>
    private int? ValueAt(Point point)
    {
        if (map is not { } m || Bounds.Width <= 0 || Bounds.Height <= 0) return null;
        double scale = Math.Min(Bounds.Width / m.Width, Bounds.Height / m.Height);
        int x = (int)((point.X - (Bounds.Width - m.Width * scale) / 2) / scale), y = (int)((point.Y - (Bounds.Height - m.Height * scale) / 2) / scale);
        if (x < 0 || y < 0 || x >= m.Width || y >= m.Height) return null;
        int value = m.Pixels[y * m.Width + x];
        return value != 0 && parts.Any(p => p.Value == value) ? value : null;
    }
}
