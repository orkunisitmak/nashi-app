#!/usr/bin/env bash
# Builds Nashi for Linux and installs it for the current user (no sudo):
#   ~/.local/lib/nashi            self-contained app (no system .NET needed)
#   ~/.local/bin/nashi            command-line entry point
#   ~/.local/share/applications   menu entry and "Open with › Nashi"
#   ~/.local/share/icons          app icon
# Usage: linux/install.sh [--uninstall]
# PREFIX=/opt/nashi BIN_DIR=/usr/local/bin DATA_DIR=/usr/local/share sudo -E linux/install.sh installs system-wide.
set -euo pipefail

repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
data_home=${XDG_DATA_HOME:-$HOME/.local/share}
prefix=${PREFIX:-$HOME/.local/lib/nashi}
bin_dir=${BIN_DIR:-$HOME/.local/bin}
data_dir=${DATA_DIR:-$data_home}
desktop_file=$data_dir/applications/nashi.desktop

refresh_caches() {
  command -v update-desktop-database >/dev/null && update-desktop-database -q "$data_dir/applications" || true
  command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$data_dir/icons/hicolor" || true
}

if [[ ${1:-} == "--uninstall" ]]; then
  # Removes only what this script installed. Library and settings in
  # ${XDG_DATA_HOME:-~/.local/share}/Nashi are kept.
  [[ -L $bin_dir/nashi ]] && rm -f -- "$bin_dir/nashi"
  rm -f -- "$desktop_file"
  for size in 256 512; do rm -f -- "$data_dir/icons/hicolor/${size}x${size}/apps/nashi.png"; done
  [[ -f $prefix/nashi && -f $prefix/Nashi.Core.dll ]] && rm -rf -- "$prefix"
  refresh_caches
  echo "Nashi was removed. Your Library and settings were kept in $data_home/Nashi."
  exit 0
fi

dotnet=$(command -v dotnet || true)
[[ -z $dotnet && -x $HOME/.dotnet/dotnet ]] && dotnet=$HOME/.dotnet/dotnet
if [[ -z $dotnet ]] || ! "$dotnet" --list-sdks | grep -q '^10\.'; then
  echo "The .NET 10 SDK is needed to build Nashi (only for building; the installed app is self-contained)." >&2
  echo "  Arch/CachyOS: sudo pacman -S dotnet-sdk      Fedora: sudo dnf install dotnet-sdk-10.0" >&2
  echo "  Ubuntu/Debian: sudo apt install dotnet-sdk-10.0   or: https://dot.net/v1/dotnet-install.sh --channel 10.0" >&2
  exit 1
fi

case $(uname -m) in x86_64|aarch64|arm64) ;; *) echo "Unsupported CPU: $(uname -m)" >&2; exit 1 ;; esac

stage=$(mktemp -d)
trap 'rm -rf -- "$stage"' EXIT
echo "Building Nashi…"
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$dotnet" publish "$repo/linux/Nashi.Linux" -c Release -o "$stage/app" -v quiet -nologo

# Replace the previous install only after a successful build.
if [[ -e $prefix && ! -f $prefix/Nashi.Core.dll ]]; then
  echo "$prefix exists and is not a Nashi install; choose another PREFIX." >&2; exit 1
fi
mkdir -p "$(dirname "$prefix")" "$bin_dir" "$data_dir/applications"
rm -rf -- "$prefix.previous"
[[ -d $prefix ]] && mv -- "$prefix" "$prefix.previous"
mv -- "$stage/app" "$prefix"
rm -rf -- "$prefix.previous"
ln -sfn "$prefix/nashi" "$bin_dir/nashi"

for size in 256 512; do
  icons=$data_dir/icons/hicolor/${size}x${size}/apps
  mkdir -p "$icons" "$stage/icon-$size"
  "$prefix/nashi" tool resize "$prefix/share/nashi.png" --width "$size" --out "$stage/icon-$size" >/dev/null 2>&1 \
    && mv -f -- "$stage/icon-$size/nashi resize.png" "$icons/nashi.png" || cp -f -- "$prefix/share/nashi.png" "$icons/nashi.png"
done

# Absolute Exec/TryExec: desktop sessions do not always have ~/.local/bin on PATH.
sed -e "s|^Exec=nashi |Exec=$prefix/nashi |" -e "s|^TryExec=nashi$|TryExec=$prefix/nashi|" "$repo/linux/nashi.desktop" >"$desktop_file"
refresh_caches

echo "Nashi is installed. Open it from your app menu, run 'nashi', or right-click files › Open with › Nashi."
case ":$PATH:" in *":$bin_dir:"*) ;; *) echo "Note: $bin_dir is not on your PATH; add it to use the 'nashi' command." ;; esac
"$prefix/nashi" doctor || true
