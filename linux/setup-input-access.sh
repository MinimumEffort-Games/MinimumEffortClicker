#!/usr/bin/env bash
# Minimum Effort Clicker: lets the person signed in at this computer read keyboards and mice.
# The clicker needs this for global hotkeys that work in every app, mouse-button shortcuts,
# recording, and stopping playback when you touch the mouse or keyboard yourself.
#
# It adds one udev rule that grants the active local session read access (the same "uaccess"
# mechanism GNOME uses for game controllers). Nothing else changes. Run with sudo:
#   sudo bash setup-input-access.sh           install the rule
#   sudo bash setup-input-access.sh --remove  remove it again (and take back the access it gave)
set -euo pipefail
RULE=/etc/udev/rules.d/70-minimum-effort-clicker-input.rules
if [[ $EUID -ne 0 ]]; then echo "Run this with sudo." >&2; exit 1; fi
if [[ ${1:-} == --remove ]]; then
  rm -f "$RULE"
  echo "Removed $RULE."
  REMOVING=1
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
if [[ ${REMOVING:-} == 1 ]]; then
  # Without the rule no new access is granted, but access the rule already gave stays on each device until it is
  # plugged in again or the computer restarts. Take it back now from keyboards, mice and touchpads; game
  # controllers keep theirs, and so does any device another rule still grants to the session (SteamOS does that
  # for the Steam Deck's own keyboard and controller): that access was never ours to take.
  udevadm settle
  if command -v setfacl >/dev/null; then
    for node in /dev/input/event*; do
      props="$(udevadm info --query=property --name="$node" 2>/dev/null)" || continue
      grep -qE '^ID_INPUT_(KEYBOARD|MOUSE|TOUCHPAD)=1$' <<<"$props" || continue
      grep -qE '^ID_INPUT_JOYSTICK=' <<<"$props" && continue
      # CURRENT_TAGS are the tags of the rules as they are now (older udev only has TAGS).
      tags="$(grep -E '^CURRENT_TAGS=' <<<"$props" || grep -E '^TAGS=' <<<"$props" || true)"
      [[ $tags == *:uaccess:* ]] && continue
      setfacl --remove-all "$node"
    done
    echo "Read access to keyboards, mice and touchpads is taken back."
  else
    echo "setfacl is missing: access already given ends when you restart the computer or plug the devices in again."
  fi
fi
echo "Done. In Minimum Effort Clicker, open Settings and choose Check again."
