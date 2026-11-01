# Linux reference guide

[Linux overview](README.md) · [Türkçe](README.tr.md) · [Project](../README.md)

This is **the first Linux edition of Nashi (v1)**. Detailed testing is still in progress: compatibility across distributions, desktop sessions, codecs and real-world files is not yet fully validated. Treat it as a preview. The shared application version is currently **0.7.12**; “Linux v1” describes this first Linux edition, not a change to the shared version number.

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

Requirements depend on the operation. Image and PDF libraries are bundled; Office and media operations need additional tools.

| Component | Needed for | How to obtain it |
| --- | --- | --- |
| FUSE 2 | Normal AppImage mounting | On Ubuntu 22.04: `sudo apt install libfuse2`. Alternatively use extraction mode above. Package names can differ on other distributions. |
| FFmpeg and ffprobe | Audio/video conversion, editing, playback and frame export | **Set up tools → Download missing tools**, or install system `ffmpeg`. |
| LibreOffice | Office/document conversions, including routes involving PDF, EPUB and Pages | Install a native system package providing `soffice` or `libreoffice` on `PATH`. Nashi does not download LibreOffice. |
| ExifTool and Perl | Image/PDF metadata editing | Setup downloads and verifies ExifTool 13.59. Install the system `perl` interpreter separately. |
| Deno | Pages document conversions | Available through **Download missing tools**. |
| PulseAudio-compatible audio service | Sound in media previews and wheel feedback | PulseAudio or PipeWire's PulseAudio compatibility service. Without it, media picture playback can be silent. |
| Desktop utilities | Opening results, links and terminals | A desktop with `xdg-open`; a supported terminal for the Terminal action. |

For Ubuntu/Debian, install the optional system tools needed for your work:

```sh
sudo apt update
sudo apt install ffmpeg libreoffice perl
```

The LibreOffice Snap and Flatpak builds are not used: their isolated temporary directories are incompatible with the conversion worker. Use your distribution's native package. Tool setup requires an internet connection and verifies pinned downloads. Conversions run locally after their tools are installed. Setup also provisions yt-dlp, but the general link-download interface is not enabled in this preview.

## The two wheel faces

Open files with **Choose files**, drag them into the app, or paste copied files with **Ctrl+V**. **Ctrl+Space** opens the wheel for the selection inside Nashi; with no selection, it opens the picker.

1. **Convert** groups compatible output formats into **Images, Documents, Video, Audio, Archives, Text & Data**. Only applicable groups appear.
2. **Tools** groups editing and file operations by purpose. Depending on the selection, these include Edit, Background, Markup, Create, Frames, Pages, Compress, Metadata, Scan QR and Files.

**Tab** switches between the two faces. Hover over a group briefly to open its outer ring, then move onto an option. Short captions stay on one line; the full action name appears beneath the wheel on hover or keyboard selection. Immediate actions run when selected; operations with options open an editor with Save and Cancel.

With X11 desktop integration running, **hold Ctrl and drag a file**, hover over a group and drop on an outer-ring option. Dropping outside an actionable option does not run a Nashi operation.

## Formats and capabilities

The available targets depend on the source type, selection and installed tools. Not every input converts to every output.

| Input family | Recognized formats | Main capabilities |
| --- | --- | --- |
| Images | JPG/JPEG, PNG, WebP, HEIC/HEIF, TIFF/TIF, SVG, AVIF, BMP | Convert to PNG, JPG, WebP, AVIF, BMP, GIF, TIFF or PDF; edit images and create documents/collages. |
| Video and GIF | MP4, MOV, MKV, WebM, AVI, WMV, GIF, M4V | Convert video; extract audio or JPG/PNG frames; edit and compress. GIF sources have no audio-extraction targets. |
| Audio | MP3, M4A, WAV, FLAC, OGG, OPUS, AIFF, WMA, AAC | Convert to MP3, WAV, FLAC, AAC, M4A, OGG, OPUS, AIFF or WMA; edit and visualize. |
| PDF | PDF | Export each page as JPG/PNG; extract existing text to TXT/Markdown; document conversions and PDF tools. |
| Documents and data | PPT, PPTX, DOCX, Pages, MD, RTF, RTFD, textClipping, HTML, EPUB, TXT, XLSX, XLS, ODS, CSV, TSV | Compatible document, spreadsheet, text and page-image conversions, generally through LibreOffice. Pages routes also need Deno. |
| Archives | ZIP, TAR, GZ, RAR | Extract archives and create supported ZIP/TAR/GZIP outputs. RAR creation is unavailable. |
| Subtitles | SRT, VTT | Subtitle/text conversions. |

HEIC is an input format; **HEIC export is unavailable**. Edited HEIC files save as PNG. A mixed JPG/PNG selection can target either format; every selected file produces a fresh encoded copy. Generic folders are not imported as files.

### Image tools

- Quick rotate, flip, grayscale and plain-background removal; create one PDF or ZIP from a selection.
- **Crop** with draggable bounds, aspect presets and numeric fields; **Edit Photo** with rotation, sizing, light, colour and detail controls.
- **Background** with solid/gradient/image fills, blur, padding, rounded corners and shadows.
- **Redact** with manually drawn solid, blurred or pixelated areas.
- **Annotate** with shapes, arrows, pen, text, numbered markers, callouts, magnifiers and image stamps; save as PNG.
- **Collage** with layouts, ordering, spacing, canvas sizing and backgrounds; save one PNG beside the first source.
- Compression with quality, dimensions and optional target size; metadata browsing/editing/removal through ExifTool; QR reading from images and PDF pages.

Background removal is suited to plain backgrounds, paper, logos and signatures. It is not AI subject segmentation for people or complex scenes. Automatic face/text detection is unavailable.

### PDF and document tools

Organize pages with selection, reordering, rotation, duplication and deletion; split by page, count or ranges; merge in a chosen order; compress and edit metadata. Create PDFs from images. PDF-to-image writes one file per page. PDF-to-text reads an existing text layer: **there is no OCR for scanned pages**.

Some PDF editors prompt for passwords, but the general conversion flow does not yet offer full password/options controls. Conversion defaults include 144 DPI page rendering, editable PDF layout and reflowable EPUB. LibreOffice conversions run headless with an isolated temporary profile, a five-minute timeout and cancellation. On older LibreOffice versions without Markdown export, Markdown falls back to plain text and the result message says so. HTML conversion uses sanitized content through LibreOffice; it does not execute browser scripts.

### Audio and video tools

Video tools include trim, split, crop, speed, snapshots, timed redaction, mute, join, compression and metadata. Video outputs include MP4, MOV, MKV, WebM, AVI, WMV and GIF. Choosing JPG/PNG opens the frame editor with the first frame selected; add time points or intervals before saving.

Audio tools include trim, loudness normalization, channel conversion/swapping, bleep or silence ranges, and audio-to-video visualizations. Editors provide previews and timelines. Space plays/pauses, arrows step, Shift+arrows step frames, I/O set the selected range, and M adds split points where supported. Codec support still depends on the installed FFmpeg build and the source file.

### File operations and Float

The **Files** group offers Copy, Move, Copy Path, Terminal, Share and Float. Share reveals the files in the file manager. Float is a persistent shelf of file references with edge docking, collapsing, selection, copying, revealing and removing entries. Removing a shelf entry does not delete its source file.

Browser drag supports local files, browser image payloads and supported HTTPS downloads. A watched-folder backend is also present, configured through JSON; there is no watched-folder settings screen yet.

## Where new files are saved

**Converted and edited files are saved beside their source.** For example, converting `Downloads/report.pdf` to JPG creates page images such as `Downloads/report Page 1.jpg`. There is no extra Nashi output folder.

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

**X11 is required for global Ctrl-drag and pointer-positioned wheels.** On Wayland, use the app or Open With; the wheel opens centrally rather than tracking the global pointer. Global Ctrl+Space is not installed as a desktop shortcut.

Other launch options open the UI rather than performing headless conversion:

```sh
./Nashi-Linux-x86_64.AppImage --background
./Nashi-Linux-x86_64.AppImage --wheel /path/to/file.pdf
./Nashi-Linux-x86_64.AppImage --actions /path/to/file.pdf
./Nashi-Linux-x86_64.AppImage --uninstall-integration
```

Uninstalling integration stops the background instance and removes its managed desktop entries and legacy scripts; user files, settings and downloaded tools are retained.

Preferences and tool state live in `$XDG_DATA_HOME/Nashi` (normally `~/.local/share/Nashi`). `settings.json` supports `language` (`en`, `tr` or `system`) and `gestureKey` (`Ctrl`, `Shift`, `Alt`, combinations such as `Ctrl+Shift`, or `Off`). The Settings screen is not enabled yet.

## Preview limits and testing status

Detailed tests are ongoing. There is no Library screen, general batch preview, settings UI, automatic updater, full link-download interface or terminal-only conversion command. Jobs currently run sequentially. OCR, automatic face/text detection, HEIC encoding and some conversion-option dialogs remain unavailable.

As of 2026-11-01, the Linux capability matrix offers **373 source/target routes**. A media-enabled fixture run exercised **357**, with **16 Pages routes skipped because Deno was absent**. An ExifTool round trip was also skipped when that tool was absent. These checks cover small fixtures, not every file variant, codec or desktop environment. The native X11 regression check also completes an actual PDF → JPG drag/drop and verifies that the JPEG is beside the unchanged source.

When reporting a problem, include the app version, distribution, X11/Wayland session, source/target format, tool versions and exact error. A small non-sensitive sample is useful.

## Build and verify

Building requires the **.NET 10 SDK**, `curl` and `sha256sum`:

```sh
scripts/build-linux-appimage.sh
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
python3 scripts/test-linux-native-drop.py
xvfb-run --auto-servernum python3 scripts/test-linux-native-drop.py
```
