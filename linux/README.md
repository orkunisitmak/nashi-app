# Nashi for Linux (preview)

A Linux desktop front end using Avalonia 12 / .NET 10 and the existing
`windows/Nashi.Core` engine. The shared core stays at its existing path so this
port does not require a disruptive Windows source-tree move. macOS and WPF UI
code are not compiled into the Linux app.

## Build and run

Requires the .NET 10 SDK, glibc Linux, fontconfig and X11 libraries. Wayland
sessions need XWayland; this preview uses Avalonia's X11 backend. Tested locally
on CachyOS x86_64; CI builds and tests Ubuntu 24.04 x86_64. ARM64 is a build target,
not a claim of a completed native ARM64 desktop test. Alpine/musl is not supported.

```sh
dotnet restore linux/Nashi.Linux --locked-mode
dotnet run --project linux/Nashi.Linux -c Release
# Optional file arguments (absolute or relative local paths):
dotnet run --project linux/Nashi.Linux -c Release -- /path/to/photo.png
# Self-contained portable archive; users do not need .NET:
linux/build.sh linux-x64
# Or cross-publish: linux/build.sh linux-arm64
```

The archive is `build/Nashi-linux-x64.tar.gz`, with an adjacent SHA-256 file.
Extract the whole folder and run `./Nashi`. Keep its libraries together.
Optional: run `./install.sh` **inside the extracted folder** to register an app
menu entry for the current user. Keep that folder in place. To unregister,
remove `$XDG_DATA_HOME/applications/io.github.orkunisitmak.Nashi.desktop`
(default: `~/.local/share/applications/…`). No administrator install is needed.
CI archives are preview artifacts, not official signed releases.

## Using the app

Choose files or drop local files/folders into Home. Use the up/down buttons to
set input order. Choose an action and output format, change the relevant fields,
choose an output folder, then Export. Originals are preserved and existing output
names are never overwritten. Cancel or Escape terminates a running process and
removes transaction-owned partial outputs. Closing during an export cancels it
before exiting. In-app image preview does not launch arbitrary file handlers.

Ctrl+O chooses files; Ctrl+1 / Ctrl+2 switches Home / Library. Library keeps the
last 50 successful exports, supports search and can reuse available files.
Appearance supports System, Light and Dark. Data and settings live in
`$XDG_DATA_HOME/nashi` (default `~/.local/share/nashi`), outside the app folder.
A second instance using the same data directory exits to protect history/settings;
add files to the existing window. Relative XDG and PATH entries are ignored.

### Optional distribution tools

Images, PDF page operations, archives and subtitles work immediately. Tools are
resolved from absolute entries in PATH and run with argument lists, without a
shell. Nashi does not download or attempt to run Windows `.exe` tools on Linux.
Use **Settings & tools → Refresh tools** after installation.

- **FFmpeg + ffprobe**: audio/video conversions and tools.
- **yt-dlp + Deno + FFmpeg**: public media downloads. Sites may restrict downloads;
  use current versions from trusted distribution/upstream packages. Direct web
  images and YouTube thumbnails do not require these executables.
- **LibreOffice**: supported Office/text/spreadsheet/slide conversions. Each job
  uses a private temporary profile with macros and automatic link updates disabled,
  never the user's running LibreOffice profile.

Ubuntu/Debian: `sudo apt install ffmpeg libreoffice` (plus yt-dlp/Deno if wanted).
Arch/CachyOS: `sudo pacman -S ffmpeg libreoffice-fresh yt-dlp deno`.
Package availability and yt-dlp freshness vary by distribution.

## Included features and differences

Only implemented operations are offered. This is an initial usable port, not
full macOS/Windows feature parity. The shared catalog still defines conversion
intersections; Linux filters them to its implemented engines.

| Area | Linux preview |
| --- | --- |
| Home / Library | Local file/folder selection and drop, ordered inputs, image preview, last 50 exports, search/reuse, light/dark appearance |
| Images | PNG/JPG/WebP/AVIF/TIFF/BMP/GIF output and SVG/HEIC import through the bounded shared decoder; resize, numeric crop, rotate, horizontal flip, exposure/brightness/contrast/saturation, metadata removal, multi-image PDF |
| Media | Shared catalog audio/video conversions; compression, metadata removal, normalization, mute, trim, speed, join, snapshots, interval split, waveform video and mono/stereo; progress and process cancellation |
| PDF | Merge, grouped/ranged split, page reorder/duplicate/delete by page numbers; optional password |
| Archives | ZIP creation from files/folders; ZIP/TAR/GZIP/RAR extraction; ZIP/TAR/GZIP conversion; shared traversal/link/size limits |
| Subtitles | SRT/VTT/TXT export with Unicode and timestamps |
| Documents | Catalog-supported DOC/DOCX/RTF/text/Markdown, spreadsheet and slide conversion through system LibreOffice; availability also depends on installed LibreOffice filters |
| Web | Public video/audio/thumbnail/photo/direct-image downloads through the shared engine; no browser cookies; network/site availability is not guaranteed |
| Desktop integration | App menu registration, native file dialogs, in-app file drop; X11 or XWayland |

Not yet included: global drag wheels or shortcuts, system window tiling, tray,
autostart, clipboard import, Float shelves, audio/video preview, graphical crop/
annotation/redaction/frame/collage editors, QR reading, HEIC encoding, advanced
Office/Pages/EPUB workflows, metadata field editing, and signed automatic updates.
Some image codecs depend on the native Magick.NET build; unsupported inputs fail
without publishing partial outputs. This preview uses English UI text.

## Verification

```sh
# Real file/codec/PDF/archive/process regressions; FFmpeg must be on PATH:
dotnet run --project linux/Nashi.Linux.Verify -c Release
# Native X11 window + actual resize from UI fields, light/dark render captures:
mkdir -p build/linux-smoke
XDG_DATA_HOME="$PWD/build/linux-smoke/data" \
NASHI_SMOKE_DIRECTORY="$PWD/build/linux-smoke" \
  xvfb-run -a dotnet run --project linux/Nashi.Linux -c Release -- --smoke-test
```

`--smoke-test` requires `NASHI_SMOKE_DIRECTORY`, uses only its own synthetic
image, and closes its own window. `smoke.json` appears only after export checks
and both renders succeed. Existing evidence should be removed before rerunning.
This native test drives controls programmatically; it does not establish a
physical pointer/drag interaction test, portal permissions or full Wayland parity.
The engine suite covers dimensions/pixels, codecs, PDF pages, Unicode, archive
traversal/links, cancelled child processes, case-sensitive paths, collision-safe
names, unchanged originals and failed-batch rollback. CI runs both suites.
