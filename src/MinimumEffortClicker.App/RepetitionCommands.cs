using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class SequenceEditor
{
    private void Repetition_Click(object sender, RoutedEventArgs e)
    {
        var current = history.Current.Repetition ?? new();
        var dialog = new Window { Title = "Sequence repetition", Owner = this, Width = 540, Height = 550,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(BackgroundProperty, "AppBackground"); dialog.SetResourceReference(ForegroundProperty, "TextPrimary");
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox) }) if (TryFindResource(type) is Style style) dialog.Resources[type] = style;
        var panel = new StackPanel { Margin = new Thickness(24) }; dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) });
        Label("A loop completes the entire sequence, including its chosen branches. Nested calls do not add loops. Stop All and focus loss still stop immediately.");
        var mode = new ComboBox { ItemsSource = new[] { "Once", "Completed loop count", "Active duration", "Until stopped" },
            SelectedIndex = current.ActiveDuration is not null ? 2 : current.Count == 0 ? 3 : current.Count == 1 ? 0 : 1 };
        AutomationProperties.SetName(mode, "Sequence repetition mode"); panel.Children.Add(mode);
        Label("Completed loops (count mode) or active seconds (duration mode)");
        var value = new TextBox { Text = current.ActiveDuration?.TotalSeconds.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? (current.Count > 1 ? current.Count : 10).ToString() };
        AutomationProperties.SetName(value, "Loop count or active seconds"); panel.Children.Add(value);
        Label("Delay between loops · milliseconds (separate from action gaps)");
        var delay = new TextBox { Text = Time(current.DelayBetweenLoops) }; AutomationProperties.SetName(delay, "Delay between loops in milliseconds"); panel.Children.Add(delay);
        Label("Duration includes inter-loop waits and excludes startup countdowns. Instant loops have a 10 ms minimum cadence. Until stopped is explicit and never enables a start binding.");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var apply = new Button { Content = "Use repetition", IsDefault = true }; panel.Children.Add(apply);
        panel.Children.Add(new Button { Content = "Cancel", IsCancel = true });
        void Update() { value.IsEnabled = mode.SelectedIndex is 1 or 2; delay.IsEnabled = mode.SelectedIndex != 0; }
        mode.SelectionChanged += (_, _) => Update(); Update();
        apply.Click += (_, _) =>
        {
            try
            {
                var repetition = new SequenceRepetition(mode.SelectedIndex switch { 0 => 1, 1 => int.Parse(value.Text), _ => 0 },
                    mode.SelectedIndex == 0 ? TimeSpan.Zero : Parse(delay.Text), mode.SelectedIndex == 2 ? Parse(value.Text) * 1000 : null);
                repetition.Validate(); history.Commit(history.Current with { Repetition = repetition });
                Refresh(StepsList.SelectedIndex); MessageLabel.Text = "Repetition updated in the draft. Playback remains stopped."; dialog.DialogResult = true;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { error.Text = ex.Message; }
        };
        dialog.ShowDialog();
    }
    private void ScaleGaps_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(PaceBox.Text, out double factor) || !double.IsFinite(factor) || factor is < 0 or > 100)
        { MessageLabel.Text = "Enter a gap multiplier from 0 to 100. Holds and overlap stay unchanged."; return; }
        PreviewTimingEdit(s => SequenceEdits.ChangeIdleGaps(s, gap => gap * factor, points));
    }
}
