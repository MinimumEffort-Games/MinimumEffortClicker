using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;
using System.Diagnostics;
using System.IO;
using System.Text;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    /// <summary>Help text, one section per page. A line starting with "- " renders as a bullet; an empty line is a gap.</summary>
    private static readonly (string Title, string[] Lines)[] HelpSections =
    [
        ("Nothing runs until you start it",
        [
            "The clicker does nothing on its own. It only runs when you press Start, press a toggle's shortcut, or press the Actions start key.",
            "Loading a macro or switching windows never starts anything.",
            "",
            "F9 stops everything at once: actions, toggles and recording. It also lets go of any key or button the clicker is holding.",
        ]),
        ("Actions",
        [
            "The Actions page runs a list of clicks, key presses and pauses in order.",
            "",
            "- Click target: choose where the clicks go. Desktop uses screen positions on any monitor. A window uses positions inside that window and follows it when it moves.",
            "- The window must be in front while the list runs. If it goes behind another window, the run pauses and continues by itself once the window has been back in front for half a second.",
            "- Wait until a colour or image: the run waits there until the colour shows up, however long that takes. F9 stops it. With Desktop as the target, Pick colour shows a Monitor list, so the wait can watch any monitor.",
            "- Add Action: pick a click, key press, pause, wait for a colour, or screenshot. Fill in its settings and press Add Action.",
            "- Start (F8): runs the list once. Tick Loop to repeat it until you stop it.",
            "- Test (on each row): the clicker minimises and runs only that action. The button turns green when it worked and red when it did not. A click shows a ring where it landed. A colour or image wait is checked once: green means it is there right now.",
            "",
            "Select a row to edit it. The arrow, copy, delete and undo buttons arrange the list.",
            "",
            "Record (F10) starts recording right away. Click and type in your target, then press F10 again. Your clicks and keys appear in the list as you go.",
            "The Record button minimises the clicker first. F10 leaves it where it is, unless the Click target is Desktop.",
            "With Record pauses on, the time between them is kept as pauses you can edit. When you start with F10, the time before your first click or key is kept too.",
            "With Simulate mouse movement on, the pointer travels to the next click during the pause before it instead of jumping. With Loop on, the pause at the end of the list is used for the trip back to the first click.",
            "",
            "Save Macro writes the list to a .mecprofile file in the Macros folder. Load Macro opens one. Macros move freely between the Linux and Windows builds.",
            "A macro holds actions, timing and any captured colours or images. It never holds hotkeys or window details.",
        ]),
        ("Toggles",
        [
            "A toggle holds or spams one key or mouse button until you press its shortcut again.",
            "",
            "- Give it a name.",
            "- Choose the key or button.",
            "- Choose Hold or Spam.",
            "- Pick a shortcut and save it.",
            "",
            "Tick Active to arm a toggle. Nothing happens until you press its shortcut while your Click target is in front.",
            "Spam is built for fast tapping. The click limit on the Actions page does not apply to it.",
        ]),
        ("Screenshot",
        [
            "Saves a PNG of the whole screen or of an area you drag. The clicker hides itself while it captures.",
            "",
            "Choose the folder. You can also copy the image to the clipboard, group files into daily folders, and set a hotkey.",
        ]),
        ("Settings",
        [
            "- Minimize to the top bar: the window hides in the top bar instead of closing. Use the icon's Exit to quit.",
            "- Appearance: light or dark.",
            "- Scale click positions: when the target window is resized, saved positions scale with it.",
            "- Overlay: a small status box beside the target shows what is running. Set its opacity and size here, or hide it.",
            "- Keys: F8 starts and stops Actions, F9 stops everything, F10 records. Each needs its own key.",
        ]),
        ("Linux setup",
        [
            "GNOME on Wayland does not let apps see other windows, read the pointer or send input. The small ME Clicker GNOME helper does that for this app and nothing else.",
            "",
            "- Install GNOME helper (Settings, Linux setup), then log out and back in once. GNOME only loads a new helper at login.",
            "- Without input access the app still clicks, presses keys, plays and saves macros, takes screenshots and waits for colours. Keyboard hotkeys then go through GNOME shortcuts.",
            "- Recording, mouse-button shortcuts, holding a hotkey to run, and stopping when you touch your mouse or keyboard need input access. Copy command gives you the one command to run with sudo; press Check again afterwards.",
            "",
            "Only GNOME is supported for now. Clicks always go to the window in front: Linux has no background clicks.",
        ]),
        ("If something is stuck",
        [
            "Cleanup pending means the clicker could not let go of a key or button because you were holding the same one yourself. Let go of it and press Release stuck input.",
            "",
            "Closing the app normally stops and releases everything. If the app crashes or is killed, the GNOME helper lets go of anything it was holding.",
            "",
            "Some apps and games ignore simulated input or forbid macros. Check their rules first.",
        ]),
        ("Your data",
        [
            "Settings and macros are stored in the Data folder beside the app.",
            "",
            "No account, no internet, no tracking.",
        ]),
    ];

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        const string title = "Minimum Effort Clicker · help";
        var page = new StackPanel { Margin = new Thickness(4, 0, 18, 12) };
        foreach (var (heading, lines) in HelpSections)
        {
            page.Children.Add(new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 18, 0, 8) });
            foreach (string line in lines)
            {
                if (line.Length == 0) { page.Children.Add(new Border { Height = 8 }); continue; }
                bool bullet = line.StartsWith("- ", StringComparison.Ordinal);
                page.Children.Add(new TextBlock { Text = bullet ? "•  " + line[2..] : line, FontSize = 14, TextWrapping = TextWrapping.Wrap,
                    LineHeight = 22, Margin = bullet ? new Thickness(12, 0, 0, 4) : new Thickness(0, 0, 0, 4) });
            }
        }
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 12) };
        Avalonia.Automation.AutomationProperties.SetName(scroll, title);
        var panel = new DockPanel { Margin = new Thickness(22) };
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new DialogWindow { Title = title, OwnerWindow = this, Width = 760, Height = 640, MinWidth = 440, MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        dialog.SetResourceReference(BackgroundProperty, "AppBackground");
        dialog.SetResourceReference(ForegroundProperty, "TextPrimary");
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close); panel.Children.Add(scroll);
        dialog.ShowDialog();
    }
}
