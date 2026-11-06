#!/bin/sh
# Removes the Nashi app installed by install.sh. Your Library, settings and
# downloaded tools in ~/.local/share/Nashi are kept; delete that folder yourself
# if you want them gone too.
set -eu
if [ "${1:-}" = "--system" ]; then
  [ "$(id -u)" -eq 0 ] || { echo "Run with sudo to remove a system-wide install." >&2; exit 1; }
  app=/opt/nashi; bin=/usr/local/bin; applications=/usr/share/applications
else
  data=${XDG_DATA_HOME:-$HOME/.local/share}
  app=$data/nashi-app; bin=$HOME/.local/bin; applications=$data/applications
fi
[ -L "$bin/nashi" ] && rm -f "$bin/nashi"
rm -f "$applications/nashi.desktop"
[ -x "$app/nashi" ] && rm -rf "$app"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$applications" 2>/dev/null || true
echo "Nashi was removed. Library and settings remain in ${XDG_DATA_HOME:-$HOME/.local/share}/Nashi."
