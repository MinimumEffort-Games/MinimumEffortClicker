using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private void Help_Click(object sender, RoutedEventArgs e) => ShowText("Minimum Effort Clicker · help", """
        Everything starts stopped
        Nothing runs until you press Start, a toggle shortcut, or the Actions start key. Loading a macro, importing, or switching windows never starts anything. F9 (Stop all) stops every action, toggle and recording at once and releases any key or button the clicker is holding.

        Actions
        Pick a Click target: Desktop uses physical screen coordinates; choosing a window uses coordinates inside that window and follows it when it moves. Choose an action (click, key press, pause, wait for a colour, screenshot), fill in its settings and press Add Action. Select a row to edit it; use the arrows, copy, delete and undo buttons to arrange the list. Test action runs only the selected action after a short countdown. Start (F8) runs the whole list once, or forever with Loop checked. F10 records your own clicks and keys into the list; Record pauses keeps the time between them as editable pauses.

        Save Macro writes the list to a .mecprofile file in the Macros folder beside your data. Load Macro opens one. Files only contain actions, timing and any captured colours or templates; never hotkeys, window handles or device state.

        Toggles
        A toggle holds or spams one key or mouse button until you tap its shortcut again. Give it a name, choose the key to hold or tap, choose Hold or Spam, pick a shortcut, then save. Checked toggles are armed: nothing happens until you press the shortcut inside your Click target. Spam is deliberately not limited by the Actions click cap; it is made for fast tapping.

        Screenshot
        Save a PNG of the full screen or a dragged area. The clicker hides itself while capturing. Choose the folder, optionally copy to the clipboard and group files into daily folders, and set a global hotkey if you want one.

        Settings
        Closing the window can minimise to the notification area instead of exiting; use the tray icon's Exit to quit. Choose light or dark appearance. When a Click target window is resized, saved click positions scale with it by default. The status overlay sits beside the target and shows what is running; set its opacity and size here, or hide it with Show overlay. Global controls are F8 (Actions start/stop), F9 (Stop all) and F10 (record) by default; each needs a different key.

        If something is stuck
        Cleanup pending means the clicker could not release an input because you were physically holding the same key or button. Release it and press Release stuck input. Closing the app normally stops and releases everything; killing the process from Task Manager cannot. Some apps and games ignore synthetic input or ban macros; follow their rules.

        Data
        A build with portable.mode stores everything in the Data folder beside the executable; otherwise it lives in your local application data. No account, network service or telemetry is used. This build is unsigned; Windows may warn before it runs.
        """);

    private void ShowText(string title, string text)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 12) };
        AutomationProperties.SetName(box, title);
        var panel = new DockPanel { Margin = new Thickness(22) };
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window { Title = title, Owner = this, Width = 720, Height = 620, MinWidth = 440, MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        dialog.SetResourceReference(BackgroundProperty, "AppBackground");
        dialog.SetResourceReference(ForegroundProperty, "TextPrimary");
        foreach (var type in new[] { typeof(Button), typeof(TextBox) })
            if (TryFindResource(type) is Style style) dialog.Resources[type] = style;
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close); panel.Children.Add(box);
        dialog.ShowDialog();
    }
}
