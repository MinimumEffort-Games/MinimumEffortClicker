#!/usr/bin/env bash
# Minimum Effort Clicker: lets the person signed in at this computer read keyboards and mice.
# The clicker needs this for global hotkeys that work in every app, mouse-button shortcuts,
# recording, and stopping playback when you touch the mouse or keyboard yourself.
#
# It adds one udev rule that grants the active local session read access (the same "uaccess"
# mechanism GNOME uses for game controllers). Nothing else changes. Run with sudo:
#   sudo bash setup-input-access.sh           install the rule
#   sudo bash setup-input-access.sh --remove  remove it again
set -euo pipefail
RULE=/etc/udev/rules.d/70-minimum-effort-clicker-input.rules
if [[ $EUID -ne 0 ]]; then echo "Run this with sudo." >&2; exit 1; fi
if [[ ${1:-} == --remove ]]; then
  rm -f "$RULE"
  echo "Removed $RULE."
else
  cat > "$RULE" <<'RULES'
# Minimum Effort Clicker: the active local session may read keyboards, mice and touchpads.
SUBSYSTEM=="input", KERNEL=="event*", ENV{ID_INPUT_KEYBOARD}=="1", TAG+="uaccess"
SUBSYSTEM=="input", KERNEL=="event*", ENV{ID_INPUT_MOUSE}=="1", TAG+="uaccess"
SUBSYSTEM=="input", KERNEL=="event*", ENV{ID_INPUT_TOUCHPAD}=="1", TAG+="uaccess"
RULES
  echo "Installed $RULE."
fi
udevadm control --reload
udevadm trigger --subsystem-match=input --action=change
echo "Done. In Minimum Effort Clicker, open Settings and choose Check again."
