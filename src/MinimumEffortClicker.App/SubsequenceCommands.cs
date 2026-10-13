using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class SequenceEditor
{
    private void MakeSubsequence_Click(object sender, RoutedEventArgs e)
    {
        var indices = SelectedIndices();
        if (indices.Length == 0 || indices[^1] - indices[0] + 1 != indices.Length)
        { MessageLabel.Text = "Select one contiguous range of actions to make a reusable subsequence."; return; }
        Edit(source =>
        {
            var library = source.Subsequences ?? [];
            int number = 1;
            while (library.Any(b => b.Name == $"Subsequence {number}")) number++;
            var body = new ReusableSequence(Guid.NewGuid(), $"Subsequence {number}", source.Steps.Skip(indices[0]).Take(indices.Length).ToArray());
            return SequenceEdits.Replace(source with { Subsequences = [.. library, body] }, indices[0], indices.Length, [SequenceStep.Call(body.Name, body.Id)], points);
        }, "Selection is now a reusable subsequence. Insert more calls from Subsequences; Undo restores the original actions.");
    }

    private void Subsequences_Click(object sender, RoutedEventArgs e)
    {
        var window = new Window { Owner = this, Title = "Reusable subsequences", Width = 620, Height = 530, MinWidth = 460, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(BackgroundProperty, "AppBackground"); window.SetResourceReference(ForegroundProperty, "TextPrimary");
        foreach (var type in new[] { typeof(Button), typeof(TextBox) }) if (TryFindResource(type) is Style style) window.Resources[type] = style;
        var panel = new DockPanel { Margin = new Thickness(24) }; window.Content = panel;
        var heading = new TextBlock { Text = "Calls share one definition in this sequence. Editing it updates every call. All definitions travel with profile exports. Editing never starts playback.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        var name = new TextBox { MaxLength = 100 }; AutomationProperties.SetName(name, "Subsequence name"); bottom.Children.Add(name);
        var buttons = new WrapPanel(); bottom.Children.Add(buttons);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap }; bottom.Children.Add(message);
        var list = new ListBox { DisplayMemberPath = "Name" }; AutomationProperties.SetName(list, "Reusable subsequences");
        list.SetResourceReference(BackgroundProperty, "Surface"); list.SetResourceReference(ForegroundProperty, "TextPrimary"); panel.Children.Add(list);
        void Refresh(Guid? selected = null)
        { list.ItemsSource = history.Current.Subsequences ?? []; list.SelectedItem = (history.Current.Subsequences ?? []).FirstOrDefault(b => b.Id == selected); if (list.SelectedIndex < 0) list.SelectedIndex = 0; }
        list.SelectionChanged += (_, _) => name.Text = (list.SelectedItem as ReusableSequence)?.Name ?? "";
        void Button(string title, Action<ReusableSequence> action)
        {
            var button = new Button { Content = title }; buttons.Children.Add(button);
            button.Click += (_, _) =>
            {
                if (list.SelectedItem is not ReusableSequence body) { message.Text = "Use Make subsequence on selected actions first."; return; }
                try { action(body); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { message.Text = ex.Message; }
            };
        }
        void Commit(SequenceDefinition updated, Guid? id)
        { history.Commit(updated); Refresh(id); this.Refresh(StepsList.SelectedIndex); message.Text = "Updated in the draft; save the sequence to keep it."; }
        Button("Insert call", body => { Add(SequenceStep.Call(body.Name, body.Id)); window.Close(); });
        Button("Edit actions", body =>
        {
            var editor = new SequenceEditor(new(body.Steps, history.Current.KeyboardLayout, history.Current.Subsequences), runtime, points, allowRepetition: false) { Owner = window };
            if (editor.ShowDialog() != true) return;
            var library = editor.Result.Subsequences ?? [];
            Commit(history.Current with { Subsequences = library.Select(b => b.Id == body.Id ? b with { Steps = editor.Result.Steps } : b).ToArray() }, body.Id);
        });
        Button("Rename", body => Commit(history.Current with { Subsequences = history.Current.Subsequences!.Select(b => b.Id == body.Id ? b with { Name = name.Text.Trim() } : b).ToArray() }, body.Id));
        Button("Delete unused", body => Commit(history.Current with { Subsequences = history.Current.Subsequences!.Where(b => b.Id != body.Id).ToArray() }, null));
        Refresh(); window.ShowDialog();
    }
}
