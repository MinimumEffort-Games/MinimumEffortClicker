# KDE Plasma support

Branch `kde-support`. Goal: the Linux build works on KDE Plasma 6 (Wayland) as well as GNOME, with every page, macro
and the engine unchanged.

## Phase 1 (2026-10-14): what KDE offers for each helper call

Measured in Roy's CachyOS VM (KWin 6.7.5, Plasma 6.7.5, libei 1.6.0). Every input test ran in a **private headless
KWin** (`kwin_wayland --virtual` inside its own `dbus-run-session`, own runtime and config folders) against a scratch
copy of `tools/LinuxProbe/probe-target.py`. No input was sent to Roy's desktop. Two read-only calls went to the real
session: a KWin script that only counted windows and screens, and a ScreenShot2 call that was refused. The throwaway
tests lived in the session scratchpad; none is in the repository.

Linux build (0 warnings) and tests (13 Linux + 261 shared) pass here with .NET 10.0.401 from `~/.dotnet`.

### Results

| GNOME helper call | KDE route | Result | Measured |
| --- | --- | --- | --- |
| ListWindows, GetWindow, GetFocusWindow, QueryTarget | KWin script (`workspace.windowList()`, `activeWindow`, `clientGeometry`, `pid`, `minimized`) | Works | Titles, PIDs, client and frame geometry, output, minimised state. Wayland and Xwayland windows both listed. |
| WindowAt | KWin script `workspace.windowAt(pos, n)` | Works | Returns the topmost window first. |
| ActivateWindow | KWin script `workspace.activeWindow = w` (after `minimized = false`) | Works | Activates and restores. KWin is the window manager, so there is no "is ready" refusal like GNOME's. |
| FocusChanged, WindowClosed signals | KWin script `workspace.windowActivated` / `windowRemoved` → `callDBus` | Works | Arrive in order. |
| GetPointer | KWin script `workspace.cursorPos` | Works | Logical desktop pixels. |
| GetMonitors | KWin script `workspace.screens` (geometry, `devicePixelRatio`, name) | Works with a catch | Two outputs and 1.5× scale reported correctly. **Guessed:** that the first screen in the list is the primary one. |
| GetShellState | `org.freedesktop.ScreenSaver.GetActive` + `ActiveChanged` (served by KWin) | Works with a catch | Lock state is reported, but see "Lock screen" below. KDE has nothing like GNOME's overview/modal check. |
| GetKeyboardLayout | `org.kde.keyboard /Layouts`: `getLayoutsList`, `getLayout`, `layoutChanged` | Works with a catch | Gives the layout (`de`, `us`) and the current index, but not the variant: Dvorak only shows in the long name. The variant has to be read from `kxkbrc` (`VariantList`) at the same index. |
| PointerMove, PointerButton, Scroll, Key, Keyval, ReleaseAll | KWin fake-input Wayland protocol (`org_kde_kwin_fake_input` v6), authorised by `X-KDE-Wayland-Interfaces` in the app's `.desktop` file | Works with a catch | Absolute moves land on the exact logical pixel (also at 1.5×, also on an Xwayland window). Left/right click, keys (W, F2) and scroll arrive. KWin does not offer the protocol to a program without that `.desktop` line. A `.desktop` file written while the session runs is picked up after about 2 s, with no re-login. **Catch:** on a crash KWin releases held keys but **not held mouse buttons** (still down after 5 s). A later fake-input connection's release does free the button. |
| CaptureRaw, CapturePng | `org.kde.KWin.ScreenShot2.CaptureArea`, authorised by `X-KDE-DBUS-Restricted-Interfaces` in the `.desktop` file | Works | Refused without the line, works with it. 8×8 area about 2.7 ms. With `native-resolution` it returns physical pixels plus the scale (20×20 logical at 1.5× → 30×30). |
| CaptureWindowRaw | `ScreenShot2.CaptureWindow` with KWin's window id, `include-decoration=false` | Works | Returns the client area only, and the window's own pixels: with a red window on top, an area capture saw red and the window capture still saw the target's blue. About 11 ms for 640×420; the app crops the region. |
| GrabAccelerator, UngrabAccelerator, Accelerator signals | KGlobalAccel (`org.kde.kglobalaccel`, served by KWin): `doRegister`, `setShortcutKeys`, component `globalShortcutPressed` / `globalShortcutReleased`, `setInactive` | Works with a catch | F8 press arrived at once and release when the key was let go (held 300 ms → 301 ms), so "hold hotkey to run" works. The key does not also reach the focused app, as on GNOME. **Catch:** KGlobalAccel accepts a key that is already taken and returns it as if granted. Ctrl+F2 (KWin's "Switch to Desktop 2") never fired. The KDE side must ask `getGlobalShortcutsByKey` first and report "already used". Registered shortcuts appear in System Settings → Shortcuts. |

Speed of the KWin script channel: loading a fresh script per question takes about 4 ms. One persistent script that
long-polls the app with `callDBus(..., callback)` answers in about 1 ms (median of 50, max 2 ms). The persistent
script is the route to use.

Real session (read-only): KWin scripting answered in 6.7 ms. ScreenShot2 refused an unauthorised caller with
`NoAuthorized`, as in the private session.

### Safety

- **Held input after a crash.** The GNOME helper releases everything when the app disconnects. KWin's fake input
  releases keys but not mouse buttons. Plan: a small **input guard**: the app starts its own executable again in a
  helper mode, and that process is the only fake-input sender. The app talks to it over a pipe. When the pipe closes
  (app crashed), the guard releases every key and button it holds, then exits. If the guard itself dies, KWin
  releases its keys, and the still-running app can release buttons through a new connection (measured). Only a crash
  of both at once could leave a button down. Cost: one extra process (a .NET start, guessed 40 MB).
- **Lock screen.** KWin does **not** block fake input while locked: after `Lock` the pointer still moved, and keys
  would reach the lock screen. The target window received nothing. The guard therefore checks
  `org.freedesktop.ScreenSaver` (`GetActive` and the `ActiveChanged` signal) and refuses to send while locked, and
  the run pauses the way it does when the GNOME helper reports the shell not ready.

### Ruled out

- **KWin's EIS endpoint** (`org.kde.KWin.EIS.RemoteDesktop.connectToEIS`) worked from any program: no `.desktop`
  line, no prompt. Input arrived, but it is KWin's private backend for the RemoteDesktop portal, not an app API, and
  it also left held input stuck after a crash. Not used.
- **RemoteDesktop and GlobalShortcuts portals.** Not measured: `xdg-desktop-portal-kde` would not start inside the
  private session. Both ask the player in a dialog before the first use; fake input and KGlobalAccel do not, and are
  what KDE's own tools use. They remain the option for other compositors (Sway, Hyprland) later.
- **uinput / ydotool.** Need device access and elevation; excluded by AGENTS.md.

### Guessed, not measured

- The first entry of `workspace.screens` is the primary monitor.
- The guard process costs about 40 MB and adds well under a millisecond per input.
- C# can speak the fake-input protocol through `libwayland-client` (`wl_proxy_marshal_array_flags` with a hand-built
  interface description). This is the riskiest part of Phase 2; the tests above used a small C client.
- A real game under Xwayland at fractional scaling (only the GTK test window was checked).

### Headless KWin for probes

Works. `kwin_wayland --virtual --no-lockscreen --socket <short name> [--xwayland]` inside `dbus-run-session`, with
`XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `XDG_CACHE_HOME`, `XDG_STATE_HOME` and `XDG_RUNTIME_DIR` pointing at private
folders. Two outputs (`--output-count 2`), fractional scale (`kscreen-doctor output.Virtual-0.scale.1.5` inside the
session), the lock screen (leave out `--no-lockscreen`, then `org.freedesktop.ScreenSaver.Lock`) and Xwayland (the
new display shows up in `/tmp/.X11-unix`) all worked. Notes: the Wayland socket path must stay under 108 bytes, so
the runtime folder has to be short. `--scale` on the virtual backend only enlarges the output; use `kscreen-doctor`
for real scaling. Set `GDK_DEBUG=no-portals` so GTK test windows do not wait for portals.

## Plan for Phase 2 (after Roy's "go")

1. **One boundary.** Extract an `IDesktopHelper` interface from today's `ShellHelper` members (same records, one
   exception type). `ShellHelper` becomes the GNOME implementation unchanged. `DesktopControl` connects the helper for
   the running desktop (`XDG_CURRENT_DESKTOP`). No page or runtime code branches on the desktop. Build and test,
   commit.
2. **KDE helper** behind the same interface, one owner per concern:
   - `KWinScript`: a bundled JS script loaded through `/Scripting`. It answers windows, focus, pointer, monitors,
     window-at and activation through one long-poll channel addressed to the app's unique bus name, and pushes focus
     and close events. It is unloaded on exit, and a leftover copy is replaced at start. KWin's UUID window ids map to
     the `ulong` ids the app already uses.
   - `KWinCapture`: ScreenShot2 area and window capture over a pipe, converted to the image the app gets today.
   - `KdeInputGuard`: the fake-input sender process described above; refuses while locked.
   - `KdeShortcuts`: KGlobalAccel with the conflict check, press and release, unregistered on exit.
   - Keyboard layout: `org.kde.keyboard` plus `kxkbrc`, mapped like the GNOME `xkb:` names.
3. **Install.** `install.sh` checks the running desktop. On KDE it writes the same `.desktop` file with
   `X-KDE-DBUS-Restricted-Interfaces=org.kde.KWin.ScreenShot2` and `X-KDE-Wayland-Interfaces=org_kde_kwin_fake_input`
   and installs no extension and needs no re-login. GNOME stays as it is. `uninstall.sh` also removes the KDE
   shortcut entries.
4. **Tests.** Fake-based tests for helper selection, the id table, image conversion, the hotkey conflict check,
   refusal while locked and layout mapping. Then `scripts/linux/run-headless-kwin-probe.sh`: the existing probe
   against a private headless KWin (clicks, keys, Stop All, guard release after a killed app, refusal while locked,
   capture).
5. **Docs.** `docs/LINUX.md` (KDE install, limits), this file, `Where-We-Are.md`.

Each step is built and tested, then committed with what changed for the player, pushed, and a pull request opened into
`main`. If `linux-catch-up-2` work lands on `main`, merge it into `kde-support`. The Windows projects, solution and
lock files are not touched.

**For Roy to decide:** the Settings → Linux setup page today names the GNOME helper. On KDE it would show "KDE
(built in)" status text instead. That is the only visible page change; is that fine?
