# Changelog

## Unreleased

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
- Windows: the clicker comes back to the front by itself after everything in the Action List that needed the target:
  the list ending or being stopped, Ping click location, Test Selected Action, Set location and recording, with a
  window target as well as Desktop. Toggles never change focus. A list that ends while a toggle keeps running leaves
  the clicker out of the way.
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
