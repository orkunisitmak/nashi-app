#!/bin/sh
# Per-user installation of an extracted Nashi tarball; no sudo or system changes.
set -eu
NASHI_SOURCE=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
NASHI_INSTALL="$HOME/.local/opt/nashi"
NASHI_DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
if [ "$NASHI_SOURCE" = "$NASHI_INSTALL" ]; then
    echo 'Already installed here.'
    exit 0
fi
mkdir -p "$NASHI_INSTALL" "$HOME/.local/bin" "$NASHI_DATA/applications" "$NASHI_DATA/icons/hicolor/256x256/apps"
cp -a "$NASHI_SOURCE/." "$NASHI_INSTALL/"
ln -sfn "$NASHI_INSTALL/Nashi" "$HOME/.local/bin/nashi"
cp "$NASHI_INSTALL/nashi.png" "$NASHI_DATA/icons/hicolor/256x256/apps/nashi.png"
# Desktop Exec uses its own quoting rules, independent of shell quoting.
NASHI_QUOTED=$(printf '%s' "$NASHI_INSTALL/Nashi" | sed 's/[\\"`$]/\\&/g')
sed "s|^Exec=.*|Exec=\"$NASHI_QUOTED\" %F|" "$NASHI_INSTALL/nashi.desktop" > "$NASHI_DATA/applications/nashi.desktop"
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database "$NASHI_DATA/applications"; fi
printf 'Nashi installed. Open Nashi from your application menu or run %s\n' "$NASHI_INSTALL/Nashi"
