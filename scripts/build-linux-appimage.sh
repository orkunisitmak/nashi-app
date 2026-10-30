#!/usr/bin/env bash
# Builds build/linux/Nashi-Linux-x86_64.AppImage (unsigned preview).
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1

TOOL_VERSION=1.9.0
TOOL_URL="https://github.com/AppImage/appimagetool/releases/download/$TOOL_VERSION/appimagetool-x86_64.AppImage"
# Upstream publishes no checksum; this hash was pinned from the 1.9.0 release asset.
TOOL_SHA256=46fdd785094c7f6e545b61afcfb0f3d98d8eab243f644b4b17698c01d06083d1

out=build/linux
app=$out/AppDir
tool=$out/appimagetool-$TOOL_VERSION-x86_64.AppImage

rm -rf "$app"
mkdir -p "$app/usr/lib/nashi" "$app/usr/share/doc/nashi"
"${DOTNET:-dotnet}" publish linux/Nashi.Linux -c Release -p:RestoreLockedMode=true -o "$app/usr/lib/nashi"

install -m 755 linux/packaging/AppRun "$app/AppRun"
install -m 644 linux/packaging/nashi.desktop "$app/nashi.desktop"
install -m 644 Resources/Brand/NashiIcon.png "$app/nashi.png"
install -m 644 LICENSE linux/THIRD-PARTY-NOTICES.txt "$app/usr/share/doc/nashi/"
# Every bundled third-party license text. WPF is not part of the Linux app, and its PDFium notices
# (with the Linux library's hashes) come from linux/licenses, not the Windows copy.
mkdir -p "$app/usr/share/doc/nashi/licenses"
for f in windows/licenses/*.txt linux/licenses/*.txt; do
  case "$f" in windows/licenses/WPF-*|windows/licenses/PDFium-*) continue;; esac
  install -m 644 "$f" "$app/usr/share/doc/nashi/licenses/"
done

if ! { [ -f "$tool" ] && echo "$TOOL_SHA256  $tool" | sha256sum -c --status; }; then
  curl -fsSL -o "$tool" "$TOOL_URL"
  echo "$TOOL_SHA256  $tool" | sha256sum -c --status || { echo "appimagetool checksum mismatch" >&2; rm -f "$tool"; exit 1; }
fi
chmod +x "$tool"

# Extract-and-run so FUSE is not required (CI runners lack it).
APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64 "$tool" "$app" "$out/Nashi-Linux-x86_64.AppImage"
(cd "$out" && sha256sum Nashi-Linux-x86_64.AppImage > Nashi-Linux-x86_64.AppImage.sha256)
echo "Built $out/Nashi-Linux-x86_64.AppImage"
