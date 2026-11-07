# Nashi for Linux (preview)

A native Linux desktop app built with [Avalonia](https://avaloniaui.net/) on top of the shared `windows/Nashi.Core` engine. The same `Resources/FeatureCatalog.json` decides which formats and tools apply to a selection, so the choices match macOS and Windows. All 27 catalog tools are available.

## Build and run

Requires the .NET 10 SDK (`dotnet-sdk-10.0` on Arch/CachyOS and Ubuntu 24.04+).

```sh
dotnet build linux/Nashi.Linux -c Release
linux/Nashi.Linux/bin/Release/net10.0/Nashi [files…]
```

Self-contained build: `dotnet publish linux/Nashi.Linux -c Release -r linux-x64 --self-contained`.

### Optional system tools

Settings → Tools shows which of these were found. Nashi works without them; the matching actions are hidden or explain what to install.

| Tool | Used for | How to get it |
| --- | --- | --- |
| FFmpeg, ffprobe | Audio and video, HEIC export | Package manager, or Settings → *Set up media tools* (verified official downloads) |
| yt-dlp, Deno | Web downloads, Pages documents | Package manager, or *Set up media tools* |
| LibreOffice (`soffice`) | Documents, spreadsheets, slides | Package manager (`libreoffice-fresh`, `libreoffice`) |
| ExifTool | Editing metadata fields of images and PDFs | Offered as a verified download the first time (needs `perl`) |
| Tesseract | Text in scanned PDFs, *Find text* in redaction | Package manager, plus a language pack (`tesseract-data-tur`, `tesseract-ocr-tur`…) |

PDF pages are rendered with the same PDFium build as Windows (bundled NuGet package), so PDF thumbnails, PDF → image and QR codes in PDFs need nothing extra.

## What works

| Area | Linux |
| --- | --- |
| Home | Drop files, **Ctrl+O** to choose, **Ctrl+V** to paste files, images, text (saved to `Imports`) or a web link (opens the Web tab); file actions Copy, Move to…, Copy path, Float and Open terminal here |
| Images | All shared conversions; crop, edit photo, add background, collage, redact (black box, blur, pixelate, **Find text** with Tesseract), **annotate** (marker, rectangle, arrow, line, ellipse, text, numbers, callout, magnifier, placed image, freehand, select/move, undo), compress (presets, quality, target size or reduction, image size), metadata, QR codes |
| PDF | Merge, split, **organize** (thumbnails, drag to reorder, rotate, duplicate, remove, uniform width), compress (lossless for text PDFs, re-rendered to a target for scans), metadata, PDF → DOCX/Pages/PPTX/XLSX/EPUB/JPG/PNG/TXT/MD, QR codes on pages, OCR for scanned pages |
| Documents | DOCX, Pages, Markdown, RTF/RTFD, text clippings, HTML, EPUB, TXT, XLSX/XLS/ODS/CSV/TSV, PPT/PPTX through the Windows conversion code; when a LibreOffice build cannot export EPUB, Nashi packages an EPUB 3 itself |
| Audio / video | All shared FFmpeg conversions and tools: compress, metadata (edit tags), mute, normalize, trim, crop, redact regions (optionally for a time range), bleep/silence ranges, split, snapshots, visualizer, speed, join, channels |
| Subtitles / archives | SRT ↔ VTT ↔ TXT; extract ZIP/TAR/GZIP/RAR, convert between ZIP, TAR and GZIP |
| Web | YouTube video/audio/thumbnail, Instagram photo selection and Reels, direct images, quality limit, into the download folder |
| Settings | Appearance, English/Türkçe, output next to the original or in one folder, download folder, tool status and setup, per-format defaults |

Outputs go through the shared export transaction: a cancelled or failed run publishes nothing and originals are never overwritten. The Library and settings live in `$XDG_DATA_HOME/Nashi` (usually `~/.local/share/Nashi`).

## Not on Linux

- **Drag wheels with a modifier key** anywhere on the desktop: Wayland gives apps no global pointer or drag hooks. Use Nashi's window, the clipboard or Float instead.
- **Tray and automatic updates**: there is no Linux release channel yet; GNOME also shows no tray without an extension.
- **Share and face detection for redaction**: no common Linux service; *Find text* covers text.

## Checks

```sh
linux/Nashi.Linux/bin/Release/net10.0/Nashi --verify-operations "$(mktemp -d)/run"
linux/Nashi.Linux/bin/Release/net10.0/Nashi --verify-editors "$(mktemp -d)/run"
```

`--verify-operations` runs real exports on generated fixtures and reads every result back (pixels, page counts, durations, document text, metadata, hashes); document, OCR and ExifTool checks run when those tools are present. Set `NASHI_VERIFY_WEB=1` to add real downloads (a pinned image checked by SHA-256 and a public YouTube video). `--verify-editors` drives the annotate, Organize PDF, metadata, redaction, Web and Float windows with real pointer and keyboard events on Avalonia's headless platform. `--capture file.png [--light|--dark] [--tab web|settings|library] [--lang tr] [--dialog annotateImage|organizePDF]` renders a window to a PNG and exits, for checks on a locked or headless session.
