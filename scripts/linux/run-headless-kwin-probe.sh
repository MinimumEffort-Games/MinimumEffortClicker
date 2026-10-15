#!/usr/bin/env bash
# Runs the opt-in Linux input probe inside a private, headless KWin (KDE Plasma's compositor): its own HOME, D-Bus
# session and Wayland display. Nothing is sent to your real desktop. Besides the probe that also runs under GNOME it
# checks the two KDE safety rules: input a killed clicker held is released, and nothing is sent while the screen is
# locked (KWin itself does not block fake input on the lock screen). Needs kwin_wayland (Plasma 6), python-gobject and GTK 4.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONFIGURATION="${1:-Debug}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/mec-kwin-probe.XXXXXX")"
RUNTIME="$(mktemp -d /tmp/mecrt.XXXXXX)"   # Wayland socket paths must stay short.
chmod 700 "$RUNTIME"
cleanup() { rm -rf "$WORK" "$RUNTIME" 2>/dev/null || true; }
trap cleanup EXIT

DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
"$DOTNET" build "$ROOT/tools/LinuxProbe/LinuxProbe.csproj" -c "$CONFIGURATION" -nologo -v:q
PROBE="$(readlink -f "$ROOT/tools/LinuxProbe/bin/$CONFIGURATION/net10.0/MinimumEffortClicker.LinuxProbe")"
mkdir -p "$WORK/home/.local/share/applications" "$WORK/home/.config"
# What install.sh writes for the app: KWin lets this executable capture the screen and use fake input.
cat > "$WORK/home/.local/share/applications/mec-probe.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=MEC probe
Exec=$PROBE
NoDisplay=true
X-KDE-DBUS-Restricted-Interfaces=org.kde.KWin.ScreenShot2
X-KDE-Wayland-Interfaces=org_kde_kwin_fake_input
DESKTOP

cat > "$WORK/session.sh" <<'INNER'
set -u
# KWin's own locker locks a private session at once, so a stand-in reports the lock state the way KWin does.
python3 "$ROOT/tools/LinuxProbe/fake-screensaver.py" &
SAVER_PID=$!
kwin_wayland --virtual --no-lockscreen --socket mec-probe-kwin --width 1280 --height 800 --output-count 2 > "$WORK/kwin.log" 2>&1 &
KWIN_PID=$!
for _ in $(seq 1 80); do
  gdbus introspect --session --dest org.kde.KWin --object-path /Scripting >/dev/null 2>&1 \
    && gdbus introspect --session --dest org.freedesktop.ScreenSaver --object-path /ScreenSaver >/dev/null 2>&1 && break
  sleep 0.25
done
export WAYLAND_DISPLAY=mec-probe-kwin MEC_PROBE_SHELL=headless-kwin
python3 "$ROOT/tools/LinuxProbe/probe-target.py" "$WORK/target.log" >/dev/null 2>&1 &
TARGET_PID=$!
for _ in $(seq 1 40); do grep -q ready "$WORK/target.log" 2>/dev/null && break; sleep 0.25; done
set +e
"$PROBE" --isolated-headless-shell
RESULT=$?

echo "--- a killed clicker's held button and Shift are released"
MARK=$(wc -l < "$WORK/target.log")
"$PROBE" --hold-until-killed > "$WORK/hold.log" 2>&1 &
HOLDER=$!
for _ in $(seq 1 60); do grep -q HOLDING "$WORK/hold.log" && break; sleep 0.25; done
kill -9 "$HOLDER"; wait "$HOLDER" 2>/dev/null
sleep 1.5
tail -n +"$((MARK + 1))" "$WORK/target.log"
for expected in "press button=1" "release button=1" "key down evdev=42" "key up evdev=42"; do
  if tail -n +"$((MARK + 1))" "$WORK/target.log" | grep -q "$expected"; then echo "PASS $expected"; else echo "FAIL target did not receive: $expected"; RESULT=1; fi
done

echo "--- nothing is sent while the screen is locked"
gdbus call --session --dest org.freedesktop.ScreenSaver --object-path /ScreenSaver --method org.freedesktop.ScreenSaver.Lock >/dev/null
sleep 2
MARK=$(wc -l < "$WORK/target.log")
"$PROBE" --locked-screen || RESULT=1
if tail -n +"$((MARK + 1))" "$WORK/target.log" | grep -q "evdev=17"; then echo "FAIL a key reached the target while locked"; RESULT=1; fi
set -e
kill "$TARGET_PID" "$KWIN_PID" "$SAVER_PID" 2>/dev/null; wait "$KWIN_PID" 2>/dev/null || true
echo "--- probe target received ---"; cat "$WORK/target.log"
for expected in "press button=1 n=1 x=" "press button=3 n=2" "key down evdev=17 keyval=W" "key down evdev=60 keyval=F2"; do
  if ! grep -q "$expected" "$WORK/target.log"; then echo "FAIL target did not receive: $expected"; RESULT=1; fi
done
grep -E -i "me-clicker|clicker-kwin|js error|script.*error" "$WORK/kwin.log" | head -30 || true
exit $RESULT
INNER
env -i PATH="/usr/bin:/bin" HOME="$WORK/home" XDG_RUNTIME_DIR="$RUNTIME" XDG_CONFIG_HOME="$WORK/home/.config" \
  XDG_DATA_HOME="$WORK/home/.local/share" XDG_CACHE_HOME="$WORK/home/.cache" XDG_STATE_HOME="$WORK/home/.local/state" \
  XDG_CURRENT_DESKTOP=KDE XDG_SESSION_TYPE=wayland KDE_FULL_SESSION=true GDK_DEBUG=no-portals \
  DOTNET_ROOT="$(dirname "$(readlink -f "$DOTNET")")" ROOT="$ROOT" WORK="$WORK" PROBE="$PROBE" dbus-run-session -- bash "$WORK/session.sh"
