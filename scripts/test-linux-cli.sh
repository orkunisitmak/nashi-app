#!/usr/bin/env bash
# Exercises every Linux engine path through the real `nashi` binary with real
# FFmpeg/PDFium/Magick.NET output. Usage: scripts/test-linux-cli.sh PATH/TO/nashi [WORK_DIR]
# Office conversions run only when LibreOffice is installed (soffice on PATH).
set -uo pipefail
nashi=$(realpath "$1")
work=${2:-$(mktemp -d)}
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
fixtures=$work/fixtures out=$work/out
mkdir -p "$fixtures" "$out"
export XDG_DATA_HOME=$work/data
pass=0 fail=0

run() {
  local name=$1; shift
  local result
  if result=$("$nashi" "$@" --out "$out" 2>"$work/error.txt"); then
    local file; file=$(tail -1 <<<"$result")
    if [[ -e $file ]]; then pass=$((pass+1)); printf 'PASS %-22s %s\n' "$name" "$(basename "$file")"; return; fi
    printf 'FAIL %-22s no output: %s\n' "$name" "$result"
  else
    printf 'FAIL %-22s %s\n' "$name" "$(tail -c 600 "$work/error.txt")"
  fi
  fail=$((fail+1))
}
expect_failure() {
  local name=$1 message=$2; shift 2
  if "$nashi" "$@" --out "$out" >/dev/null 2>"$work/error.txt"; then printf 'FAIL %-22s unexpectedly succeeded\n' "$name"; fail=$((fail+1)); return; fi
  if grep -q -- "$message" "$work/error.txt"; then pass=$((pass+1)); printf 'PASS %-22s %s\n' "$name" "$(head -c 120 "$work/error.txt")"; else printf 'FAIL %-22s %s\n' "$name" "$(cat "$work/error.txt")"; fail=$((fail+1)); fi
}

photo="$fixtures/Fotoğraf Ş.jpg"
ffmpeg -v error -f lavfi -i testsrc2=size=640x480 -frames:v 1 -y "$photo"
ffmpeg -v error -f lavfi -i testsrc2=size=480x640 -frames:v 1 -y "$fixtures/second.png"
ffmpeg -v error -f lavfi -i testsrc=size=640x360:rate=30 -f lavfi -i sine=frequency=440 -t 4 -c:v libx264 -pix_fmt yuv420p -c:a aac -y "$fixtures/clip.mp4"
ffmpeg -v error -f lavfi -i sine=frequency=330 -t 3 -y "$fixtures/tone.wav"
cp "$repo/Reference/document-fixtures/rich.pdf" "$fixtures/"
printf 'Merhaba dünya\nŞğüçöı İ\n' >"$fixtures/note.txt"
printf '# Nashi\n\nLinux *works*.\n' >"$fixtures/readme.md"
printf '1\n00:00:01,000 --> 00:00:02,500\nMerhaba\n' >"$fixtures/sub.srt"

"$nashi" --version
"$nashi" doctor

for format in png webp heic tiff avif bmp pdf; do run "image → $format" convert "$format" "$photo"; done
run resize tool resize "$photo" --width 320
run rotate tool rotate "$photo"
run flip tool flip "$photo" --horizontal false
run crop tool cropImage "$photo" --region 10,10,200,100
run redact tool redactImage "$photo" --region 10,10,200,100 --style pixelate
run annotate tool annotateImage "$photo" --region 40,40,200,120 --text "Merhaba Linux"
run edit tool editImage "$photo"
run frame tool frameImage "$photo"
run compress-image tool compress "$photo" --reduce 50
run metadata-image tool removeMetadata "$photo"
run collage tool createCollage "$photo" "$fixtures/second.png"
run create-pdf tool createPDF "$photo" "$fixtures/second.png"
run zip tool createZip "$photo" "$fixtures/second.png"
for format in mov mkv webm gif mp3; do run "video → $format" convert "$format" "$fixtures/clip.mp4"; done
run trim-video tool trimVideo "$fixtures/clip.mp4" --start 1 --end 2.5
run mute tool muteVideo "$fixtures/clip.mp4"
run crop-video tool cropVideo "$fixtures/clip.mp4" --region 0,0,320,180
run redact-video tool redactVideo "$fixtures/clip.mp4" --region 10,10,100,100 --style blur
run speed tool changeVideoSpeed "$fixtures/clip.mp4" --speed 2
run snapshots tool videoSnapshots "$fixtures/clip.mp4" --times 0.5,1.5
run split-video tool splitVideo "$fixtures/clip.mp4" --interval 2
run compress-video tool compress "$fixtures/clip.mp4" --reduce 40
for format in mp3 flac ogg opus m4a; do run "audio → $format" convert "$format" "$fixtures/tone.wav"; done
run normalize tool normalizeAudio "$fixtures/tone.wav"
run trim-audio tool trimAudio "$fixtures/tone.wav" --start 0.5 --end 2
run channels tool audioChannels "$fixtures/tone.wav" --channels 1
run visualizer tool audioToVideo "$fixtures/tone.wav"
run "pdf → png" convert png "$fixtures/rich.pdf"
run "pdf → txt" convert txt "$fixtures/rich.pdf"
run "pdf → epub (fixed)" convert epub "$fixtures/rich.pdf" --epub-layout fixed
run split-pdf tool splitPDF "$fixtures/rich.pdf"
run organize-pdf tool organizePDF "$fixtures/rich.pdf" --pages 2,1
run compress-pdf tool compress "$fixtures/rich.pdf"
run metadata-pdf tool removeMetadata "$fixtures/rich.pdf"
run "srt → vtt" convert vtt "$fixtures/sub.srt"
archive=$(ls -t "$out"/*.zip | head -1)
run "zip → tar" convert tar "$archive"
run extract tool extractArchive "$archive"

if command -v soffice >/dev/null || command -v libreoffice >/dev/null; then
  run "txt → docx" convert docx "$fixtures/note.txt"
  run "md → pdf" convert pdf "$fixtures/readme.md"
  run "docx → pdf" convert pdf "$(ls -t "$out"/*.docx | head -1)"
  run "pdf → docx" convert docx "$fixtures/rich.pdf"
  run "txt → png" convert png "$fixtures/note.txt"
else
  expect_failure "office missing" "Install LibreOffice" convert docx "$fixtures/note.txt"
fi

qr=$("$nashi" qr "$photo" 2>/dev/null); [[ -z $qr ]] && { pass=$((pass+1)); echo "PASS qr (no code, as expected)"; } || { fail=$((fail+1)); echo "FAIL qr found: $qr"; }
if command -v qrencode >/dev/null; then
  # A 1-bit palette PNG also checks that PDF export widens it for PDFsharp.
  qrencode -s 8 -o "$fixtures/code.png" "https://github.com/orkunisitmak/nashi-app"
  run "qr png → pdf" convert pdf "$fixtures/code.png"
  for source in "$fixtures/code.png" "$out/code.pdf"; do
    [[ $("$nashi" qr "$source" 2>/dev/null) == "https://github.com/orkunisitmak/nashi-app" ]] && { pass=$((pass+1)); echo "PASS qr $(basename "$source")"; } || { fail=$((fail+1)); echo "FAIL qr $(basename "$source")"; }
  done
fi
for page in home library wheel tools settings; do
  if "$nashi" --render "$out/ui-$page.png" --page "$page" --lang tr "$photo" >/dev/null 2>"$work/error.txt" && [[ -s $out/ui-$page.png ]]; then pass=$((pass+1)); echo "PASS render $page"; else fail=$((fail+1)); echo "FAIL render $page $(cat "$work/error.txt")"; fi
done
# The window's Run button path: tool panel → Operations → Library.
mkdir -p "$work/gui" && cp "$photo" "$work/gui/"
if "$nashi" --render "$out/ui-run.png" --tool convert:webp --run "$work/gui/$(basename "$photo")" 2>"$work/error.txt" | grep -q "^OUTPUT .*\.webp$" && ls "$work/gui/"*.webp >/dev/null 2>&1; then pass=$((pass+1)); echo "PASS window run → webp"; else fail=$((fail+1)); echo "FAIL window run $(cat "$work/error.txt")"; fi
[[ $("$nashi" --render "$out/ui-key.png" --press-ctrl-space "$photo" 2>/dev/null | grep '^WHEEL') == "WHEEL True" ]] && { pass=$((pass+1)); echo "PASS Ctrl+Space opens the wheel"; } || { fail=$((fail+1)); echo "FAIL Ctrl+Space did not open the wheel"; }
# Real mouse clicks: a wheel tool, then Run, then wheel scrolling on Home.
mouse=$("$nashi" --render "$out/ui-mouse.png" --click-wheel "Create PDF" "$photo" "$fixtures/second.png" 2>/dev/null | grep -E '^(WHEELRUN|SCROLL)' | tr '\n' ' ')
[[ $mouse == "WHEELRUN outputs=1 SCROLL moved " ]] && { pass=$((pass+1)); echo "PASS mouse: wheel → Run → scroll"; } || { fail=$((fail+1)); echo "FAIL mouse: $mouse"; }
# Drop chords pick the wheel, as Shift/Ctrl/Alt do on macOS and Windows.
expect_wheel(){ local got; got=$("$nashi" --render "$out/ui-drop.png" "$@" 2>/dev/null | grep -E '^(DROP|LINK)'); [[ $got == *"$WANT"* ]] && { pass=$((pass+1)); echo "PASS $NAME"; } || { fail=$((fail+1)); echo "FAIL $NAME: $got"; }; }
NAME="Shift drop → convert wheel" WANT="DROP convert: PNG" expect_wheel --drop-chord Shift "$photo"
NAME="Ctrl drop of images → one PDF first" WANT="DROP tools: Create PDF" expect_wheel --drop-chord Ctrl "$photo" "$fixtures/second.png"
NAME="Alt drop → file actions" WANT="DROP actions: Open folder" expect_wheel --drop-chord Alt "$photo"
NAME="YouTube link → download wheel" WANT="LINK Download Video | Download Audio | Download Thumbnail" expect_wheel --link "https://youtu.be/dQw4w9WgXcQ"
[[ -e $photo && -e $fixtures/clip.mp4 ]] && { pass=$((pass+1)); echo "PASS originals kept"; } || { fail=$((fail+1)); echo "FAIL an original was removed"; }

echo "PASSED=$pass FAILED=$fail"
[[ $fail -eq 0 ]]
