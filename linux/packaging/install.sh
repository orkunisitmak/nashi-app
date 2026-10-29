#!/usr/bin/env bash
# Register this extracted portable directory for the current user. No sudo.
set -euo pipefail
app="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ ! -x "$app/Nashi" || ! -f "$app/nashi.png" ]]; then
  echo 'Run install.sh from the extracted portable Nashi folder.' >&2; exit 1
fi
data="${XDG_DATA_HOME:-$HOME/.local/share}"
if [[ "$data" != /* ]]; then data="$HOME/.local/share"; fi
# Desktop Entry quoting differs from shell quoting. Escape reserved characters
# and field-code percent signs before writing the fixed executable command.
escape_exec() { local value="$1"; value="${value//\\/\\\\}"; value="${value//\"/\\\"}"; value="${value//\$/\\\$}"; value="${value//\`/\\\`}"; value="${value//%/%%}"; value="${value//\\/\\\\}"; printf '%s' "$value"; }
# Plain string values have their own backslash escape syntax.
escape_value() { local value="$1"; value="${value//\\/\\\\}"; printf '%s' "$value"; }
if [[ "$app" == *$'\n'* || "$app" == *$'\r'* ]]; then echo 'Move Nashi to a path without newlines.' >&2; exit 1; fi
mkdir -p "$data/applications"
cat > "$data/applications/io.github.orkunisitmak.Nashi.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Nashi
Comment=Local file conversion and editing
# A fixed launcher also supports literal percent signs in the app path.
Exec=/usr/bin/env -- "$(escape_exec "$app/Nashi")" %F
Icon=$(escape_value "$app/nashi.png")
Terminal=false
Categories=Utility;
StartupWMClass=Nashi
DESKTOP
if command -v update-desktop-database >/dev/null; then update-desktop-database "$data/applications"; fi
printf 'Registered Nashi from %s\nKeep this folder in place. Remove the desktop entry to unregister.\n' "$app"
