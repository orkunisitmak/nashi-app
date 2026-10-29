# Nashi for Linux (preview)

A native Linux desktop app built with [Avalonia](https://avaloniaui.net/) on top of the shared `windows/Nashi.Core` engine. The same `Resources/FeatureCatalog.json` decides which formats and tools apply to a selection, so the choices match macOS and Windows; the preview offers only the workflows listed below.

## Build and run

Requires the .NET 10 SDK (`dotnet-sdk-10.0` on Arch/CachyOS, `dotnet-sdk-10.0` on Ubuntu 24.04+). Media tools use FFmpeg from your package manager.

```sh
dotnet build linux/Nashi.Linux -c Release
linux/Nashi.Linux/bin/Release/net10.0/Nashi [files…]
```

Self-contained build: `dotnet publish linux/Nashi.Linux -c Release -r linux-x64 --self-contained`.

## What works

| Area | Linux preview |
| --- | --- |
| Home | Drop files, **Ctrl+O** to choose, **Ctrl+V** to paste files (also `file://` text from Nautilus/Dolphin), remove/clear the selection |
| Images | Convert between JPG, PNG, WebP, HEIC, TIFF, AVIF and BMP (SVG input), image → PDF; crop (drag, move, corner resize, aspect presets, Shift aspect lock, arrow nudge, Enter; one selection applies relative to each selected image); edit photo (22 Light/Color/Detail controls from the shared engine, rotate, flip, live preview, show original); add background (solid, gradient or blurred image, padding, rounded corners, shadow, position, canvas size) and collage (grid/row/column/featured, spacing, order) with live previews; redact (black box, blur or pixelate over any number of drawn boxes, live preview; the EXIF thumbnail is removed so no small unredacted copy survives); compress; remove metadata (color profile kept); read QR codes |
| PDF | Merge, split into pages, extract text (TXT) |
| Audio / video | All shared FFmpeg conversions, compress, remove audio, normalize volume, trim video and audio (In/Out with frame previews, I/O and arrow keys), crop video and redact video regions (boxes drawn on a frame, applied to the whole clip), split into equal parts, snapshots, audio visualizer, change video speed (0.5×–2×), join videos in selection order, mono/stereo (needs `ffmpeg`/`ffprobe` on `PATH`) |
| Archives | Extract ZIP/TAR/GZIP/RAR, convert between ZIP, TAR and GZIP |
| Library / settings | Searchable history of finished files, System/Light/Dark appearance, stored in `$XDG_DATA_HOME/Nashi` |

Outputs are written beside the first input through the shared export transaction: a cancelled or failed run publishes nothing and originals are never overwritten. HEIC is decoded by the bundled ImageMagick and encoded with FFmpeg, as on Windows.

## Not yet on Linux

The global drag-with-modifier wheel, the annotate editor, time ranges for video redaction, audio bleeps, PDF page organizer and rendering, Office documents, metadata editing, web downloads, Float shelf, tray and automatic updates. These are listed in the [desktop comparison](../docs/DESKTOP-PARITY.md) for the other platforms.

## Checks

```sh
linux/Nashi.Linux/bin/Release/net10.0/Nashi --verify-operations "$(mktemp -d)/run"
```

Runs real exports on generated fixtures: every image output format with dimension read-back, image → PDF, PDF merge/split page counts, QR decode, archive extraction, photo redaction styles, audio conversion and normalization, video crop and redaction when FFmpeg is present, no-overwrite naming and an unchanged-original hash. `--capture file.png [--light|--dark]` renders the window to a PNG and exits, for checks on a locked or headless session.
