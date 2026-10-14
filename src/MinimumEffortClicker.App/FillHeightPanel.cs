using System.Windows;
using System.Windows.Controls;

namespace MinimumEffortClicker.App;

/// <summary>
/// Holds a list that takes whatever height its row is given. Inside a scrolling page a list would otherwise ask for the
/// height of all its items, so the page would grow instead of the list scrolling. This panel asks for no height of its
/// own (its MinHeight still applies) and gives the list exactly the height it is arranged at.
/// </summary>
public sealed class FillHeightPanel : Decorator
{
    private double arranged;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return default;
        double height = arranged > 0 ? arranged : MinHeight;
        if (!double.IsInfinity(constraint.Height)) height = Math.Min(height, constraint.Height);
        Child.Measure(new Size(constraint.Width, height));
        return new Size(Child.DesiredSize.Width, 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is null) return arrangeSize;
        // The final height is only known now: measure the list again at that height so it shows enough rows.
        if (arrangeSize.Height != arranged) { arranged = arrangeSize.Height; Child.Measure(arrangeSize); }
        Child.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }
}
