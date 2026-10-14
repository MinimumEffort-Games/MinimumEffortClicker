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
- A Help button in the header explains each page.
- Safety: an unexpected error now stops playback and releases every held key before it is reported, the Stop All
  hooks can no longer be lost while input is being sent, and the app no longer mistakes its own held key for the
  user's finger.
- A spam toggle whose press length equals its interval now keeps full speed.
- Suspended Store apps no longer appear as Click targets. Orphaned toggle shortcuts no longer block their key.
- Browsing the shutdown options no longer switches Loop on by itself.
- Choosing a non-F key for Stop All explains that it will not work while an administrator window is in front.
- Linux: KDE Plasma 6 on Wayland works as well as GNOME. KWin itself is the helper there: `install.sh` sets it up
  with no log-out, and a separate small input process lets go of every held key and button if the clicker
  crashes and sends nothing while the screen is locked.
- Building: the SDK pin accepts newer 10.0.4xx patches, the portable ZIP is written under `artifacts\package`,
  and the VM probe needs `MEC_ISOLATED_VM=1` in a virtual machine instead of a specific user account.

## 0.5.0-preview.34.16 (2026-10-13)

First public preview: MIT-licensed source and a portable, unsigned Windows 11 x64 build.
