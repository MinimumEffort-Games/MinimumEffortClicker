# Changelog

## Unreleased

- Report a bug, next to Help. It saves a zip (your text, app and system details, the end of the log, and
  optionally a picture of the clicker and the Action List; user name and computer name replaced) and opens a new
  GitHub issue with the text filled in. The app sends nothing itself. After a close that was not normal, the next
  start offers a report.
- Look recording for first- and third-person games. While a game holds the cursor, Record keeps the mouse
  movement as Look actions (relative movement, about 60 steps a second), so playback turns the camera the same way.
  Clicks made then keep no screen spot, so they never move the cursor. Selecting a Look shows its length and turn;
  its pause after and name can be changed.
  Linux reads the movement from the mouse itself (input access) and plays it through the desktop helper; on GNOME
  the helper has to be installed again (version 1.1, Settings, Linux setup).
- Each click or key press in the Action List has a "× times" field: it runs that many times in a row (1–9,999)
  each time the list gets to it, so a thousand clicks are one row. Once / Repeating still decides the later loops.
- The Click target chosen last is selected again at the next start when its window is open. Choosing a Click
  target puts the overlay back on the Overlay Anchor Point from Settings.
- Settings, Window, Keep on top keeps the clicker in front of other windows.
- A controller button for Pause / Unpause, View (Back) by default; an older setup that already used View
  keeps it there. "Start / toggle" is now called "Start / Stop".
- The Actions editor's number fields line up with the buttons and the location row above them.
- New default keys side by side: F6 Stop All, F7 Pause / Unpause, F8 Start / Stop, F9 Screenshot, F10 Record. Keys you
  already saved stay as they are; Reset keybinds gives the new defaults. If an older setup already uses F7, Pause takes
  the first free key.
- Toggles and Controller to Key edit like the Actions page: blue Add Toggle / Add Binding, green Save Changes for the
  selected item, grey until complete, Clear editor, the bin below the list, and a click elsewhere ends editing.
- Linux: the clicker runs once, however fast the icon is clicked; a second launch brings the running one to the front.
- The unlocked overlay stays where you dropped it on the screen and no longer follows the Click target; locking it
  pins it to the window where it is. Picking a spot on the Overlay Anchor Point picture locks it there.
- Minimize on Start and Minimize on record minimize with any Click target and any way of starting; a window target
  still comes to the front.
- A Pause key (Settings, Global controls): "Pause F7" / "Unpause F7" on the button. F8 stays Start / Stop and
  also stops a paused run.
- Linux: a desktop helper that answers too slowly (a busy KWin or GNOME Shell) no longer closes the app while the
  overlay is being placed, for example when closing the clicker to the tray; the overlay keeps its place instead.
- Workshop: key presses from a Workshop macro never go to a terminal or command line (cmd, PowerShell, Windows Terminal
  and others). With one as Click target the list does not start.
- Steam Deck: the controller shortcuts default to the back buttons (L4 start/stop, L5 stop all, R4 record, R5
  screenshot), since Steam's desktop layout already uses A, B, X and Y. Reset controller buttons sets them.
- Linux: recording works on every keyboard layout; it failed when stopped on a layout without a Windows code.
- Linux: on systems that already give access to the keyboard and mouse (SteamOS), Linux setup says so and no longer
  offers Turn off, which could have removed that access.

- Steam Deck: limited support, now said in the app, Help and docs. Recording sees only a USB or Bluetooth mouse or
  keyboard; the Deck's own trackpads, buttons and on-screen keyboard can't be recorded. Linux setup no longer counts
  the Deck's silent built-in devices as input access, so Turn on is offered for an external mouse or keyboard.
- Linux: Settings, Linux setup has a switch to show the input access notice at startup again; the notice says it can
  be changed there any time.
- Settings, Emergency stop names the stop that works now (the key, or the controller button after controller input).
## 0.5.0-preview.36 (2026-10-17)

- New look: pictures for keys, controller buttons and action types, a mouse picture to pick mouse buttons on and a
  controller picture to pick controller buttons on, and a new app icon. Most choices are buttons with a green outline
  instead of checkboxes and dropdowns; number fields change with the mouse wheel or their arrows (Shift for steps of
  10). Mouse buttons are called LMB, RMB, MMB, M4 and M5 everywhere.
- Key presses: Single, Double or Hold, with Ctrl, Shift and Alt as keycap toggles; a press of 500 ms or more is a hold,
  so the separate Hold key is gone.
- Toggles can be switched by a controller button as well as a key or mouse button (Keyboard/Mouse and Controller
  halves of the toggle shortcut, either or both). Older toggle files load unchanged.
- The status overlay can sit anywhere: Settings → Overlay → Overlay Anchor Point pins it to any spot along the edge of
  the Click target window (or the screen with Desktop), and the padlock on the overlay unlocks it so you can drag it.
  It keeps its place next to the window as the window moves. Older settings keep it top right, locked.
- The screenshot key is F7 by default and shows on Take Screenshot.
- Linux: "Minimize to tray" (the top bar on GNOME, the system tray on KDE); Settings opens at the top.
- New Controller to Key page: turn any controller button into a key or mouse button, held for as long as the button
  is held, in whichever window is in front (turbo buttons, autorun, a mouse-button spam). Binds can be switched off
  and come back when the clicker starts; Stop All lets go of everything.
- Controllers: Xbox, PlayStation, Switch and Steam Deck controllers (including the Deck's back buttons) work on
  Windows and Linux. A starts and stops Actions, B stops everything, Y records, X takes a screenshot, in any window
  like the keys; change or clear them under Settings → Controller. After you use the controller the overlay and
  buttons show its button names, and the key names again after you type; moving the mouse changes nothing.

## 0.5.0-preview.35 (2026-10-16)

- Linux is no longer a separate preview: it has the same version and status as the Windows build.

- The code now matches the four pages. The hidden macro-library page, the step editor, the setup wizard and the
  overlay drag mode are gone, together with the engine features only they used (typed text, reusable groups,
  if/repeat branches, "run helpers together", logical keys).
- Macro files move to schema 16. Files from schema 2 to 15 still open; retired actions are dropped on load and
  the app says how many. Saving keeps the upgraded file. Nothing is reset.
- A Wait until colour step keeps looking until the colour appears, however long it takes; it used to stop the run
  after 30 seconds without saying why. F9 still stops it. Macro files move to schema 17: older files open with
  their old wait limit dropped. The log now records why every run ended.
- Each action has a Repeating / Once choice: with Loop on, Once actions run only in the first loop.
- The Action List grows with the window. One Test Selected Action button above the list replaces the Test
  button on every row.
- Windows: Ping click location shows a quick red ring three times instead of a 5-second overlay, so the clicker stays
  usable; the tested-click ring sits on the saved spot; the clicker comes back after any run it minimised for; colour
  tests wait until the clicker is out of the picture. Save Macro is a plain button on both platforms.
- A "Minimize on record" choice next to Record/Stop: untick it to record without minimising the clicker.
- Windows: the overlay has the Linux buttons to bring the clicker back and to quit; the rest stays click-through.
- Scroll: recording captures the mouse wheel (a quick run of notches becomes one "Scroll down ×5" action), and a new
  Scroll action turns the wheel a set number of notches at the cursor or a saved spot. Every notch is sent on its own.
- Emergency stop choice in Settings: by default only Stop All (F9) stops the clicker, ending the Action List and every
  toggle, and nothing else you press or move stops a run, so you can click into your game; "Stop on my input" brings
  back the old stop on any input. Older settings files get the new default.
- The overlay keeps its shortcut line on top: "F8 to Start/Stop" on the left and "F9 to Stop All" in red beside a red
  X, with the keys you bound. The run line is short ("Running · 10 Actions · Looping") and a toggle that is on shows
  in blue.
- A "Minimize on Start" checkbox under Loop: untick it to keep the clicker up when Start runs on the Desktop target.
- Linux: while input access is off, a notice at startup explains that Record and Stop on my input need it and that the
  rest of the app works either way, with a **Show at startup** checkbox. **Turn on** and **Turn off** (there and in
  Settings → Linux setup) ask for your password in the system's own window instead of a terminal command.
- The clicker comes back to the front by itself after everything in the Action List that needed the target:
  the list ending or being stopped, Ping location, Ping scan area, Test Selected Action, Set location and recording,
  with a window target as well as Desktop. Toggles never change focus. A list that ends while a toggle keeps running
  leaves the clicker out of the way.
- Wait until a colour: the Pick colour button is gone (Choose area and colour does both). A new **Ping scan area**
  button outlines the watched area three times on the target, and Test Selected Action on a wait outlines it once,
  green when the colour is there and red when it is not. The area picker opens maximised. "Ping click location" is
  now "Ping location", because scroll actions use it too.
- The clicker remembers Loop, Minimize on Start, Record pauses, Minimize on record and Simulate mouse movement as you
  last left them, and the toggles you left Active are armed again at the next start. Armed only: nothing is held or
  pressed until you tap a toggle's shortcut, so the clicker still starts stopped.
- Wait until a colour is set up entirely in the Create an Action panel; the separate wait window is gone. Select
  area… (drag on a snapshot) sits next to Ping scan area, with the RGB colour, the Eyedropper and a colour preview
  below; below Name, an Advanced section holds the exact coordinates, tolerance, scan interval and matching samples.
  The area picker opens maximised on the monitor the clicker is on. Cursor location is no longer cut off.
- Add Action (and Save Changes) stays grey until the action can be added; hovering it says what is missing. Select
  area… is outlined red until an area is chosen, then green.
- A colour wait picked with Desktop as the Click target belongs to the screen, one picked on a window moves with
  that window. After switching between the two, those waits turn red in the Action List with a Reselect Area tag,
  and the editor asks to select the area again before the wait can be saved.
- Linux: the Ping scan area outline stays on the area near GNOME's top bar.
- With "Only Stop All (F9)", your own clicks never end a run: if you hold the button the clicker is about to press, it
  waits until you let go; if you press a button the clicker is holding, your release finishes the click. Before, either
  overlap ended the run ("physical input takes priority" or "cleanup pending"). "Stop on my input" is unchanged.
- Add Action is blue; with a row selected it turns green "Save Changes". Clicking an empty spot in the list or
  elsewhere in the clicker stops editing; picking from a dropdown keeps the row selected; Duplicate selects the copy.
- Looped recordings keep the pause after the last action, so a recorded drag drags again in every loop.
- The Toggles page says toggles only work with a click target selected.
- Stopping a paused run now logs why it stopped instead of "Resumed explicitly".
- Windows: when Windows drops the clicker's input hook, the clicker notices within a second and installs it again.
  A Stop All key outside F1–F11 gets Ctrl+Alt+F9 as a backup that also works while an administrator window is in
  front.
- Closing the window in the first second no longer quits the app when close-to-tray is on.
- Steam version (built with `scripts/Build-Steam.ps1`): Steam Cloud carries saved macros, toggles and settings to
  your other PCs, and the Workshop shares macros. Share on Workshop uploads the saved macro and updates the same item
  next time; Browse Workshop opens the Workshop in Steam; subscribed macros appear in Load Macro's Workshop folder.
  The portable ZIP is unchanged and stays offline. See `docs/STEAM.md`.
- Workshop safety: a Workshop macro that presses a system shortcut (Ctrl+Esc, Ctrl+Shift+Esc, Alt+F4, Ctrl+Alt+T,
  Alt+F2, Alt+Space, Ctrl+Alt+Delete, Ctrl+Alt+F-keys, Alt+Print Screen) is refused, sharing one is refused, and key
  presses from Workshop actions only run with a window as Click target. Your own macros are not limited.
- A Help button in the header explains each page.
- Safety: an unexpected error now stops playback and releases every held key before it is reported, the Stop All
  hooks can no longer be lost while input is being sent, and the app no longer mistakes its own held key for the
  user's finger.
- A spam toggle whose press length equals its interval now keeps full speed.
- Suspended Store apps no longer appear as Click targets, and neither do background programs' invisible helper windows (for example a printer's update service): the list follows the same rules as Alt+Tab. Orphaned toggle shortcuts no longer block their key.
- Browsing the shutdown options no longer switches Loop on by itself.
- Choosing a non-F key for Stop All explains that it will not work while an administrator window is in front.
- Linux: KDE Plasma 6 on Wayland works as well as GNOME. KWin itself is the helper there: `install.sh` sets it up
  with no log-out, and a separate small input process lets go of every held key and button if the clicker
  crashes and sends nothing while the screen is locked.
- Building: the SDK pin accepts newer 10.0.4xx patches, the portable ZIP is written under `artifacts\package`,
  and the VM probe needs `MEC_ISOLATED_VM=1` in a virtual machine instead of a specific user account.

## 0.5.0-preview.34.16 (2026-10-13)

First public preview: MIT-licensed source and a portable, unsigned Windows 11 x64 build.
