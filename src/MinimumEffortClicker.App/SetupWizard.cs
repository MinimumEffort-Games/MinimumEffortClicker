using System.Windows;
using System.Windows.Controls;
using MinimumEffortClicker.Windows;

namespace MinimumEffortClicker.App;

internal sealed class SetupWizard : Window
{
    public string TargetName { get; private set; } = "";
    public SetupWizard(Window owner, DesktopRuntime runtime, string guide)
    {
        Owner = owner; Width = 470; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = guide == "record" ? "Record a macro — guided setup" : "Image and colour — guided setup";
        SetResourceReference(BackgroundProperty, "AppBackground"); SetResourceReference(ForegroundProperty, "TextPrimary");
        foreach (var type in new[] { typeof(Button), typeof(ComboBox) })
            if (owner.TryFindResource(type) is Style style) Resources[type] = style;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        var heading = new TextBlock { FontSize = 22, Text = "1. Choose the app", Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(heading);
        var hint = new TextBlock { Text = "Only this window will receive playback or be recorded.", TextWrapping = TextWrapping.Wrap }; panel.Children.Add(hint);
        var targets = new ComboBox { ItemsSource = DesktopRuntime.ListTargets(), DisplayMemberPath = "Title", Margin = new Thickness(0, 12, 0, 12), MinWidth = 350 }; panel.Children.Add(targets);
        System.Windows.Automation.AutomationProperties.SetName(targets, "Click target for guided setup");
        var refresh = new Button { Content = "Refresh windows", Margin = new Thickness(0, 0, 0, 12) }; refresh.Click += (_, _) => targets.ItemsSource = DesktopRuntime.ListTargets(); panel.Children.Add(refresh);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(error);
        var next = new Button { Content = "Next", Padding = new Thickness(12, 7, 12, 7) }; panel.Children.Add(next);
        int step = 0;
        next.Click += (_, _) =>
        {
            try
            {
                if (step == 0)
                {
                    if (targets.SelectedItem is not TargetWindow target) { error.Text = "Select an app from the list."; return; }
                    runtime.SelectTarget(target); TargetName = target.Title;
                    targets.Visibility = refresh.Visibility = Visibility.Collapsed; error.Text = ""; step++;
                    heading.Text = guide == "record" ? "2. Record, then fine-tune" : "2. Choose what to watch";
                    hint.Text = guide == "record"
                        ? "In the editor, F10 starts and ends recording. The app comes forward automatically. Perform your actions, then press F10 again. Review the recording and edit any step's key, click, wait or duration. Duplicate and reorder steps before saving."
                        : "Choose an image or colour condition. Use Select area to drag a search zone on a captured view of the app. For images, capture a small template first, then select a larger search zone. Set a timeout, then add the action to perform after the match.";
                    next.Content = "Open editor";
                }
                else DialogResult = true;
            }
            catch (InvalidOperationException ex) { error.Text = ex.Message; }
        };
    }
}
