#!/usr/bin/env bash
# Self-contained tar distribution. Building never installs or modifies the user's existing Nashi.
set -euo pipefail
cd "$(dirname "$0")/../.."
task_rid=${1:-linux-x64}
case "$task_rid" in linux-x64|linux-arm64) ;; *) echo 'Use linux-x64 or linux-arm64.' >&2; exit 2;; esac
task_output="$PWD/build/linux"
mkdir -p "$task_output"
task_stage=$(mktemp -d "$task_output/.portable.XXXXXX")
trap 'rm -rf "$task_stage"' EXIT
task_root="$task_stage/Nashi"
mkdir -p "$task_root/licenses"
"${DOTNET:-dotnet}" publish linux/Nashi.Linux -p:NashiLinuxRuntime="$task_rid" -c Release -m:1 --self-contained true -p:RestoreLockedMode=true -o "$task_root/app"
install -m 644 LICENSE "$task_root/"
install -m 644 linux/THIRD-PARTY-NOTICES.txt "$task_root/"
for task_license in windows/licenses/*.txt linux/licenses/*.txt; do
  case "$task_license" in windows/licenses/WPF-*|windows/licenses/PDFium-*) continue;; esac
  install -m 644 "$task_license" "$task_root/licenses/"
done
install -m 755 linux/packaging/install-portable.sh "$task_root/install.sh"
cat > "$task_root/nashi" <<'LAUNCHER'
#!/bin/sh
task_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec "$task_dir/app/Nashi" "$@"
LAUNCHER
chmod +x "$task_root/nashi"
if [ "$task_rid" = linux-arm64 ]; then
  # Cross-checked against chromium/8044's ARM64 archive, including all 15 license payloads.
  (cd "$task_root/app" && echo '794a43ec86642d48ed69becb6923022cd0b34d23c81c524452d6f0549398d710  libpdfium.so' | sha256sum -c)
  (cd "$task_root/app" && sha256sum libpdfium.so) > "$task_root/PDFium-ARM64.sha256"
  cat > "$task_root/PDFium-ARM64-PROVENANCE.txt" <<'PROVENANCE'
Source: https://github.com/bblanchon/pdfium-binaries/releases/download/chromium%2F8044/pdfium-linux-arm64.tgz
Archive SHA-256: e98400ef5f005f27cfba5c14f72d464e25187298f04950de46646033cf24cef0
Native SHA-256: 794a43ec86642d48ed69becb6923022cd0b34d23c81c524452d6f0549398d710
All 15 license payloads match licenses/PDFium-THIRD-PARTY-NOTICES.txt.
PROVENANCE
  echo 'Experimental ARM64 build. Full media setup and hardware verification are not complete.' > "$task_root/EXPERIMENTAL-ARM64.txt"
fi
task_archive="Nashi-$task_rid.tar.gz"
tar -C "$task_stage" -czf "$task_output/$task_archive" Nashi
(cd "$task_output" && sha256sum "$task_archive" > "$task_archive.sha256")
echo "Built $task_output/$task_archive"
