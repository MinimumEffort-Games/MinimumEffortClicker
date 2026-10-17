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
            "Nothing else you press stops a run, so you can click into your game. Settings > Emergency stop > Stop All Any Input makes any of your input stop it instead.",
        ]),
        ("Actions",
        [
            "The Actions page runs a list of clicks, key presses and pauses in order.",
            "",
            "- Click target: choose where the clicks go. Desktop uses screen positions on any monitor. A window uses positions inside that window and follows it when it moves.",
            "- The window must be in front while the list runs. If it goes behind another window, the run pauses and continues by itself once the window has been back in front for half a second.",
            "- Add Action: pick one of the six pictures (click, key press, pause, wait until, screenshot, scroll; hover one for its name), fill in its settings and press Add Action. A green outline shows what is chosen; a red one shows what still needs a choice.",
            "- Click: click the button on the mouse picture (LMB, MMB, RMB, M4, M5), then Single Click, Double Click or Hold.",
            "- Key Press: press the key button and click the key on the keyboard. LCtrl, LShift and LAlt below it are held with the key. A press duration of 500 ms or more holds the key.",
            "- Wait until a colour: the run waits there until the colour shows up (or goes), however long that takes. F9 stops it. Select area… (drag on a snapshot) picks where it watches and its colour (with Desktop as the target, on any monitor); the Eyedropper changes only the colour, and Advanced holds the exact coordinates, tolerance and scan speed. Ping scan area outlines that area on the target, and Test Selected Action outlines it green when the colour is there now, red when not.",
            "- Scroll: turns the mouse wheel in the direction of the arrow key you pick, a number of notches, at your cursor or a saved location. Every notch is sent on its own, so games count each one.",
            "- Numbers: turn the mouse wheel over a number field, or use its small arrows; hold Shift for ten steps at a time. Typing works too.",
            "- Start (F8): runs the list once. Turn on Loop to repeat it until you stop it. With Desktop as the Click target, Start minimises the clicker; turn off Minimize on Start to keep it up.",
            "- Test Selected Action (above the list, on the right): the clicker minimises and runs only the selected action. A click shows a ring where it landed. A colour wait is checked once: the button turns green when the colour is there right now and red when it is not. It also turns red when an action could not run.",
            "- Each row shows a mouse or keyboard for clicks and key presses. Active / Inactive turns the action on or off. Below it, the repeat icon makes it run in every loop and the 1 icon only in the first loop, for example a click that opens a level before the loop starts. Without Loop, every action runs once.",
            "",
            "Select a row to edit it: the blue Add Action button turns green and says Save Changes. Click an empty spot in the list, or anywhere else in the clicker, to stop editing. The editor keeps its settings, so Add Action then adds a new action.",
            "The arrow, copy, delete and undo buttons arrange the list. Copy selects the new copy.",
            "",
            "Record (F10) starts recording right away. Click and type in your target, then press F10 again. Your clicks, keys and scrolls appear in the list as you go; a quick run of wheel notches becomes one Scroll action.",
            "The Record button minimises the clicker first. F10 leaves it where it is, unless the Click target is Desktop. Turn off Minimize on record to keep the clicker where it is either way.",
            "With Record pauses on, the time between them is kept as pauses you can edit. When you start with F10, the time before your first click or key is kept too. The pause after your last action is kept as well, so a loop waits before it starts again.",
            "With Simulate mouse movement on, the pointer travels to the next click during the pause before it instead of jumping. With Loop on, the pause at the end of the list is used for the trip back to the first click.",
            "Shutdown PC after… (below the list): choose After X loops, After X minutes of running or At set time, fill in the number or time, and press Enable shutdown… to arm it.",
            "",
            "Save Macro writes the list to a .mecprofile file in the Macros folder. Load Macro opens one. Macros move freely between the Linux and Windows builds.",
            "A macro holds actions, timing and any captured colours or images. It never holds hotkeys or window details.",
        ]),
        ("Toggles",
        [
            "A toggle holds or spams one key or mouse button until you press its shortcut again.",
            "",
            "- Give it a name.",
            "- Click the key or mouse button to hold or tap, on the keyboard or the mouse beside it.",
            "- Choose Hold or Spam.",
            "- Pick its shortcut: Keyboard/Mouse for a key or mouse button, Controller for a controller button, or both. Right-click either one to remove it. Then save.",
            "",
            "Toggles only work when a click target is selected. Set a toggle Active to arm it; the toggles you leave active are armed again the next time the app starts. Nothing happens until you press its shortcut while your Click target is in front.",
            "Spam is built for fast tapping. The click limit on the Actions page does not apply to it.",
        ]),
        ("Controller to Key",
        [
            "Turns controller buttons into keys and mouse buttons, in whichever window is in front; no Click target is needed.",
            "- Name the bind, click the key or mouse button it presses, press Controller binding and then the controller button (or click it on the picture), and save.",
            "- While the controller button is held, its key or mouse button is held. Two binds may press the same key; it stays down until both buttons are up.",
            "- Set a bind Inactive to keep it without using it. Active binds come back when the clicker starts, and press nothing until you press their button.",
            "- On the controller picture, buttons that already do something are outlined red; hover one to see what. Buttons used under Settings → Controller (A, B, Y, X by default) cannot be bound here; clear them there first. Stop All lets go of everything.",
        ]),
        ("Screenshot",
        [
            "Saves a PNG of the whole screen or of an area you drag. The clicker hides itself while it captures.",
            "",
            "Choose the folder. You can also copy each image to the clipboard and group files into daily folders. F7 takes a screenshot from anywhere; change the key under Settings.",
        ]),
        ("Settings",
        [
            "- Minimize to the top bar: clicking X keeps the clicker running in the top bar; use the icon's Exit to quit. Turn it off and X closes the clicker.",
            "- Appearance: light or dark; the one showing has the green outline.",
            "- When the click target is resized: Scale makes saved clicks and scroll spots stretch with the window; Keep fixed keeps them the same distance from its top-left corner.",
            "- Emergency stop: Stop All F9 (the default) means only F9 stops the clicker. Stop All Any Input also stops everything when you press a key or button the clicker is using, or move the mouse while it does. F9 always stops everything.",
            "- Overlay: a small status box on the target shows what is running and which keys stop it. Toggles show Ready, or On in blue. Show overlay (top right) turns it on or off; Show overlay when idle keeps it up while nothing runs and the clicker is minimised. Set its opacity and size here.",
            "- The overlay's corners button brings the clicker window back to the front (GNOME shows no taskbar icon while it is hidden or minimised). Its X stops anything running and quits the clicker.",
            "- Overlay Anchor Point: click along an edge of the picture to pin the overlay to that spot of the Click target window (or the screen, for Desktop). Unlock it with the padlock on the overlay to drag it anywhere instead; it then keeps that place next to the window as the window moves. While unlocked it catches clicks; lock it again to let clicks through.",
            "- Keys: F8 starts and stops Actions, F9 stops everything, F10 records, F7 takes a screenshot. Each needs its own key.",
            "- Controller: A starts and stops Actions, B stops everything, Y records and X takes a screenshot, in any window, like the keys. To change one, click its button under Controller, then press the controller button or click it on the picture; right-click to leave a control without one. Xbox, PlayStation, Switch and Steam Deck controllers work, including the Deck's back buttons. The overlay and buttons show the controller's names after you use it and the keys again after you type.",
        ]),
        ("Linux setup",
        [
            "Wayland does not let apps see other windows, read the pointer or send input. On GNOME the small ME Clicker GNOME helper does that for this app and nothing else; on KDE Plasma, KWin does it for apps it has been told about.",
            "",
            "- GNOME: Install GNOME helper (Settings, Linux setup), then log out and back in once. GNOME only loads a new helper at login.",
            "- KDE Plasma: install.sh tells KWin about the clicker; there is nothing to log out for. A copy started from another folder needs Allow this copy of the app (Settings, Linux setup) once. The clicker's hotkeys show in System Settings, Shortcuts while it runs.",
            "- Without input access the app still clicks, presses keys, plays and saves macros, takes screenshots and waits for colours. Keyboard hotkeys then go through the desktop's shortcuts.",
            "- Recording, Stop on my input, mouse-button shortcuts and holding a hotkey to run need input access. Turn on (Settings, Linux setup, or the notice at startup) asks for your password in the system's own window and sets it up; Turn off removes it again. Without a password window, Copy command gives you the one command to run with sudo.",
            "",
            "GNOME and KDE Plasma are supported; other desktops are not yet. Clicks always go to the window in front: Linux has no background clicks.",
        ]),
        ("If something is stuck",
        [
            "Cleanup pending means the clicker could not let go of a key or button because you were holding the same one yourself. Let go of it and press Release stuck input.",
            "",
            "Closing the app normally stops and releases everything. If the app crashes or is killed, the GNOME helper (or, on KDE, the clicker's small input helper) lets go of anything it was holding.",
            "",
            "Some apps and games ignore simulated input or forbid macros. Check their rules first.",
        ]),
        ("Your data",
        [
            "Settings and macros are stored in the Data folder beside the app.",
            "",
            "No account, no internet, no tracking.",
            "",
            "When something goes wrong, the clicker writes it to log.txt in the same folder. It stays on your PC. Attach it to a bug report if you send one.",
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
