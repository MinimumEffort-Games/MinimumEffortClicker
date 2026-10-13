#!/usr/bin/env bash
# Runs the opt-in Linux input probe inside a private, headless GNOME Shell: its own HOME, D-Bus
# session and Wayland display. Nothing is sent to your real desktop.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONFIGURATION="${1:-Debug}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/mec-probe.XXXXXX")"
RUNTIME="$(mktemp -d /tmp/mecrt.XXXXXX)"   # Wayland socket paths must stay short.
chmod 700 "$RUNTIME"
cleanup() { fusermount3 -u "$RUNTIME/doc" 2>/dev/null || fusermount -u "$RUNTIME/doc" 2>/dev/null || true; rm -rf "$WORK" "$RUNTIME" 2>/dev/null || true; }
trap cleanup EXIT

DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
"$DOTNET" build "$ROOT/tools/LinuxProbe/LinuxProbe.csproj" -c "$CONFIGURATION" -nologo -v:q
PROBE="$ROOT/tools/LinuxProbe/bin/$CONFIGURATION/net10.0/MinimumEffortClicker.LinuxProbe"
UUID="me-clicker-helper@minimumeffort-games.github.io"
mkdir -p "$WORK/home/.local/share/gnome-shell/extensions" "$WORK/home/.config/me-clicker-helper"
cp -r "$ROOT/linux/gnome-extension/$UUID" "$WORK/home/.local/share/gnome-shell/extensions/"
readlink -f "$PROBE" > "$WORK/home/.config/me-clicker-helper/allowed-clients"

cat > "$WORK/session.sh" <<'INNER'
set -u
gsettings set org.gnome.shell disable-user-extensions false
gsettings set org.gnome.shell enabled-extensions "['me-clicker-helper@minimumeffort-games.github.io']"
gsettings set org.gnome.shell welcome-dialog-last-shown-version '999'
gnome-shell --headless --wayland --no-x11 --virtual-monitor 1280x800 --virtual-monitor 1024x768 --wayland-display=mec-probe-0 > "$WORK/shell.log" 2>&1 &
SHELL_PID=$!
for _ in $(seq 1 80); do
  gdbus introspect --session --dest io.github.MinimumEffortGames.ClickerHelper --object-path /io/github/MinimumEffortGames/ClickerHelper >/dev/null 2>&1 && break
  sleep 0.25
done
export WAYLAND_DISPLAY=mec-probe-0 MEC_PROBE_SHELL=headless
python3 "$ROOT/tools/LinuxProbe/probe-target.py" "$WORK/target.log" >/dev/null 2>&1 &
TARGET_PID=$!
for _ in $(seq 1 40); do grep -q ready "$WORK/target.log" 2>/dev/null && break; sleep 0.25; done
# The overview opens on startup; Escape closes it so the target can take focus.
set +e
"$PROBE" --isolated-headless-shell
RESULT=$?
set -e
kill "$TARGET_PID" "$SHELL_PID" 2>/dev/null; wait "$SHELL_PID" 2>/dev/null
echo "--- probe target received ---"; cat "$WORK/target.log"
for expected in "press button=1 n=1 x=" "press button=3 n=2" "key down evdev=17 keyval=W" "key down evdev=60 keyval=F2"; do
  if ! grep -q "$expected" "$WORK/target.log"; then echo "FAIL target did not receive: $expected"; RESULT=1; fi
done
grep -E -i "clicker|JS ERROR|error" "$WORK/shell.log" | head -30 || true
exit $RESULT
INNER
env -i PATH="/usr/bin:/bin" HOME="$WORK/home" XDG_RUNTIME_DIR="$RUNTIME" XDG_CONFIG_HOME="$WORK/home/.config" \
  XDG_DATA_HOME="$WORK/home/.local/share" XDG_CACHE_HOME="$WORK/home/.cache" DOTNET_ROOT="$(dirname "$(readlink -f "$DOTNET")")" \
  ROOT="$ROOT" WORK="$WORK" PROBE="$PROBE" dbus-run-session -- bash "$WORK/session.sh"
