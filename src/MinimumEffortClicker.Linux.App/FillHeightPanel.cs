using Avalonia;
using Avalonia.Controls;

namespace MinimumEffortClicker.App;

/// <summary>
/// Holds a list that takes whatever height its row is given. Inside a scrolling page a list would otherwise ask for the
/// height of all its items, so the page would grow instead of the list scrolling. This panel asks for no height of its
/// own (its MinHeight still applies) and gives the list exactly the height it is arranged at.
/// </summary>
public sealed class FillHeightPanel : Decorator
{
    private double arranged;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null) return default;
        double height = arranged > 0 ? arranged : MinHeight;
        if (!double.IsInfinity(availableSize.Height)) height = Math.Min(height, availableSize.Height);
        Child.Measure(new Size(availableSize.Width, height));
        return new Size(Child.DesiredSize.Width, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null) return finalSize;
        // The final height is only known now: measure the list again at that height so it shows enough rows.
        if (finalSize.Height != arranged) { arranged = finalSize.Height; Child.Measure(finalSize); }
        Child.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
