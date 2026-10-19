namespace MinimumEffortClicker.Core;

/// <summary>One section of the Help manual. A line starting with "- " is a bullet; an empty line is a gap.</summary>
public sealed record HelpSection(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// The Help manual for both apps (User, 2026-10-19: clear documentation of every function, with a clickable contents
/// list). One text, so Windows and Linux never drift apart; the parts that only apply to Linux, a Steam Deck or the Steam
/// version are added only there.
/// </summary>
public static class HelpManual
{
    public static IReadOnlyList<HelpSection> For(bool linux, bool steam, bool steamDeck)
    {
        string tray = linux ? "the system tray (the top bar on GNOME)" : "the notification area";
        var sections = new List<HelpSection>
        {
            new("Getting started",
            [
                "Minimum Effort Clicker clicks, presses keys and waits for you, in the order you set, once or in a loop.",
                "",
                "- Pick a target: the window the clicks go to, or Desktop for screen positions.",
                "- Build an Action List on the Actions page, or press F10 to record what you do.",
                "- Press Start (F8). F6 stops everything at once.",
                "",
                "Nothing runs until you start it: loading a macro, switching windows or starting the app never starts anything. F6 (Stop All) stops actions, toggles and recording, and lets go of every key and button the clicker holds. Nothing else you press stops a run, so you can click into your game; see Keys and controller to change that.",
            ]),
            new("Pick a target",
            [
                "The Click target decides where clicks land.",
                "",
                "- Desktop: saved spots are screen positions, on any monitor. Toggles need a window and do not work with Desktop.",
                "- A window: saved spots are inside that window and follow it when it moves. Open the list to refresh it.",
                "- The window must be in front while a list runs. If it goes behind another window, the run pauses and goes on by itself once the window has been back in front for half a second. A pause you asked for stays paused.",
                "- The clicker remembers the window you chose and selects it again at the next start when that window is open.",
                "- Choosing a target puts the overlay back on the spot set under Settings, Overlay Anchor Point.",
                "- When the target is resized: Settings, When the click target is resized. Scale stretches saved spots with the window; Keep fixed keeps them the same distance from its top-left corner. A window resized to another shape usually needs its spots set again.",
            ]),
            new("Actions and the Action List",
            [
                "The Action List runs from top to bottom. Create an action on the left, then press Add Action.",
                "",
                "- Click: choose the button on the mouse picture (LMB, MMB, RMB, M4, M5), then Single Click, Double Click or Hold. Click at your cursor, or press Set Location… and click the spot on the target. Ping location shows where a saved spot is.",
                "- Key Press: press the key button and click the key. LCtrl, LShift and LAlt below it are held with it. A press of 500 ms or more holds the key.",
                "- Pause: waits a number of milliseconds or seconds.",
                "- Wait Until: waits until a colour appears or disappears; see Wait Until a colour.",
                "- Screenshot: saves a picture of the whole screen, then pauses at least one second.",
                "- Scroll: turns the mouse wheel up, down, left or right, a number of notches, at your cursor or a saved spot. Every notch is sent on its own, so games count each one.",
                "- Randomize: random click offset (a few pixels around the spot) and random press and pause timing (between the values you set and a maximum).",
                "- Numbers: turn the mouse wheel over a number field, or use its small arrows; hold Shift for ten steps.",
                "",
                "In the list:",
                "- × next to a click or key press: how many times in a row it runs each time the list reaches it (1–9,999), so a thousand clicks are one row.",
                "- Active / Inactive turns an action on or off without removing it.",
                "- Repeating / Once (the two small icons): with Loop on, a Once action runs in the first loop only, for example a click that opens a level. Without Loop every action runs once.",
                "- Select a row to change it: Add Action turns green and says Save Changes. Click anywhere else to stop editing. Clear editor empties the settings.",
                "- The arrow, copy, delete, undo, redo and clear buttons under the list arrange it.",
                "- Test Selected Action runs only the selected row. A click shows a ring where it landed; a colour wait turns the button green when the colour is there now, red when not.",
                "",
                "Running:",
                "- Start (F8) runs the list; with Loop on it repeats until you stop it. F8 again stops it, also while it is paused.",
                "- Pause (F7) pauses a running list; Unpause (F7) goes on where it was.",
                "- Minimize on Start gets the clicker out of the way when the list starts; a window target comes to the front. The clicker comes back when the list ends.",
                "- Simulate mouse movement: the pointer travels to the next click during the pause before it instead of jumping.",
                "- Shutdown PC after… (below the list): after a number of loops, minutes, or at a set time. Press Enable shutdown… to arm it.",
            ]),
            new("Recording and Look",
            [
                "Record (F10) starts recording right away. Click, type and scroll in your target, then press F10 again. What you do appears in the list as you go.",
                "",
                "- Minimize on record gets the clicker out of the way first.",
                "- Record pauses keeps the time between your inputs as Pause actions you can edit, including the time after your last input, so a loop waits before it starts again.",
                "- A quick run of wheel notches becomes one Scroll action; a drag stays a drag.",
                "- Look: in first- and third-person games that hold the cursor, Record also keeps your mouse movement as Look actions (mouse icon), so playback turns the camera the same way. A rest of a quarter second starts a new Look; clicks made while turning stay in it and land where the game has the cursor. Keep the game's mouse sensitivity the same between recording and playback. Some games with anti-cheat ignore movement from other programs.",
                "- Select a Look row to see how long it lasts and how far it turns; its pause after and name can be changed. Record it again to change the movement.",
            ]),
            new("Wait Until a colour",
            [
                "The list waits at this action until a colour shows up, or goes away, however long that takes. F6 stops it.",
                "",
                "- Select area… takes a picture of the target (or the monitors, for Desktop); drag the area to watch. The colour comes from its top-left pixel.",
                "- Eyedropper changes only the colour; the RGB field takes six hexadecimal digits.",
                "- Ping scan area outlines the area on the target. Test Selected Action checks it once: green when the colour is there now, red when not.",
                "- Advanced holds the exact coordinates, tolerance and scan speed.",
                "- An area picked for Desktop and then used with a window (or the other way round) is marked red: select the area again.",
            ]),
            new("Saving and loading macros",
            [
                "- Save Macro writes the Action List to a .mecprofile file in the Macros folder; Load Macro opens one.",
                "- A macro holds the actions, their timing and any captured colours or images. It never holds your hotkeys or window details.",
                "- Macros move freely between the Windows and Linux versions.",
            ]),
            new("Toggles",
            [
                "A toggle holds or spams one key or mouse button until you press its shortcut again, for autorun, turbo buttons or a simple mouse-button spam.",
                "",
                "- Name it, click the key or mouse button to hold or tap, and choose Hold or Spam.",
                "- Pick its shortcut: Keyboard/Mouse, Controller, or both. Right-click either to remove it. Press Add Toggle.",
                "- Set a toggle Active to arm it. Armed toggles come back at the next start and press nothing until you press their shortcut.",
                "- Toggles need a window as Click target, and that window in front. If not, pressing the shortcut shows a red line on the overlay: No Click Target Selected, or Click Target Not In Front.",
                "- Select a toggle to change it (Save Changes); the bin under the list removes it.",
                "- Spam is built for fast tapping; the Actions click limit does not apply to it.",
            ]),
            new("Controller to Key",
            [
                "Turns controller buttons into keys and mouse buttons, in whichever window is in front. No Click target is needed.",
                "",
                "- Name the bind, click the key or mouse button it presses, press Controller binding and then the controller button (or click it on the picture), and press Add Binding.",
                "- While the controller button is held, its key or mouse button is held. Two binds may press the same key.",
                "- Set a bind Inactive to keep it without using it. Active binds come back at the next start.",
                "- Buttons that already do something are outlined red on the picture; hover one to see what. Buttons used under Settings, Controller cannot be bound here; clear them there first.",
                "- The button picker closes by itself after 30 seconds if nothing is chosen.",
            ]),
            new("Screenshot",
            [
                "- Take a picture of the whole screen, or drag an area. The clicker hides while it captures and comes back as it was (minimised stays minimised).",
                "- Choose the folder; optionally copy each picture to the clipboard and sort files into daily folders.",
                "- F9 takes a screenshot from anywhere; change the key under Settings.",
            ]),
            new("Keys and controller",
            [
                "- Keys: F6 Stop All, F7 Pause / Unpause, F8 Start / Stop, F9 Screenshot, F10 Record. Change any of them under Settings, Global controls; Reset keybinds brings the defaults back. Each needs its own key."
                    + (linux ? "" : " If Stop All is not one of F1–F11, Ctrl+Alt+F9 also stops everything, even while an administrator window is in front."),
                steamDeck
                    ? "- Controller on a Steam Deck: L4 Start / Stop, L5 Stop All, R4 Record, R5 Screenshot, View Pause, in any window, like the keys."
                    : "- Controller: A Start / Stop, B Stop All, View (Back) Pause, Y Record, X Screenshot, in any window, like the keys. On a Steam Deck the back buttons are the default instead.",
                "- To change a controller button, click it under Settings, Controller, then press the button or click it on the picture. Right-click to leave a control without one.",
                "- Xbox, PlayStation, Switch and Steam Deck controllers work, including the Deck's back buttons.",
                "- Emergency stop: Stop All (the default) means only Stop All stops the clicker. Stop All Any Input also stops everything when you press a key or button the clicker is using, or move the mouse while it does.",
            ]),
            new("Overlay",
            [
                "A small status box on the target: what runs, and the keys that start and stop it. Toggles show Ready, or On in blue.",
                "",
                "- Show overlay (top right) turns it on or off. Show overlay when idle keeps it up while nothing runs.",
                "- Its corners button brings the clicker back to the front; its X stops everything and quits.",
                "- The padlock unlocks it: drag it anywhere on the screen, and it stays there. Locking it pins it to the target where it is. Unlocked it catches clicks; locked it lets them through.",
                "- Settings, Overlay: opacity, size, and the Overlay Anchor Point (click along an edge of the picture to pin it to that spot).",
                "- A short red line means something did not work, for example a toggle without a Click target.",
            ]),
            new("Settings",
            [
                "- Keep on top: the clicker stays in front of other windows. Start, Test and Record still minimise it when they are set to.",
                $"- {(linux ? "Minimize to tray" : "Minimize to taskbar")}: clicking X keeps the clicker running in {tray}; use the icon's Exit to quit. Turn it off and X closes the clicker. Starting the clicker again brings the running one to the front.",
                "- Appearance: light or dark.",
                "- Global controls, Controller, Emergency stop, Overlay and resize behaviour: see their own sections.",
            ]),
        };
        if (steam) sections.Add(new("Steam Cloud and Workshop",
        [
            "- Steam Cloud: your macros, toggles, settings and hotkeys follow you to your other PCs.",
            "- Share on Workshop (under Save Macro) uploads the macro with a title, description and who can see it. Sharing it again updates the item. Captured image areas are pictures of your screen; everyone who can see the item can see them.",
            "- Browse Workshop opens the Workshop in Steam. Subscribed macros appear in the Workshop folder that Load Macro opens and update when their author updates them.",
            "- A Workshop macro was made by someone else: read every action before you press Start. For your safety a macro that presses a system shortcut is never added, and Workshop key presses only go to a selected window, never to Desktop and never into a terminal.",
            "- The clicker only goes online through Steam: when you share, and when Steam downloads macros you subscribed to.",
        ]));
        if (linux) sections.Add(new("Linux setup",
        [
            "Wayland does not let apps see other windows, read the pointer or send input. On GNOME the small ME Clicker GNOME helper does that for this app only; on KDE Plasma, KWin does it for apps it has been told about.",
            "",
            "- GNOME: Install GNOME helper (Settings, Linux setup), then log out and back in once. Look actions need helper version 1.1: install it again if Look reports an older helper.",
            "- KDE Plasma: install.sh tells KWin about the clicker. A copy started from another folder needs Allow this copy of the app once.",
            "- Input access: recording, Stop on my input, mouse-button shortcuts and holding a hotkey to run need read access to your keyboard and mouse. Turn on asks for your password in the system's own window; Turn off removes it. Without a password window, Copy command gives you the one command to run.",
            "- Without input access the clicker still clicks, presses keys, plays and saves macros, takes screenshots and waits for colours.",
            "- GNOME and KDE Plasma are supported. Clicks always go to the window in front: Linux has no background clicks.",
        ]));
        if (linux) sections.Add(new("Steam Deck",
        [
            "The clicker runs in Desktop Mode (not in Game Mode).",
            "",
            "- The Deck's own trackpads, buttons and on-screen keyboard can't be recorded: Steam types and clicks for them in a way no app can see. A USB or Bluetooth mouse or keyboard can be recorded once input access is on.",
            "- Actions set up by hand (Set Location), toggles, Controller to Key, screenshots and the back-button shortcuts all work.",
            "- Steam's desktop buttons: in Desktop Mode, Steam's desktop controller layout presses keys for the back buttons (L4 presses the Windows key, for example), so a back button would do two things at once. To give them to the clicker only: Steam, Settings, Controller, Desktop Layout, Edit, and set L4, L5, R4 and R5 to nothing. Not needed if you only use the clicker while a game runs: a running game switches the desktop layout off.",
        ]));
        sections.Add(new("Bug report and if something is stuck",
        [
            "- Bug report (next to Help): describe what happened and choose what to include: app and system details, the end of the log, a picture of the clicker, the Action List. Your user name and computer name are replaced. Create report saves a zip and opens a new GitHub issue with your text; drag the zip into it and submit. The clicker sends nothing itself. After a close that was not normal, the next start offers a report.",
            "- Cleanup pending: the clicker could not let go of a key or button because you held the same one. Let go of it and press Release stuck input.",
            linux
                ? "- Closing the app normally stops and releases everything. If it crashes or is killed, the desktop helper lets go of anything it was holding."
                : "- Closing the app normally stops and releases everything. Ending it from Task Manager does not.",
            "- Some apps and games ignore simulated input or forbid macros. Check their rules first.",
        ]));
        sections.Add(new("Your data",
        [
            linux
                ? "- Settings, macros and log.txt are in the Data folder beside the app."
                : "- With a portable.mode file next to the app, settings, macros and log.txt are in the Data folder beside it; otherwise in your local app data folder.",
            "- No account and no tracking. " + (steam ? "The clicker only goes online through Steam, when you share or subscribe." : "The clicker never goes online.")
                + (linux || steam ? "" : " The GitHub build is not signed, so Windows may warn the first time it runs."),
            "- log.txt records what went wrong and stays on your PC; Bug report can include its end.",
        ]));
        return sections;
    }
}
