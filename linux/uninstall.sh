#!/usr/bin/env bash
# Removes the per-user install, launcher and GNOME helper. Saved macros in the app's Data folder
# are kept unless you pass --delete-data. The optional input-access rule is removed separately with
#   sudo bash setup-input-access.sh --remove
set -euo pipefail
ROOT="${XDG_DATA_HOME:-$HOME/.local/share}/minimum-effort-clicker"
UUID="me-clicker-helper@minimumeffort-games.github.io"
gnome-extensions disable "$UUID" 2>/dev/null || true
rm -rf "${XDG_DATA_HOME:-$HOME/.local/share}/gnome-shell/extensions/$UUID" "${XDG_CONFIG_HOME:-$HOME/.config}/me-clicker-helper"
rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/applications/io.github.MinimumEffortGames.Clicker.desktop"
if [[ ${1:-} == --delete-data ]]; then rm -rf "$ROOT"; echo "Removed the app and its data."
else
  find "$ROOT/app" -mindepth 1 -maxdepth 1 ! -name Data -exec rm -rf {} + 2>/dev/null || true
  echo "Removed the app. Saved data is kept in $ROOT/app/Data."
fi
