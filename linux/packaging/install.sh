#!/bin/sh
# Per-user installation of an extracted Nashi tarball; no sudo or system changes.
set -eu
NASHI_SOURCE=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
NASHI_PREFIX="${NASHI_INSTALL_PREFIX:-$HOME/.local}"
NASHI_INSTALL="$NASHI_PREFIX/opt/nashi"
NASHI_DATA="${NASHI_DATA_ROOT:-${XDG_DATA_HOME:-$NASHI_PREFIX/share}}"
if [ "$NASHI_SOURCE" = "$NASHI_INSTALL" ]; then
    echo 'Already installed here.'
    exit 0
fi
mkdir -p "$NASHI_PREFIX/opt" "$NASHI_PREFIX/bin" "$NASHI_DATA/applications" "$NASHI_DATA/icons/hicolor/256x256/apps"
NASHI_STAGE=$(mktemp -d "$NASHI_PREFIX/opt/.nashi-install.XXXXXXXX")
NASHI_BACKUP=""
trap 'if [ -d "$NASHI_STAGE" ]; then rm -rf -- "$NASHI_STAGE"; fi; if [ -n "$NASHI_BACKUP" ] && [ ! -e "$NASHI_INSTALL" ]; then mv -- "$NASHI_BACKUP" "$NASHI_INSTALL"; fi' EXIT
cp -a "$NASHI_SOURCE/." "$NASHI_STAGE/"
if [ -e "$NASHI_INSTALL" ] || [ -L "$NASHI_INSTALL" ]; then
    NASHI_BACKUP="$NASHI_INSTALL.previous.$$"
    mv -- "$NASHI_INSTALL" "$NASHI_BACKUP"
fi
mv -- "$NASHI_STAGE" "$NASHI_INSTALL"
ln -sfn "$NASHI_INSTALL/Nashi" "$NASHI_PREFIX/bin/nashi"
cp "$NASHI_INSTALL/nashi.png" "$NASHI_DATA/icons/hicolor/256x256/apps/nashi.png"
# Desktop Exec uses its own quoting rules, independent of shell quoting.
NASHI_QUOTED=$(printf '%s' "$NASHI_INSTALL/Nashi" | sed 's/[\\"`$]/\\&/g; s/[&|]/\\&/g')
sed "s|^Exec=.*|Exec=\"$NASHI_QUOTED\" %F|" "$NASHI_INSTALL/nashi.desktop" > "$NASHI_DATA/applications/nashi.desktop"
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database "$NASHI_DATA/applications"; fi
printf 'Nashi installed. Open Nashi from your application menu or run %s\n' "$NASHI_INSTALL/Nashi"
if [ -n "$NASHI_BACKUP" ]; then printf 'Previous installation retained: %s\n' "$NASHI_BACKUP"; fi
