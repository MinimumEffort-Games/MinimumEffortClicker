using System.Windows;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private void TimingOptions_Changed(object sender, RoutedEventArgs e)
    { if (!initialized || loadingQuick) return; ShowTimingOptions(); TestResultLabel.Text = ""; }
    private void ShowTimingOptions()
    {
        RandomRanges.Visibility = RandomTimingBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        MaximumPressFields.Visibility = ModeBox.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
        AfterLabel.Text = "Pause after this action (ms)";
        MaxAfterLabel.Text = ModeBox.SelectedIndex == 2 ? "Maximum pause duration (ms)" : "Maximum pause after (ms)";
    }
    private static string TimingSummary(SequenceStep step)
    {
        if (step.Kind == StepKind.Screenshot) return $"Save PNG · {Ms(step.Duration)} ms pause after saving";
        string motion = step.Motion is { } m ? (m.MoveDuration > TimeSpan.Zero ? $" · move {Ms(m.MoveDuration)} ms" : "")
            + (m.OffsetPixels > 0 ? $" · position ±{m.OffsetPixels} px" : "") : "";
        if (step.Timing is not { } timing) return $"{Ms(step.Duration)} ms" + motion;
        var press = step.Children?.FirstOrDefault()?.Duration ?? (step.Kind == StepKind.Input ? step.Duration : TimeSpan.Zero);
        string summary = timing.Interval is { } interval ? $"Interval {Ms(interval)}–{Ms(timing.MaximumInterval ?? interval)} ms" : $"{Ms(step.Duration)} ms minimum";
        if (timing.MaximumPressDuration is { } maxPress) summary += $" · press {Ms(press)}–{Ms(maxPress)} ms";
        if (timing.MaximumPause is { } maxPause) summary += $" · pause up to {Ms(maxPause)} ms";
        return summary + motion;
    }
}
