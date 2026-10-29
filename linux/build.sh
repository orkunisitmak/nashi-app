#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
rid="${1:-linux-x64}"
case "$rid" in linux-x64|linux-arm64) ;; *) echo 'Usage: linux/build.sh [linux-x64|linux-arm64]' >&2; exit 2;; esac
publish="$root/build/linux/$rid/Nashi"
dotnet restore "$root/linux/Nashi.Linux" --locked-mode -p:NashiRuntimeIdentifier="$rid" -p:NashiPortable=true
dotnet publish "$root/linux/Nashi.Linux" -c Release -p:NashiRuntimeIdentifier="$rid" -p:NashiPortable=true --no-restore -o "$publish"
cp "$root/LICENSE" "$publish/LICENSE.txt"
cp "$root/linux/THIRD-PARTY-NOTICES.md" "$root/linux/README.md" "$publish/"
cp "$root/linux/packaging/install.sh" "$publish/"
cp "$root/Resources/Brand/NashiIcon.png" "$publish/nashi.png"
cp "$root/windows/licenses/dotnet-MIT.txt" "$root/windows/licenses/dotnet-THIRD-PARTY-NOTICES.txt" "$publish/"
mkdir -p "$publish/licenses"
cp "$root"/linux/licenses/* "$publish/licenses/"
for name in BouncyCastle-MIT PDFsharp-MIT SharpCompress-MIT ZXing-Apache-2.0 PdfPig-Apache-2.0 Magick.NET-THIRD-PARTY-NOTICES; do
  cp "$root/windows/licenses/$name.txt" "$publish/licenses/"
done
chmod +x "$publish/Nashi" "$publish/install.sh"
tar -C "$(dirname "$publish")" -czf "$root/build/Nashi-$rid.tar.gz" Nashi
(cd "$root/build" && sha256sum "Nashi-$rid.tar.gz") > "$root/build/Nashi-$rid.tar.gz.sha256"
echo "Built $root/build/Nashi-$rid.tar.gz"
