#!/bin/sh
# Installs Nashi from this extracted folder.
#   ./install.sh            for the current user (no administrator password)
#   sudo ./install.sh --system   for every user, under /opt/nashi
# Run ./uninstall.sh (or with --system) to remove it. Library and settings in
# ~/.local/share/Nashi are never touched by either script.
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ "${1:-}" = "--system" ]; then
  [ "$(id -u)" -eq 0 ] || { echo "Run with sudo for a system-wide install." >&2; exit 1; }
  app=/opt/nashi; bin=/usr/local/bin; applications=/usr/share/applications
else
  data=${XDG_DATA_HOME:-$HOME/.local/share}
  app=$data/nashi-app; bin=$HOME/.local/bin; applications=$data/applications
fi
[ -x "$here/nashi" ] || { echo "Run install.sh from the extracted Nashi folder." >&2; exit 1; }
if [ "$here" != "$app" ]; then
  # Stage beside the target and swap, so a failed copy never leaves a half-installed app.
  mkdir -p "$(dirname "$app")"
  stage="$app.new.$$"; rm -rf "$stage"; cp -a "$here" "$stage"
  [ -d "$app" ] && mv "$app" "$app.old.$$"
  mv "$stage" "$app"; rm -rf "$app.old.$$"
fi
mkdir -p "$bin" "$applications"
ln -sf "$app/nashi" "$bin/nashi"
sed -e "s|@EXEC@|\"$app/nashi\"|" -e "s|@ICON@|$app/packaging/nashi.png|" "$app/packaging/nashi.desktop" > "$applications/nashi.desktop"
chmod 644 "$applications/nashi.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$applications" 2>/dev/null || true
echo "Nashi is installed in $app"
echo "Open it from your applications menu, or run: nashi"
case ":$PATH:" in *":$bin:"*) ;; *) echo "Note: $bin is not on your PATH; the menu entry still works." ;; esac
