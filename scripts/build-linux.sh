#!/bin/sh
# Builds the self-contained Linux x64 app and its release archive:
#   build/linux/Nashi/                 the app folder (run ./nashi, or ./install.sh)
#   build/linux/Nashi-Linux-x64.tar.gz the archive, with a .sha256 beside it
# Needs the .NET 10 SDK. Users of the archive do not need .NET.
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
out=$root/build/linux
rm -rf "$out/Nashi"
dotnet restore "$root/linux/Nashi.Linux" --locked-mode
dotnet publish "$root/linux/Nashi.Linux" -c Release --no-restore -o "$out/Nashi"
chmod +x "$out/Nashi/nashi" "$out/Nashi/install.sh" "$out/Nashi/uninstall.sh"
"$out/Nashi/nashi" --version
archive=$out/Nashi-Linux-x64.tar.gz
tar -C "$out" --owner=0 --group=0 --sort=name -czf "$archive" Nashi
(cd "$out" && sha256sum "$(basename "$archive")" > "$(basename "$archive").sha256")
echo "Built $archive"
cat "$archive.sha256"
