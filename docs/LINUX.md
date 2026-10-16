# Minimum Effort Clicker for Linux

**Preview.** The Linux build has the same Actions, Toggles, Screenshot and Settings pages as the
Windows build and reads the same macro files. It is tested on Ubuntu 26.04 with GNOME 50 on Wayland. KDE Plasma 6.7
on Wayland (CachyOS) is checked in a private KWin test session; a test on a real Plasma desktop is still to come. Other desktops are not supported yet; see "Limits" below.

## Install

1. Extract `MinimumEffortClicker-<version>-linux-x64.tar.gz` into a writable folder.
2. In that folder run `./install.sh`. It installs the app for your user (no sudo), adds
   **Minimum Effort Clicker** to your applications, and sets up the helper for the desktop you are
   running: the **ME Clicker GNOME helper** on GNOME, or on KDE Plasma two lines in the launcher that
   let KWin give the app screen capture and input.
3. **GNOME: log out and back in once.** GNOME on Wayland only loads a newly installed extension at login.
   KDE Plasma needs no log-out.
4. Optional, but needed for recording, Stop on my input, mouse-button shortcuts and hotkeys that work in
   every app. When the app starts without it, a notice explains this and offers **Turn on**, which asks for
   your password in the system's own window (pkexec) and sets it up; **Settings → Linux setup** has **Turn on**
   and **Turn off** too. Or run it yourself in a terminal:

   ```bash
   sudo bash ~/.local/share/minimum-effort-clicker/app/setup-input-access.sh
   ```

   This adds one udev rule letting the person signed in at this computer read keyboards and mice.
   Remove it again with `--remove`. In the app, open **Settings → Linux setup** and choose
   **Check again**.

You can also run `./MinimumEffortClicker` straight from the extracted folder and use
**Settings → Linux setup → Install GNOME helper** (GNOME) or **Allow this copy of the app** (KDE). Settings and macros live in `Data` beside the app.
`./uninstall.sh` removes the install and keeps `Data` unless you pass `--delete-data`.

F8 starts/stops Actions, F9 stops everything and F10 starts/stops recording, as on Windows.

## Why a helper?

Wayland deliberately stops apps from seeing other windows, reading the pointer, capturing the screen
or sending input. On GNOME the helper is a small GNOME Shell extension (`linux/gnome-extension`) that does
exactly those things for this app and nothing else:

- It answers only programs listed in `~/.config/me-clicker-helper/allowed-clients` (the installer
  adds this app's executable). Any other program gets "access denied".
- Clicks and keys go through GNOME's own virtual input devices, at exact desktop positions.
- If the app exits or crashes while holding a key or button, the helper releases it.
- Colour waits capture the target window's own pixels, so the status overlay or another window on
  top never changes what they see (like Windows Graphics Capture).
- GNOME disables extensions on the lock screen, so nothing can be sent while the screen is locked.

On KDE Plasma nothing is installed into the desktop; KWin does it, through the ways KDE offers apps:

- A small KWin script, loaded by the app while it runs, reports windows, focus, the pointer and monitors,
  and brings a window to the front. It only answers the copy of the app that loaded it.
- Colour waits and screenshots go through KWin's ScreenShot2, and clicks, keys and scrolling through KWin's
  fake input. KWin allows both only to programs whose launcher (`.desktop` file) names them; `install.sh`
  or **Allow this copy of the app** writes those lines, and KWin picks them up within seconds.
- Input is sent by a second, small copy of the app (the input guard). If the app exits or crashes, the guard
  releases every key and button it holds. KWin would release keys but not mouse buttons on its own.
- KWin does not stop input on the lock screen, so the guard refuses to send while the screen is locked.
- Hotkeys are KDE global shortcuts. While the clicker runs they show in System Settings → Shortcuts; a key
  KDE or another app already uses is refused, so choose another one.

## Physical input access

Without the optional udev rule, the app still clicks, presses keys, plays and saves macros, takes
screenshots and runs colour waits. Keyboard hotkeys then use GNOME shortcuts, so mouse-button
shortcuts, holding a hotkey to run, recording, and "physical input stops playback" are off.
Settings → Linux setup shows which mode is active.

With the rule, the app reads `/dev/input` like the Windows build's input hooks: it never grabs or
blocks a device, and only listens while it runs. Playback goes through GNOME's or KWin's virtual devices,
which never appear in `/dev/input`, so the app always knows your real input from its own.

In Desktop mode, the area picker (Choose area and colour) shows a Monitor list; colour and image waits then read
that area of the screen through the helper. With a window as Click target, a run pauses when the
window goes behind another one and continues by itself once it has been back in front for half a second.

## Limits

- **GNOME and KDE Plasma only.** Sway, Hyprland and other desktops need a different helper; they are not
  supported yet.
- **KDE scrolling** is one notch per step in X11 apps and games (most games). Wayland apps get a short smooth
  scroll per step instead of a notch, and this app's own window (Avalonia under XWayland) did not scroll from
  it in tests.
- **No cursor-free clicks.** Windows can post clicks to a background window; Linux apps only accept
  input through the compositor, so the target must be in front.
- **Window coordinates on Wayland** count from the window's visible frame. Apps that draw their own
  title bar (most GNOME apps) include it in that area.
- Fractional display scaling: KWin's answers were checked at 1.5× in a private test session (click position and
  screen capture), not yet with the app on a real screen, and not under GNOME.
- Keyboard layouts: common xkb layouts map to the same identity as on Windows, so recordings move
  between systems. "Logical" keys follow US key positions on Linux.
- No shutdown happens without the visible 30-second countdown. Linux uses logind's power-off.

## Build from source

Requires the pinned .NET SDK (see `global.json`). On Ubuntu without sudo:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version 10.0.401
```

Then:

```bash
scripts/linux/build.sh
```

```bash
scripts/linux/test.sh
```

```bash
scripts/linux/package.sh ~/Desktop/Clicker/MinimumEffortClicker-linux-x64
```

`scripts/linux/run-headless-probe.sh` is the opt-in native probe: it starts a private, headless
GNOME Shell with its own D-Bus session and Wayland display, loads the helper there, and checks
clicks, keys, Stop All, held-input release and capture against a harmless GTK test window. It never
sends input to your real desktop. It needs `gnome-shell`, `python3-gi` and GTK 4.

`scripts/linux/run-headless-kwin-probe.sh` runs the same probe in a private, headless KWin (Plasma 6),
plus two KDE checks: a killed clicker's held button and Shift are released, and nothing is sent while
the screen is locked. KWin's own locker locks a private session at once, so the probe uses a stand-in
for `org.freedesktop.ScreenSaver`. It needs `kwin_wayland`, python-gobject and GTK 4.
