using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class TimingWindow : Window
{
    private readonly SequencePlan plan;
    private readonly IReadOnlyList<TimelineEntry> entries;
    private readonly Func<InputControl, string> label;
    public int? SelectedStepIndex { get; private set; }
    private sealed record Row(int StepIndex, string Label, string Start, string Duration, double Left, double BarWidth, double TrackWidth, string Detail);
    public TimingWindow(Window owner, SequencePlan plan, Func<InputControl, string> label)
    {
        this.plan = plan; this.label = label; entries = SequenceTimeline.Create(plan);
        Owner = owner;
        InitializeComponent();
        if (owner.TryFindResource(typeof(Button)) is Style buttons) Resources[typeof(Button)] = buttons;
        Summary.Text = $"{plan.Duration.TotalSeconds:0.###} seconds fixed timing · {plan.Conditions.Count} waits / decisions · ≤ {plan.WorstCaseDuration.TotalSeconds:0.###} seconds including bounded paths (before runtime pacing)";
        Loaded += (_, _) => RefreshRows();
        Rows.SizeChanged += (_, _) => RefreshRows();
        RefreshRows();
    }
    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Rows is not null) RefreshRows(); }
    private void RefreshRows()
    {
        int selected = Rows.SelectedIndex;
        double width = Math.Max(200, Rows.ActualWidth - 495) * Zoom.Value;
        double total = Math.Max(1, plan.Duration.TotalMilliseconds);
        Rows.ItemsSource = entries.Select(e =>
        {
            string duration = e.Flow is { } flow ? (flow.Kind == FlowKind.IfElse ? "If / Else" : $"Repeat ≤ {flow.MaximumIterations}") : e.Condition is { } condition ? $"≤ {condition.Policy.Timeout.TotalSeconds * (condition.Policy.Retries + 1):0.###} s wait" : $"{e.Duration.TotalMilliseconds:0.###} ms";
            string name = e.Control is { } control ? $"{e.StepIndex + 1}. {e.Name} · {label(control)}" : $"{e.StepIndex + 1}. {e.Name} · condition";
            double left = e.Start.TotalMilliseconds / total * width;
            return new Row(e.StepIndex, name, e.Start.TotalMilliseconds.ToString("0.###", CultureInfo.CurrentCulture), duration,
                left, Math.Max(3, e.Duration.TotalMilliseconds / total * width), width + 4,
                $"{name}\nNominal start {e.Start.TotalMilliseconds:0.###} ms · {duration}" + (e.Condition is { } c ? $"\n{c.Kind}; timeout: {c.Policy.TimeoutAction}" : ""));
        }).ToArray();
        Rows.SelectedIndex = selected >= 0 ? selected : entries.Count > 0 ? 0 : -1;
    }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Rows.SelectedItem is not Row row) return;
        SelectedStepIndex = row.StepIndex; DialogResult = true;
    }
}
