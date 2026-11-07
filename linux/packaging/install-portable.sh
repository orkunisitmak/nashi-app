#!/usr/bin/env bash
# Run from an extracted portable bundle. Keeps the previous app as a recoverable sibling directory.
set -euo pipefail
task_source=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
task_base=${XDG_DATA_HOME:-"$HOME/.local/share"}
case "$task_base" in /*) ;; *) echo 'XDG_DATA_HOME must be absolute.' >&2; exit 2;; esac
task_target="$task_base/nashi-portable"
if [[ "$task_source/" == "$task_target/"* || "$task_target/" == "$task_source/"* ]]; then
  echo 'Run the installer from the newly extracted bundle, outside the installed directory.' >&2; exit 2
fi
task_version=$("$task_source/nashi" --version)
test -n "$task_version"
mkdir -p "$task_base"
task_stage=$(mktemp -d "$task_base/.nashi-install.XXXXXX")
trap 'rm -rf "$task_stage"' EXIT
cp -a "$task_source/." "$task_stage/"
task_backup=''
if [ -e "$task_target" ] || [ -L "$task_target" ]; then
  task_backup="$task_target.previous-$(date +%Y%m%d-%H%M%S)-$$"
  mv -- "$task_target" "$task_backup"
fi
if ! mv -- "$task_stage" "$task_target"; then
  if [ -n "$task_backup" ]; then mv -- "$task_backup" "$task_target"; fi
  exit 1
fi
trap - EXIT
echo "Installed Nashi $task_version at $task_target"
if [ -n "$task_backup" ]; then echo "Previous installation kept at $task_backup"; fi
echo "Registering Open With, the nashi command and background autostart."
"$task_target/nashi" --install-integration
