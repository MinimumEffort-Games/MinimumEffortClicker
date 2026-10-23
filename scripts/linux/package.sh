#!/usr/bin/env bash
# Builds the self-contained linux-x64 package and a .tar.gz with its SHA-256 checksum.
# --steam builds the Steam package instead: no portable.mode, so data lives in ~/.local/share/MinimumEffortClicker
# like the Windows Steam build's %LocalAppData%, and a .zip for SteamPipe Web Uploads whose entries keep their Linux
# file modes, so the clicker and its scripts stay executable. Build it on Linux: a Windows checkout has CRLF scripts.
# Usage: package.sh [--steam] <fresh destination folder>
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
STEAM=0
if [[ ${1:-} == --steam ]]; then STEAM=1; shift; fi
[[ $# -eq 1 ]] || { echo "Usage: package.sh [--steam] <fresh destination folder>" >&2; exit 2; }
DEST="$(realpath -m "$1")"
[[ -e "$DEST" || -e "$DEST.tar.gz" || -e "$DEST.zip" ]] && { echo "Choose a fresh destination; existing builds are never overwritten." >&2; exit 1; }
if [[ $STEAM == 1 ]] && ! command -v python3 >/dev/null; then echo "--steam needs python3 to write the upload zip." >&2; exit 1; fi
bash "$ROOT/scripts/linux/test.sh" Release
mkdir -p "$DEST"
"$DOTNET" publish "$ROOT/src/MinimumEffortClicker.Linux.App/MinimumEffortClicker.Linux.App.csproj" -c Release --self-contained true \
  -p:PublishTrimmed=false -p:RestoreLockedMode=true -nr:false -p:UseSharedCompilation=false -o "$DEST"
[[ $STEAM == 1 ]] || echo "Store local user data in Data beside these executables." > "$DEST/portable.mode"
mkdir -p "$DEST/gnome-extension" "$DEST/licenses" "$DEST/docs"
cp -r "$ROOT/linux/gnome-extension/me-clicker-helper@minimumeffort-games.github.io" "$DEST/gnome-extension/"
cp "$ROOT/linux/setup-input-access.sh" "$ROOT/linux/install.sh" "$ROOT/linux/uninstall.sh" "$DEST/"
chmod +x "$DEST/MinimumEffortClicker" "$DEST/setup-input-access.sh" "$DEST/install.sh" "$DEST/uninstall.sh"
cp "$ROOT/assets/brand/app-icon/app-icon.png" "$DEST/minimum-effort-clicker.png"
cp -r "$ROOT/licenses/dotnet" "$ROOT/licenses/nuget" "$ROOT/licenses/native" "$ROOT/licenses/nuget-inventory.json" "$DEST/licenses/"
cp "$ROOT/LICENSE" "$ROOT/THIRD_PARTY_NOTICES.md" "$DEST/"
cp "$ROOT/docs/LINUX.md" "$DEST/START HERE.md"
cp "$ROOT/docs/RELEASE_STATUS.md" "$DEST/docs/"
if find "$DEST" \( -name Data -o -name .git -o -name '*.pfx' -o -name '*.p12' -o -name '*.pem' -o -name '*.key' -o -name '*.ttf' -o -name steam_appid.txt \) | grep -q .; then
  echo "Unexpected private data, key or font in the package." >&2; exit 1
fi
if grep -l $'\r' "$DEST"/*.sh >/dev/null 2>&1; then echo "A shell script has Windows line ends; build on a Linux checkout." >&2; exit 1; fi
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"
COMMIT="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || echo unknown)"
if [[ $STEAM == 1 ]]; then KIND="Steam build"; PORTABLE=false; else KIND="Unsigned public preview"; PORTABLE=true; fi
(cd "$DEST" && find . -type f ! -name build-manifest.json -print0 | sort -z | xargs -0 sha256sum) > "$DEST/build-manifest.sha256"
printf '{\n  "buildUtc": "%s",\n  "version": "%s",\n  "kind": "%s",\n  "platform": "linux-x64",\n  "sourceCommit": "%s",\n  "portable": %s\n}\n' \
  "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$VERSION" "$KIND" "$COMMIT" "$PORTABLE" > "$DEST/build-manifest.json"
if [[ $STEAM == 1 ]]; then
  # The files sit at the zip's root, so the Steam launch option is just "MinimumEffortClicker".
  python3 - "$DEST" "$DEST.zip" <<'PY'
import os, sys, zipfile
root, target = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
    for folder, _, files in sorted(os.walk(root)):
        for name in sorted(files):
            path = os.path.join(folder, name)
            archive.write(path, os.path.relpath(path, root))  # ZipInfo.from_file keeps the file mode.
PY
  (cd "$(dirname "$DEST")" && sha256sum "$(basename "$DEST").zip" > "$(basename "$DEST").zip.sha256")
  echo "$DEST.zip"
else
  tar -C "$(dirname "$DEST")" -czf "$DEST.tar.gz" "$(basename "$DEST")"
  (cd "$(dirname "$DEST")" && sha256sum "$(basename "$DEST").tar.gz" > "$(basename "$DEST").tar.gz.sha256")
  echo "$DEST"
fi
