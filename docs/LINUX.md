# Minimum Effort Clicker for Linux

**Preview.** The Linux build has the same Actions, Toggles, Screenshot and Settings pages as the
Windows build and reads the same macro files. It is tested on Ubuntu 26.04 with GNOME 50 on
Wayland. Other desktops are not supported yet; see "Limits" below.

## Install

1. Extract `MinimumEffortClicker-<version>-linux-x64.tar.gz` into a writable folder.
2. In that folder run `./install.sh`. It installs the app for your user (no sudo), adds
   **Minimum Effort Clicker** to your applications, and installs the **ME Clicker GNOME helper**.
3. **Log out and back in once.** GNOME on Wayland only loads a newly installed extension at login.
4. Optional, but needed for recording, mouse-button shortcuts, hotkeys that work in every app and
   the safety stop when you touch your mouse or keyboard:

   ```bash
   sudo bash ~/.local/share/minimum-effort-clicker/app/setup-input-access.sh
   ```

   This adds one udev rule letting the person signed in at this computer read keyboards and mice.
   Remove it again with `--remove`. In the app, open **Settings → Linux setup** and choose
   **Check again**.

You can also run `./MinimumEffortClicker` straight from the extracted folder and use
**Settings → Linux setup → Install GNOME helper**. Settings and macros live in `Data` beside the app.
`./uninstall.sh` removes the install and keeps `Data` unless you pass `--delete-data`.

F8 starts/stops Actions, F9 stops everything and F10 starts/stops recording, as on Windows.

## Why a GNOME helper?

Wayland deliberately stops apps from seeing other windows, reading the pointer, capturing the screen
or sending input. The helper is a small GNOME Shell extension (`linux/gnome-extension`) that does
exactly those things for this app and nothing else:

- It answers only programs listed in `~/.config/me-clicker-helper/allowed-clients` (the installer
  adds this app's executable). Any other program gets "access denied".
- Clicks and keys go through GNOME's own virtual input devices, at exact desktop positions.
- If the app exits or crashes while holding a key or button, the helper releases it.
- Colour waits capture the target window's own pixels, so the status overlay or another window on
  top never changes what they see (like Windows Graphics Capture).
- GNOME disables extensions on the lock screen, so nothing can be sent while the screen is locked.

## Physical input access

Without the optional udev rule, the app still clicks, presses keys, plays and saves macros, takes
screenshots and runs colour waits. Keyboard hotkeys then use GNOME shortcuts, so mouse-button
shortcuts, holding a hotkey to run, recording, and "physical input stops playback" are off.
Settings → Linux setup shows which mode is active.

With the rule, the app reads `/dev/input` like the Windows build's input hooks: it never grabs or
blocks a device, and only listens while it runs. Playback goes through GNOME's virtual devices,
which never appear in `/dev/input`, so the app always knows your real input from its own.

In Desktop mode, Pick colour and the area picker show a Monitor list; colour and image waits then read
that area of the screen through the GNOME helper. With a window as Click target, a run pauses when the
window goes behind another one and continues by itself once it has been back in front for half a second.

## Limits

- **GNOME only.** KDE, Sway and other desktops need a different helper; they are not supported yet.
- **No cursor-free clicks.** Windows can post clicks to a background window; Linux apps only accept
  input through the compositor, so the target must be in front.
- **Window coordinates on Wayland** count from the window's visible frame. Apps that draw their own
  title bar (most GNOME apps) include it in that area.
- Fractional display scaling has not been tested yet.
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
