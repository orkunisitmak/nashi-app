#!/usr/bin/env bash
set -euo pipefail
umask 022
NASHI_ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$NASHI_ROOT"
NASHI_RID=${1:-linux-x64}
case "$NASHI_RID" in linux-x64|linux-arm64) ;; *) echo 'Use linux-x64 or linux-arm64.' >&2; exit 2;; esac
NASHI_DOTNET=${DOTNET:-dotnet}
NASHI_OUTPUT="$NASHI_ROOT/build/linux/$NASHI_RID/Nashi"
"$NASHI_DOTNET" publish linux/Nashi.Linux -c Release -r "$NASHI_RID" --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "$NASHI_OUTPUT"
python3 scripts/collect-linux-licenses.py linux/Nashi.Linux/obj/project.assets.json "$NASHI_OUTPUT"
install -m 644 Resources/Brand/NashiIcon.png "$NASHI_OUTPUT/nashi.png"
install -m 644 linux/packaging/nashi.desktop "$NASHI_OUTPUT/nashi.desktop"
install -m 644 linux/FEATURE-PARITY.md "$NASHI_OUTPUT/FEATURE-PARITY.md"
install -m 644 linux/README.md "$NASHI_OUTPUT/README.md"
install -m 644 linux/THIRD-PARTY-NOTICES.md "$NASHI_OUTPUT/THIRD-PARTY-NOTICES.md"
install -m 755 linux/packaging/install.sh "$NASHI_OUTPUT/install.sh"
printf 'Linux application: %s\n' "$NASHI_OUTPUT/Nashi"
