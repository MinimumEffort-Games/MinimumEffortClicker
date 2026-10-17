using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace MinimumEffortClicker.App;

/// <summary>
/// Number fields that work with the mouse alone: the wheel over the field and its small up and down arrows add or take
/// away one Step; with Shift held a step is ten times larger. The value stays within Minimum and Maximum. Typing still
/// works as before. Set Spin.Step on a TextBox to turn it on.
/// </summary>
internal sealed class Spin : AvaloniaObject
{
    public static readonly AttachedProperty<double> StepProperty = AvaloniaProperty.RegisterAttached<Spin, TextBox, double>("Step");
    public static readonly AttachedProperty<double> MinimumProperty = AvaloniaProperty.RegisterAttached<Spin, TextBox, double>("Minimum");
    public static readonly AttachedProperty<double> MaximumProperty = AvaloniaProperty.RegisterAttached<Spin, TextBox, double>("Maximum", 1_000_000d);

    public static double GetStep(TextBox box) => box.GetValue(StepProperty);
    public static void SetStep(TextBox box, double value) => box.SetValue(StepProperty, value);
    public static double GetMinimum(TextBox box) => box.GetValue(MinimumProperty);
    public static void SetMinimum(TextBox box, double value) => box.SetValue(MinimumProperty, value);
    public static double GetMaximum(TextBox box) => box.GetValue(MaximumProperty);
    public static void SetMaximum(TextBox box, double value) => box.SetValue(MaximumProperty, value);

    static Spin()
    {
        StepProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            if (e.OldValue is double old && old != 0) return;
            box.InnerRightContent = Arrows(box);
            // The wheel changes the number instead of scrolling the page while the pointer is over the field.
            box.AddHandler(InputElement.PointerWheelChangedEvent, (_, args) =>
            {
                if (!box.IsEnabled || box.IsReadOnly || args.Delta.Y == 0) return;
                Nudge(box, Math.Sign(args.Delta.Y), args.KeyModifiers);
                args.Handled = true;
            }, RoutingStrategies.Tunnel);
        });
    }

    private static Control Arrows(TextBox box)
    {
        // At least 22 high and stretched, so a field that centres its text still gets two arrows, not one squashed pair.
        var panel = new Grid { RowDefinitions = new RowDefinitions("*,*"), Width = 18, MinHeight = 22, Margin = new Thickness(0, 1, 1, 1), VerticalAlignment = VerticalAlignment.Stretch };
        panel.Children.Add(Arrow(box, 1, "M0,5 L5,0 L10,5 Z", "Up (Shift: ten steps)", 0));
        panel.Children.Add(Arrow(box, -1, "M0,0 L5,5 L10,0 Z", "Down (Shift: ten steps)", 1));
        return panel;
    }

    private static RepeatButton Arrow(TextBox box, int direction, string shape, string tip, int row)
    {
        var button = new RepeatButton
        {
            Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(shape), Width = 8, Height = 4, Stretch = Stretch.Uniform },
            Padding = new Thickness(0), MinHeight = 0, Focusable = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Delay = 400, Interval = 60
        };
        ((Avalonia.Controls.Shapes.Path)button.Content).Bind(Shape.FillProperty, button.GetResourceObservable("TextPrimary"));
        ToolTip.SetTip(button, tip);
        Grid.SetRow(button, row);
        // A click carries no modifiers, so Shift is read when the arrow is pressed and used for its repeats too.
        var modifiers = KeyModifiers.None;
        button.AddHandler(InputElement.PointerPressedEvent, (_, e) => modifiers = e.KeyModifiers, RoutingStrategies.Tunnel);
        button.Click += (_, _) => { if (box.IsEnabled && !box.IsReadOnly) Nudge(box, direction, modifiers); };
        return button;
    }

    private static void Nudge(TextBox box, int direction, KeyModifiers modifiers)
    {
        double step = GetStep(box) * (modifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1);
        double minimum = GetMinimum(box), maximum = GetMaximum(box);
        // An empty or unreadable field starts from its minimum.
        double value = double.TryParse((box.Text ?? "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var current) ? current : minimum - direction * step;
        // Steps land on whole multiples of the step, so 23 goes up to 30 with a step of 10, not to 33.
        double next = direction > 0 ? Math.Floor(value / step + 1e-9) * step + step : Math.Ceiling(value / step - 1e-9) * step - step;
        box.Text = Math.Clamp(next, minimum, maximum).ToString("0.###", CultureInfo.CurrentCulture);
        box.CaretIndex = box.Text.Length;
    }
}
