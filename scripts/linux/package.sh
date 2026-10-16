#!/usr/bin/env bash
# Builds the self-contained linux-x64 package and a .tar.gz with its SHA-256 checksum.
# Usage: package.sh <fresh destination folder>
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
[[ $# -eq 1 ]] || { echo "Usage: package.sh <fresh destination folder>" >&2; exit 2; }
DEST="$(realpath -m "$1")"
[[ -e "$DEST" || -e "$DEST.tar.gz" ]] && { echo "Choose a fresh destination; existing builds are never overwritten." >&2; exit 1; }
bash "$ROOT/scripts/linux/test.sh" Release
mkdir -p "$DEST"
"$DOTNET" publish "$ROOT/src/MinimumEffortClicker.Linux.App/MinimumEffortClicker.Linux.App.csproj" -c Release --self-contained true \
  -p:PublishTrimmed=false -p:RestoreLockedMode=true -nr:false -p:UseSharedCompilation=false -o "$DEST"
echo "Store local user data in Data beside these executables." > "$DEST/portable.mode"
mkdir -p "$DEST/gnome-extension" "$DEST/licenses" "$DEST/docs"
cp -r "$ROOT/linux/gnome-extension/me-clicker-helper@minimumeffort-games.github.io" "$DEST/gnome-extension/"
cp "$ROOT/linux/setup-input-access.sh" "$ROOT/linux/install.sh" "$ROOT/linux/uninstall.sh" "$DEST/"
chmod +x "$DEST/MinimumEffortClicker" "$DEST/setup-input-access.sh" "$DEST/install.sh" "$DEST/uninstall.sh"
cp "$ROOT/assets/brand/rounded-r26/shrug-rounded-r26.png" "$DEST/minimum-effort-clicker.png"
cp -r "$ROOT/licenses/dotnet" "$ROOT/licenses/nuget" "$ROOT/licenses/native" "$ROOT/licenses/nuget-inventory.json" "$DEST/licenses/"
cp "$ROOT/LICENSE" "$ROOT/THIRD_PARTY_NOTICES.md" "$DEST/"
cp "$ROOT/docs/LINUX.md" "$DEST/START HERE.md"
cp "$ROOT/docs/RELEASE_STATUS.md" "$DEST/docs/"
if find "$DEST" \( -name Data -o -name .git -o -name '*.pfx' -o -name '*.p12' -o -name '*.pem' -o -name '*.key' -o -name '*.ttf' \) | grep -q .; then
  echo "Unexpected private data, key or font in the package." >&2; exit 1
fi
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"
COMMIT="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || echo unknown)"
(cd "$DEST" && find . -type f ! -name build-manifest.json -print0 | sort -z | xargs -0 sha256sum) > "$DEST/build-manifest.sha256"
printf '{\n  "buildUtc": "%s",\n  "version": "%s",\n  "kind": "Unsigned public preview",\n  "platform": "linux-x64",\n  "sourceCommit": "%s",\n  "portable": true\n}\n' \
  "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$VERSION" "$COMMIT" > "$DEST/build-manifest.json"
tar -C "$(dirname "$DEST")" -czf "$DEST.tar.gz" "$(basename "$DEST")"
(cd "$(dirname "$DEST")" && sha256sum "$(basename "$DEST").tar.gz" > "$(basename "$DEST").tar.gz.sha256")
echo "$DEST"
