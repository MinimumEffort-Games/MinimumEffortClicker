using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class SequenceEditor
{
    private sealed record FlowChoice(Guid? Id, string Name) { public override string ToString() => Name; }
    private void AddFlow_Click(object sender, RoutedEventArgs e) => ConfigureFlow(null);
    private void EditFlow_Click(object sender, RoutedEventArgs e)
    { if (StepsList.SelectedIndex is var i && i >= 0) ConfigureFlow(i); }
    private void ConfigureFlow(int? editIndex)
    {
        var library = history.Current.Subsequences ?? [];
        if (library.Count == 0) { MessageLabel.Text = "Make a reusable subsequence first; branches and repeats select reusable bodies."; return; }
        var original = editIndex is { } at ? history.Current.Steps[at] : null;
        var condition = original?.Condition ?? new(ConditionKind.WindowActive, new(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10)));
        var window = new Window { Owner = this, Title = "Conditional flow", Width = 570, Height = 630, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(BackgroundProperty, "AppBackground"); window.SetResourceReference(ForegroundProperty, "TextPrimary");
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox) }) if (TryFindResource(type) is Style style) window.Resources[type] = style;
        var panel = new StackPanel { Margin = new Thickness(24) }; window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) });
        Label("A fresh true or false observation selects a path. Timeout, ambiguity and unavailable capture never select Else. Repeat checks before the first body and after each iteration; reaching its bound stops this run.");
        var kind = new ComboBox { ItemsSource = new[] { "If true / Else", "Repeat body until true" }, SelectedIndex = (int)(original?.Flow?.Kind ?? FlowKind.IfElse) }; AutomationProperties.SetName(kind, "Flow type"); panel.Children.Add(kind);
        var conditionButton = new Button { Content = "Edit condition · " + condition.Kind }; panel.Children.Add(conditionButton);
        conditionButton.Click += (_, _) => { var dialog = new ConditionEditor(window, runtime, condition); if (dialog.ShowDialog() == true) { condition = dialog.Result!; conditionButton.Content = "Edit condition · " + condition.Kind; } };
        Label("Then / repeat body");
        var body = new ComboBox { ItemsSource = library.Select(b => new FlowChoice(b.Id, b.Name)).ToArray() }; AutomationProperties.SetName(body, "Then or repeat subsequence"); panel.Children.Add(body);
        body.SelectedItem = body.Items.Cast<FlowChoice>().FirstOrDefault(b => b.Id == original?.Flow?.BodyId) ?? body.Items[0];
        Label("Else body (If only)");
        var otherwise = new ComboBox { ItemsSource = new[] { new FlowChoice(null, "No action") }.Concat(library.Select(b => new FlowChoice(b.Id, b.Name))).ToArray() }; AutomationProperties.SetName(otherwise, "Else subsequence"); panel.Children.Add(otherwise);
        otherwise.SelectedItem = otherwise.Items.Cast<FlowChoice>().First(b => b.Id == original?.Flow?.ElseId);
        Label("Maximum body iterations (Repeat only, 1–1000)");
        var maximum = new TextBox { Text = (original?.Flow?.MaximumIterations ?? 10).ToString() }; AutomationProperties.SetName(maximum, "Maximum repeat iterations"); panel.Children.Add(maximum);
        void Update() { otherwise.IsEnabled = kind.SelectedIndex == 0; maximum.IsEnabled = kind.SelectedIndex == 1; }
        kind.SelectionChanged += (_, _) => Update(); Update();
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var save = new Button { Content = "Use flow", IsDefault = true }; panel.Children.Add(save);
        panel.Children.Add(new Button { Content = "Cancel", IsCancel = true });
        save.Click += (_, _) =>
        {
            try
            {
                var flow = new ConditionalFlow((FlowKind)kind.SelectedIndex, ((FlowChoice)body.SelectedItem).Id!.Value,
                    kind.SelectedIndex == 0 ? ((FlowChoice)otherwise.SelectedItem).Id : null, kind.SelectedIndex == 1 ? int.Parse(maximum.Text) : 1);
                var step = (original ?? SequenceStep.WaitUntil(kind.SelectedIndex == 0 ? "If condition" : "Repeat until", condition)) with { Kind = StepKind.ConditionalFlow, Condition = condition, Flow = flow };
                var next = SequenceEdits.Replace(history.Current, editIndex ?? StepsList.SelectedIndex + 1, editIndex is null ? 0 : 1, [step], points);
                history.Commit(next); Refresh(editIndex ?? StepsList.SelectedIndex + 1); window.DialogResult = true;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { error.Text = ex.Message; }
        };
        window.ShowDialog();
    }
}
