using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MinimumEffortClicker.App;

/// <summary>
/// Number fields that work with the mouse alone: the wheel over the field and its small up and down arrows (the
/// NumberBox style) add or take away one Step; with Shift held a step is ten times larger. The value stays within
/// Minimum and Maximum. Typing still works as before.
/// </summary>
internal static class Spin
{
    public static readonly RoutedUICommand Up = new("Up", "SpinUp", typeof(Spin));
    public static readonly RoutedUICommand Down = new("Down", "SpinDown", typeof(Spin));

    public static readonly DependencyProperty StepProperty = DependencyProperty.RegisterAttached(
        "Step", typeof(double), typeof(Spin), new PropertyMetadata(0d, StepChanged));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.RegisterAttached(
        "Minimum", typeof(double), typeof(Spin), new PropertyMetadata(0d));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.RegisterAttached(
        "Maximum", typeof(double), typeof(Spin), new PropertyMetadata(1_000_000d));

    public static double GetStep(DependencyObject d) => (double)d.GetValue(StepProperty);
    public static void SetStep(DependencyObject d, double value) => d.SetValue(StepProperty, value);
    public static double GetMinimum(DependencyObject d) => (double)d.GetValue(MinimumProperty);
    public static void SetMinimum(DependencyObject d, double value) => d.SetValue(MinimumProperty, value);
    public static double GetMaximum(DependencyObject d) => (double)d.GetValue(MaximumProperty);
    public static void SetMaximum(DependencyObject d, double value) => d.SetValue(MaximumProperty, value);

    private static void StepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || (double)e.OldValue != 0) return;
        box.PreviewMouseWheel += (_, args) =>
        {
            if (!box.IsEnabled || box.IsReadOnly) return;
            Nudge(box, Math.Sign(args.Delta));
            // The wheel changes the number instead of scrolling the page while the pointer is over the field.
            args.Handled = true;
        };
        box.CommandBindings.Add(new CommandBinding(Up, (_, _) => Nudge(box, 1), (_, a) => a.CanExecute = box.IsEnabled && !box.IsReadOnly));
        box.CommandBindings.Add(new CommandBinding(Down, (_, _) => Nudge(box, -1), (_, a) => a.CanExecute = box.IsEnabled && !box.IsReadOnly));
    }

    private static void Nudge(TextBox box, int direction)
    {
        double step = GetStep(box) * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1);
        double minimum = GetMinimum(box), maximum = GetMaximum(box);
        // An empty or unreadable field starts from its minimum.
        double value = double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var current) ? current : minimum - direction * step;
        // Steps land on whole multiples of the step, so 23 goes up to 30 with a step of 10, not to 33.
        double next = direction > 0 ? Math.Floor(value / step + 1e-9) * step + step : Math.Ceiling(value / step - 1e-9) * step - step;
        next = Math.Clamp(next, minimum, maximum);
        box.Text = next.ToString("0.###", CultureInfo.CurrentCulture);
        box.CaretIndex = box.Text.Length;
    }
}
