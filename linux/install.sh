#!/usr/bin/env bash
# Installs Minimum Effort Clicker for the current user (no sudo): the app, a launcher in the
# applications menu, and the helper for the running desktop: the ME Clicker GNOME helper on GNOME, or
# on KDE Plasma the launcher lines that let KWin give this app screen capture and input (nothing to log
# out for). Run it from the extracted package folder.
set -euo pipefail
SOURCE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARGET="${XDG_DATA_HOME:-$HOME/.local/share}/minimum-effort-clicker/app"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
UUID="me-clicker-helper@minimumeffort-games.github.io"
EXTENSIONS="${XDG_DATA_HOME:-$HOME/.local/share}/gnome-shell/extensions"
ALLOW="${XDG_CONFIG_HOME:-$HOME/.config}/me-clicker-helper/allowed-clients"

if [[ "$SOURCE" != "$TARGET" ]]; then
  if [[ -d "$TARGET/Data" ]]; then
    # Keep saved macros and settings from an earlier install.
    mkdir -p "$TARGET.new"; cp -a "$SOURCE/." "$TARGET.new/"; rm -rf "$TARGET.new/Data"; mv "$TARGET/Data" "$TARGET.new/Data"
    rm -rf "$TARGET"; mv "$TARGET.new" "$TARGET"
  else
    rm -rf "$TARGET"; mkdir -p "$(dirname "$TARGET")"; cp -a "$SOURCE" "$TARGET"
  fi
fi

mkdir -p "$APPS"
cat > "$APPS/io.github.MinimumEffortGames.Clicker.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Minimum Effort Clicker
Comment=Repeat clicks and keys, toggles, recordings and screenshots
Exec="$TARGET/MinimumEffortClicker"
Icon=$TARGET/minimum-effort-clicker.png
Terminal=false
Categories=Utility;
StartupWMClass=MinimumEffortClicker
X-KDE-DBUS-Restricted-Interfaces=org.kde.KWin.ScreenShot2
X-KDE-Wayland-Interfaces=org_kde_kwin_fake_input
DESKTOP

if [[ ":${XDG_CURRENT_DESKTOP:-}:" == *:KDE:* ]]; then
  # KWin reads the two X-KDE lines above within a few seconds; the KDE helper is KWin itself.
  echo "KDE Plasma: KWin now lets Minimum Effort Clicker capture the screen and send input. No log-out needed."
  echo "Minimum Effort Clicker installed in $TARGET and added to your applications."
  echo "Optional, for recording, mouse-button shortcuts and hotkeys in every app:"
  echo "  sudo bash '$TARGET/setup-input-access.sh'"
  exit 0
fi

mkdir -p "$EXTENSIONS" "$(dirname "$ALLOW")"
rm -rf "$EXTENSIONS/$UUID"; cp -r "$TARGET/gnome-extension/$UUID" "$EXTENSIONS/"
EXECUTABLE="$(readlink -f "$TARGET/MinimumEffortClicker")"
touch "$ALLOW"
grep -qxF "$EXECUTABLE" "$ALLOW" || echo "$EXECUTABLE" >> "$ALLOW"
if ! gnome-extensions enable "$UUID" 2>/dev/null; then
  CURRENT="$(gsettings get org.gnome.shell enabled-extensions 2>/dev/null || echo '@as []')"
  if [[ "$CURRENT" != *"$UUID"* ]]; then
    if [[ "$CURRENT" == "@as []" || -z "$CURRENT" ]]; then NEW="['$UUID']"; else NEW="${CURRENT%]}, '$UUID']"; fi
    gsettings set org.gnome.shell enabled-extensions "$NEW" 2>/dev/null || true
  fi
  gsettings set org.gnome.shell disable-user-extensions false 2>/dev/null || true
  echo "GNOME helper installed. Log out and back in once so GNOME loads it."
else
  echo "GNOME helper installed and enabled."
fi
echo "Minimum Effort Clicker installed in $TARGET and added to your applications."
echo "Optional, for recording, mouse-button shortcuts and hotkeys in every app:"
echo "  sudo bash '$TARGET/setup-input-access.sh'"
