# Linux reference guide

[Linux overview](README.md) · [Türkçe](README.tr.md) · [Project](../README.md)

This is **the first Linux edition of Nashi (v1)**. Detailed testing is still in progress: compatibility across distributions, desktop sessions, codecs and real-world files is not yet fully validated. Treat it as a preview. The shared application version is currently **0.7.14**; “Linux v1” describes this first Linux edition, not a change to the shared version number.

Nashi converts files and opens editing tools from a two-sided radial menu. The Linux app uses .NET 10 and Avalonia, sharing its conversion engine with Windows. The x86_64 AppImage includes the .NET runtime; you do not need to install .NET to run it. Development and native drag testing have been performed on Ubuntu 22.04.5 with X11. Other environments still need wider testing.

## Download and launch

Get `Nashi-Linux-x86_64.AppImage` and its matching `.sha256` from a [release](https://github.com/orkunisitmak/nashi-app/releases) that includes Linux assets, or the **Linux preview** artifact produced by the [Linux workflow](../.github/workflows/linux.yml). A Linux release asset may not yet be published. These preview builds are unsigned.

Run these commands in the download directory:

```sh
sha256sum -c Nashi-Linux-x86_64.AppImage.sha256
chmod +x Nashi-Linux-x86_64.AppImage
./Nashi-Linux-x86_64.AppImage
```

If mounting the AppImage fails because FUSE is unavailable:

```sh
./Nashi-Linux-x86_64.AppImage --appimage-extract-and-run
```

Keep the AppImage in a permanent location before installing desktop integration. `--version` prints the application version.

## Required and optional components

Requirements depend on the operation. Image and PDF libraries are bundled; Office and media operations need additional tools. Use the pinned FFmpeg 9 tool setup for full media support; older distribution builds can lack required filters (for example, Ubuntu 22.04’s FFmpeg 4.4 cannot export frames with the current engine).

| Component | Needed for | How to obtain it |
| --- | --- | --- |
| FUSE 2 | Normal AppImage mounting | On Ubuntu 22.04: `sudo apt install libfuse2`. Alternatively use extraction mode above. Package names can differ on other distributions. |
| FFmpeg and ffprobe | Audio/video conversion, editing, playback and frame export | **Set up tools → Download missing tools**, or install system `ffmpeg`. |
| LibreOffice | Office/document conversions, including routes involving PDF, EPUB and Pages | Install a native system package providing `soffice` or `libreoffice` on `PATH`. Nashi does not download LibreOffice. |
| ExifTool and Perl | Image/PDF metadata editing | Setup downloads and verifies ExifTool 13.59. Install the system `perl` interpreter separately. |
| Deno | Pages document conversions | Available through **Download missing tools**. |
| Tesseract + language packs | Image and scanned-PDF text recognition | Install system `tesseract-ocr`, `tesseract-ocr-eng`, `tesseract-ocr-tur`. These are not downloaded by Nashi. |
| PulseAudio-compatible audio service | Sound in media previews and wheel feedback | PulseAudio or PipeWire's PulseAudio compatibility service. Without it, media picture playback can be silent. |
| Desktop utilities | Opening results, links and terminals | A desktop with `xdg-open`; a supported terminal for the Terminal action. |

For Ubuntu/Debian, install the optional system tools needed for your work:

```sh
sudo apt update
sudo apt install ffmpeg libreoffice perl
```

The LibreOffice Snap and Flatpak builds are not used: their isolated temporary directories are incompatible with the conversion worker. Use your distribution's native package. Tool setup requires an internet connection and verifies pinned downloads. Conversions run locally after their tools are installed. Setup also provisions yt-dlp for the **Download a link** screen and the headless download command.

## The two wheel faces

Open files with **Choose files**, drag them into the app, or paste copied files with **Ctrl+V**. **Ctrl+Space** opens the wheel for the selection inside Nashi; with no selection, it opens the picker.

1. **Convert** groups compatible output formats into **Images, Documents, Video, Audio, Archives, Text & Data**. Only applicable groups appear.
2. **Tools** groups editing and file operations by purpose. Depending on the selection, these include Edit, Background, Markup, Create, Frames, Pages, Compress, Metadata, Read and Files. Read contains QR scanning and OCR.

**Tab** switches between the two faces. Hover over a group briefly to open its outer ring, then move onto an option. Short captions stay on one line; the full action name appears beneath the wheel on hover or keyboard selection. Immediate actions run when selected; operations with options open an editor with Save and Cancel.

With X11 desktop integration running, **hold Ctrl and drag a file**, hover over a group and drop on an outer-ring option. Dropping outside an actionable option does not run a Nashi operation.

## Formats and capabilities

The available targets depend on the source type, selection and installed tools. Not every input converts to every output.

| Input family | Recognized formats | Main capabilities |
| --- | --- | --- |
| Images | JPG/JPEG, PNG, WebP, HEIC/HEIF, TIFF/TIF, SVG, AVIF, BMP | Convert to PNG, JPG, WebP, HEIC, AVIF, BMP, GIF, TIFF or PDF; edit images and create documents/collages. |
| Video and GIF | MP4, MOV, MKV, WebM, AVI, WMV, GIF, M4V | Convert video; extract audio or JPG/PNG frames; edit and compress. GIF sources have no audio-extraction targets. |
| Audio | MP3, M4A, WAV, FLAC, OGG, OPUS, AIFF, WMA, AAC | Convert to MP3, WAV, FLAC, AAC, M4A, OGG, OPUS, AIFF or WMA; edit and visualize. |
| PDF | PDF | Export each page as JPG/PNG; extract existing text to TXT/Markdown; document conversions and PDF tools. |
| Documents and data | PPT, PPTX, DOCX, Pages, MD, RTF, RTFD, textClipping, HTML, EPUB, TXT, XLSX, XLS, ODS, CSV, TSV | Compatible document, spreadsheet, text and page-image conversions, generally through LibreOffice. Pages routes also need Deno. |
| Archives | ZIP, TAR, GZ, RAR | Extract archives and create supported ZIP/TAR/GZIP outputs. RAR creation is unavailable. |
| Subtitles | SRT, VTT | Subtitle/text conversions. |

HEIC is supported as input and as FFmpeg-backed output; missing HEIC encoding tools open setup. Edited HEIC files save as PNG. A mixed JPG/PNG selection can target either format; every selected file produces a fresh encoded copy. Folders can be selected, dropped or pasted for ZIP and file actions; they are not treated as image/media files based on their names.

### Image tools

- Quick rotate, flip, grayscale and plain-background removal; create one PDF or ZIP from a selection.
- **Crop** with draggable bounds, aspect presets and numeric fields; **Edit Photo** with rotation, sizing, light, colour and detail controls.
- **Background** with solid/gradient/image fills, blur, padding, rounded corners and shadows.
- **Redact** with manually drawn solid, blurred or pixelated areas.
- **Annotate** with shapes, arrows, pen, text, numbered markers, callouts, magnifiers and image stamps; save as PNG.
- **Collage** with layouts, ordering, spacing, canvas sizing and backgrounds; save one PNG beside the first source.
- Compression with quality, dimensions and optional target size; metadata browsing/editing/removal through ExifTool; QR reading from images and PDF pages.

Background removal is suited to plain backgrounds, paper, logos and signatures. It is not AI subject segmentation for people or complex scenes. Redact Photo can find text regions with local Tesseract, optionally filtered by a word. Review all regions: OCR can miss text. Automatic face detection is unavailable.

### PDF and document tools

Organize pages with selection, reordering, rotation, duplication and deletion; split by page, count or ranges; merge in a chosen order; compress and edit metadata. Create PDFs from images. PDF-to-image writes one file per page. PDF-to-text reads an existing text layer and automatically recognizes textless pages through local Tesseract. Editable document conversions also include recognized scan text. **Tools → Read → OCR** remains available for reviewing and editing a transcript before TXT export. Searchable PDF creation is not implemented.

Some PDF editors prompt for passwords, but the general conversion flow does not yet offer full password/options controls. Settings can change per-format PDF rendering DPI (144 by default), image/media quality, audio bitrate/sample depth, metadata and GIF dimensions/frame rate. Layout remains editable with reflowable EPUB; full per-task document options are not yet available. LibreOffice conversions run headless with an isolated temporary profile, a five-minute timeout and cancellation. On older LibreOffice versions without Markdown export, Markdown falls back to plain text and the result message says so. HTML conversion uses sanitized content through LibreOffice; it does not execute browser scripts.

### Audio and video tools

Video tools include trim, split, crop, speed, snapshots, timed redaction, mute, join, compression and metadata. Video outputs include MP4, MOV, MKV, WebM, AVI, WMV and GIF. Choosing JPG/PNG opens the frame editor with the first frame selected; add time points or intervals before saving.

Audio tools include trim, loudness normalization, channel conversion/swapping, bleep or silence ranges, and audio-to-video visualizations. Editors provide previews and timelines. Space plays/pauses, arrows step, Shift+arrows step frames, I/O set the selected range, and M adds split points where supported. Codec support still depends on the installed FFmpeg build and the source file.

### File operations and Float

The **Files** group offers Copy, Move, Copy Path, Terminal, Share and Float. Share reveals the files in the file manager. Float is a persistent shelf of file references with edge docking, collapsing, selection, copying, revealing and removing entries. Removing a shelf entry does not delete its source file.

Browser drag supports local files, browser image payloads and supported HTTPS downloads. A watched-folder backend is also present, configured through JSON; there is no watched-folder settings screen yet.

## OCR

Use **Tools → Read → OCR** for images or PDFs. Choose an installed OCR language (including English, Turkish or both); select automatic, block or scattered-text layout; optionally enter PDF pages and a password. Recognition keeps existing PDF text by default and recognizes scanned pages at 300 DPI. Review and edit each source’s text before copying or saving a fresh UTF-8 TXT beside it. Cancellation stops the process; closing the editor also cancels recognition.

Install Tesseract and language packs with your distribution. Tesseract 5 is recommended; compatible Tesseract 4 LSTM installations are supported. Ubuntu/Debian: `sudo apt install tesseract-ocr tesseract-ocr-eng tesseract-ocr-tur`. The editor has **Check again** after installation. [OCR limits and alternatives](OCR-OPTIONS.md) include the current TXT-only output and first-frame behavior for multi-frame images.

## Mixed selections

Convert considers each selected file separately. Common targets come first; additional targets remain available for compatible subsets, with the source-file count shown before choosing. For example, PNG + PDF can both export JPG, while WEBP applies only to the PNG. Other files are left alone and their folders do not trigger destination prompts.

An input already in the requested target format is re-encoded for images or copied to a fresh output for other supported families. Mixed engine operations use a shared export transaction; a later failure removes earlier staged outputs. This does not merge image and document content into a single PDF: use the dedicated combining tools for that workflow.

## Where new files are saved

**By default, converted and edited files are saved beside their source.** For example, converting `Downloads/report.pdf` to JPG creates page images such as `Downloads/report Page 1.jpg`. There is no extra Nashi output folder.

- Originals remain unchanged; existing output names receive a unique alternative.
- Separate inputs retain their own source directories. Combining operations save beside the first source.
- Extraction creates a folder beside the archive.
- If the source folder is not writable, Nashi asks for another destination.
- The completion card shows the actual path and offers **Show in folder**. Hover over a shortened path to read it in full.

## Desktop integration: Ctrl-drag and Open With

```sh
./Nashi-Linux-x86_64.AppImage --install-integration
```

This starts the background instance and installs login autostart, an **Open With → Nashi** entry for image/audio/video/PDF/ZIP inputs, and an icon. **It does not add Nautilus Scripts entries.** Installation removes the old English and Turkish “Convert with Nashi” / “Nashi actions” scripts, preserving other scripts.

Managed desktop files:

- `~/.config/autostart/nashi.desktop`
- `~/.local/share/applications/nashi.desktop`
- `~/.local/share/icons/hicolor/256x256/apps/nashi.png`

The old Nashi Ctrl+Alt+N shortcut is also cleaned up without removing other shortcuts. Integration refers to the AppImage's installed path: rerun installation if you move it.

**X11 is required for global Ctrl-drag and pointer-positioned wheels.** This Avalonia build requires XWayland in Wayland sessions. On Wayland, use the app or Open With; the wheel opens centrally rather than tracking the global pointer. Global Ctrl+Space is not installed as a desktop shortcut.

These launch options open desktop flows:

```sh
./Nashi-Linux-x86_64.AppImage --background
./Nashi-Linux-x86_64.AppImage --wheel /path/to/file.pdf
./Nashi-Linux-x86_64.AppImage --actions /path/to/file.pdf
./Nashi-Linux-x86_64.AppImage --uninstall-integration
```

Uninstalling integration stops the background instance and removes its managed desktop entries and legacy scripts; user files, settings and downloaded tools are retained.

Preferences and tool state live in `$XDG_DATA_HOME/Nashi` (normally `~/.local/share/Nashi`). `settings.json` supports `language` (`en`, `tr` or `system`) and `gestureKey` (`Ctrl`, `Shift`, `Alt`, combinations such as `Ctrl+Shift`, or `Off`). The Settings screen exposes output mode/folder, appearance, X11 gesture, wheel opacity/sound, OCR conversion language, format defaults and watched-folder rules. Shelf state changed while Settings is open is preserved when saving.

## Library, downloads and command line

Library keeps the last 50 completed tasks as references to their output files, with search, reuse, external Open and Show in folder. Missing files stay in history but are excluded from reuse. Failure to save history does not remove successful conversion results.

**Download a link** supports video, audio, direct images, photo posts and thumbnails through the shared web engine. Choose a destination and press Download; cancel or close the window to stop it. Site availability and restrictions vary. This feature has not been tested against every supported website.

```sh
nashi doctor
nashi list /path/photo.png /path/report.pdf
nashi convert /path/report.pdf --to jpg --dpi 200
nashi tool quick:zip /path/folder /path/report.pdf
nashi ocr /path/scan.pdf --language eng+tur --pages 1-3
nashi qr /path/code.png
nashi download 'https://example.com/photo.png' --kind image --output /path/downloads
```

Use the AppImage path instead of `nashi` until desktop integration is installed and `~/.local/bin` is on PATH. Commands do not launch Avalonia or forward work to the desktop instance. Outputs default to each source folder; `--output` chooses one destination. `--help` lists supported CLI tools; tools needing interactive options remain in the graphical editor. Ctrl+C returns exit code 130 and cancels active processing.

Build a portable archive with `DOTNET=/path/to/dotnet bash linux/scripts/build-linux-portable.sh linux-x64`. Extract it and run `./Nashi/nashi` directly. Its optional `install.sh` installs per user and keeps the previous app in a `.previous-*` backup directory; it registers desktop integration and autostart. It does not install system dependencies. The ARM64 build argument is experimental; native execution and complete tool setup still require verification.

## Preview limits and testing status

Detailed tests are ongoing. General batch preview, automatic updates, searchable PDF generation, face detection and full per-task document options remain unavailable. Jobs run sequentially. New desktop screens currently use English. The optional drop target uses ordinary file drops; native global Wayland gestures are not implemented. ARM64 has a prepared smoke workflow but no completed remote run in this development state; tool downloads support x64. x64 DEB and RPM previews can be built, but installation on Debian/Fedora systems has not been validated. ARM64 remains experimental.

As of 2026-11-02, local verification passed **4,878 Linux checks** and **2,285 shared-engine checks including real media**, with pinned FFmpeg 9, Deno 2.9.7 and ExifTool 13.59 plus system LibreOffice. All **380 offered source/target routes** ran with no route skips, including Pages; the ExifTool round trip also ran. These checks cover small fixtures, not every file variant, codec or desktop environment. The native X11 check verifies PDF → JPG drag/drop, source-adjacent output and an unchanged source. An initial hover timeout was followed by four passing reruns (three consecutive); its intermittent cause is not yet established.

When reporting a problem, include the app version, distribution, X11/Wayland session, source/target format, tool versions and exact error. A small non-sensitive sample is useful.

## Build and verify

Building requires the **.NET 10 SDK**, `curl` and `sha256sum`:

```sh
linux/scripts/build-linux-appimage.sh
```

The script publishes a self-contained app, bundles PDFium, downloads pinned and SHA-256-verified packaging tools, and writes `build/linux/Nashi-Linux-x86_64.AppImage` with its checksum.

```sh
dotnet run --project windows/Nashi.Verify -c Release
dotnet run --project linux/Nashi.Linux.Checks -c Release
dotnet run --project linux/Nashi.Linux.Checks -c Release -- --integration
```

Conversion checks report missing-tool skips. Set `NASHI_VERIFY_FFMPEG`, `NASHI_VERIFY_FFPROBE` and `NASHI_VERIFY_DENO` to the relevant executable paths and install LibreOffice for broader route coverage.

The native XDND smoke test uses Python 3's standard library and an X11 display. It serves a temporary PDF, hovers the Images group, drops on JPG and checks the result without injecting global pointer/keyboard input. For an isolated display, install Xvfb and xauth:

```sh
python3 linux/scripts/test-linux-native-drop.py
xvfb-run --auto-servernum python3 linux/scripts/test-linux-native-drop.py
```

### Distribution packages

```sh
DOTNET=/path/to/dotnet bash linux/scripts/build-linux-portable.sh linux-x64
DOTNET=/path/to/dotnet bash linux/scripts/build-linux-package.sh deb linux-x64
DOTNET=/path/to/dotnet bash linux/scripts/build-linux-package.sh rpm linux-x64
```

DEB building needs `dpkg-deb`; RPM building needs `rpmbuild`. These scripts build packages and checksums under `build/linux`; they do not install them. Local x64 DEB payload extraction/version/help and RPM digest/payload checks passed. Installation and desktop integration through actual Debian/Fedora package managers remain untested. No automatic autostart is installed by these package payloads; desktop integration is an explicit user action.

`linux-arm64` is accepted by the portable/package builders. An experimental ARM64 archive has been cross-built; its ELF architecture and PDFium binary/license provenance were verified. Execution on ARM64 hardware, full media support and the newly prepared hosted ARM64 smoke workflow remain unverified. Tool downloads still support x64 only.
