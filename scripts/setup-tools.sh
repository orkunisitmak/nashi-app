#!/bin/zsh
set -euo pipefail
cd "${0:A:h:h}"
mkdir -p Tools Downloads
staging=$(mktemp -d)
trap 'rm -rf -- "$staging"' EXIT
for component in ffmpeg ffprobe; do
  if [[ "$component" == ffmpeg ]]; then
    expected=591260c945d0eef150e3bf82b0ef988bd36a9cecc18ff05d6679617159f0a95e
    expected_archive=d0c06c5c68ce48af3143b262f7a9118a7c9f67de1e237fcc24ffb14df9c67af9
  else
    expected=e11c17e8200b3ee4c4c186d245e2b4053f01d56957336c1817fca0b997469106
    expected_archive=0c94fbdd8917022f28115eca512196cf4648732bc9e5db9ec8896c7e519d02aa
  fi
  if [[ ! -x "Tools/$component" ]]; then
    curl --fail --location --proto '=https' --proto-redir '=https' "https://www.osxexperts.net/${component}9arm.zip" --output "Downloads/$component.zip"
    actual_archive=$(shasum -a 256 "Downloads/$component.zip" | awk '{print $1}')
    if [[ "$actual_archive" != "$expected_archive" ]]; then print -u2 "Archive checksum mismatch for $component; refusing to unpack it."; exit 1; fi
    # Unpack outside Tools/ and install only the named binary once its checksum matches.
    ditto -xk "Downloads/$component.zip" "$staging/$component"
    actual=$(shasum -a 256 "$staging/$component/$component" | awk '{print $1}')
    if [[ "$actual" != "$expected" ]]; then print -u2 "Checksum mismatch for $component; refusing to run it."; exit 1; fi
    mv -f "$staging/$component/$component" "Tools/$component"
  fi
  actual=$(shasum -a 256 "Tools/$component" | awk '{print $1}')
  if [[ "$actual" != "$expected" ]]; then print -u2 "Checksum mismatch for $component; refusing to run it."; exit 1; fi
done
if [[ ! -d Tools/LibreOffice.app ]]; then
  task_lo_source=${NASHI_LIBREOFFICE_SOURCE:-/Applications/LibreOffice.app}
  if [[ ! -d "$task_lo_source" ]]; then print -u2 'Set NASHI_LIBREOFFICE_SOURCE to a local LibreOffice.app bundle, then rerun.'; exit 1; fi
  ditto "$task_lo_source" Tools/LibreOffice.app
fi
Tools/ffmpeg -version | head -1
Tools/LibreOffice.app/Contents/MacOS/soffice --version
