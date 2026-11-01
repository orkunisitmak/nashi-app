# Nashi for Linux (preview)

Nashi for Linux is a milestone 1 preview built with .NET 10 and Avalonia. It shares the conversion engine with the Windows app. The AppImage is self-contained: no separate .NET install is required. It is unsigned, and the CI artifact is not an official signed update. Verify the download against its `.sha256` file.

The [AppImage link](https://github.com/orkunisitmak/nashi-app/releases/latest/download/Nashi-Linux-x86_64.AppImage) works once a maintainer publishes a release that includes it; until then, download the "Linux preview" CI artifact. Then `chmod +x Nashi-Linux-x86_64.AppImage` and run it. Running needs libfuse2, or use `--appimage-extract-and-run`. `--version` prints the app version.

## What works in milestone 1

- Home with the file garden, in translucent black (dark) or white (light) glass with gold as the only accent, and English or Turkish. The desktop shows through blurred where the compositor blurs (KWin), through a denser canvas where it only blends (GNOME), and the window is opaque without a compositor.
- File intake: **Choose files**, drag from your file manager, or **Ctrl+V** to paste copied files.
- **Ctrl+Space** opens the conversion wheel for the selected files, with live progress. With nothing selected, it opens the file picker first. The **Actions** chip opens the actions wheel: Share (shows the files in your file manager), Copy, Move, Copy Path and Terminal. Float is not available yet.
- The wheel looks and sounds like the macOS one: icons above upper-case labels, a gold highlight, a short opening animation (skipped when GNOME animations are off) and the selection sound: softer on every petal change (arrow keys, Tab or the pointer), full on a choice. It plays in-process through PulseAudio (`libpulse-simple`, which PipeWire also serves), else through `paplay`, `pw-play` or `aplay` when one is installed.
- Images: format conversion. HEIC output is not available on Linux because no encoder is available, so edited HEIC photos save as PNG.
- Image tools open the macOS editor: a large preview beside the options, Reset, Cancel and a gold Save (Return saves, Esc cancels). One editor is open at a time; choosing another tool brings it back. Live previews render on a smaller copy; a Save made while another task runs waits for it; Save writes the full-size copy beside the source, named as on macOS ("photo - Cropped.jpg"). Crop Image: drag to select, drag the handles or the inside, Shift locks the ratio, aspect presets, W/H/X/Y fields; one crop applies to every selected image and must fit inside each. Edit Photo: rotate, flip, size presets or W/H, and the macOS light, colour and detail sliders. Add BG: a solid, gradient or image background (with blur), padding, corners, shadow, canvas ratio and position; transparent backgrounds save as PNG. Redact Photo: drag areas onto the photo, select, move or delete them, and cover them solid, blurred or pixelated (blur and blocks grow with each area; Strength scales them) (face and text detection use macOS Vision and are not offered). Compress (images): Balanced or Strong with a quality slider, an image size, and an optional target in MB, KB or % smaller. Metadata (images): the tags by group with a search, fields for the EXIF and XMP tags Nashi writes, and Remove All. It uses ExifTool 13.59 (Perl, so `perl` must be installed), which **Download missing tools** in setup fetches and checks; without it, choosing Metadata opens setup. Read QR Codes (images and PDF pages): each code in a card with Copy, Open for http(s) links (through `xdg-open`), and Copy all. Annotate Photo: arrows, lines, rectangles, ellipses, pen and marker, text, numbers, callouts, a magnifier and image stamps in a colour, font, stroke and size, with Select / Move, Delete, Undo and Clear; the copy is a PNG. Create Collage (two or more images): grid, row, column or featured, columns, canvas ratio and width, fill or fit, spacing, padding, corners, a background colour or image, and the order by dragging; one PNG beside the first image.
- PDF: Organize PDF (page thumbnails; click, Ctrl and Shift to select, drag to reorder, Rotate, Duplicate, Delete, Move Left/Right, Make all pages the same width), Split PDF (every page, every N pages, or ranges such as 1-3, 5 with one file each), Merge PDFs in a dragged order with page counts, Compress, and Metadata (PDF and XMP tags through ExifTool). A password-protected PDF asks for its password. Create from images. PDF to PNG or JPG (one image per page) and to TXT or Markdown work right away; PDF to DOCX, PPTX, XLSX, EPUB and Pages need LibreOffice.
- Documents: every conversion the macOS app offers for Word, PowerPoint, Excel, OpenDocument, CSV/TSV, RTF/RTFD, HTML, EPUB, Markdown, plain text, text clippings and Pages files, including pages as PNG or JPG images. These need LibreOffice (see below). Pages files also need Deno, which **Download missing tools** in setup fetches.
- Audio and video: convert, and every macOS tool in its editor: Trim, Split (points with M, equal clips, leave clips out, join the rest), Crop (on a frame), Change Speed (0.25× to 4×, pitch kept), Snapshots (at the playhead, every N seconds or evenly spaced), Redact Video (areas on the frame, each over a time range; solid, blur or pixelate), Mute, Join Videos (dragged order), Compress (Balanced or Strong, optional target), Metadata (container tags, Remove all), Trim Audio, Normalize (loudness, range, true peak), Channels (mono or stereo, swap, volumes, sample depth for WAV and AIFF), Bleep (ranges of a tone or silence) and Audio to Video (waveform, spectrum, bars or a still image, colour and size). The preview plays with no extra packages: ffmpeg decodes frames into the editor and the sound into PulseAudio (PipeWire serves it too); without PulseAudio the picture plays silently and the editor says so. The timeline shows a filmstrip or waveform with gold In/Out handles; Space plays, ←/→ step a second, Shift+←/→ a frame, I and O set the range. Speed, channels, bleeps and loudness preview processed. These need media tool setup from Home or a system `ffmpeg`.
- Extract Archive asks for the password of a protected ZIP or RAR and extracts into one folder beside it.
- Archives and subtitles.

## LibreOffice

Office, text, EPUB and Pages conversions run through the system LibreOffice (`soffice` or `libreoffice` on `PATH`). Nashi does not download it, because LibreOffice publishes no portable Linux build. Install it with your package manager:

```sh
sudo apt install libreoffice      # Debian, Ubuntu
sudo dnf install libreoffice      # Fedora
```

The wheel always shows these routes. Choosing one without LibreOffice opens setup, whose **Office tools** row says *Missing* and how to install it. The Snap and Flatpak builds are not used: their sandbox has its own `/tmp`, where each conversion keeps a private, throwaway LibreOffice profile. Every conversion runs headless, stops after 5 minutes, and Cancel stops LibreOffice at once. LibreOffice versions without a Markdown export filter (7.x and similar; Nashi checks the installed filter list) save Markdown as plain text, and the success card says "Saved as plain-text Markdown".

## Use Nashi without opening it

Run once from a terminal:

```sh
./Nashi-Linux-x86_64.AppImage --install-integration
```

This starts Nashi in the background now and at every login, with no window. Then:

- **Right-click → Scripts → Convert with Nashi** in Files (Nautilus) shows the wheel at the pointer for the selected files. With a Turkish UI the entry is "Nashi ile dönüştür".
- **Right-click → Scripts → Nashi actions** shows the actions wheel for them ("Nashi işlemleri" in Turkish). `Nashi --actions FILE...` does the same.
- **Open With → Nashi** works for images, audio, video, PDF and ZIP files.
- **Hold Ctrl and drag files** from Files, the desktop or a browser: the wheel opens at the pointer with the formats that fit. Drop on a petal to run it; Tab turns to the other faces. A drop anywhere else is an ordinary Ctrl copy. The key can be changed in `settings.json` (`gestureKey`: Ctrl, Shift, Alt, two of them joined with `+`, or Off).

Choose a tool and a format as in the app. New files are saved directly next to the file they came from, each in its own folder (an archive extracts into one folder named after it, next to it; Merge and Create PDF save beside the first file). When a folder is read-only you pick another one for its files. A card at the bottom of the screen says when they are ready and offers **Show in folder**.

It writes only `~/.config/autostart/nashi.desktop`, `~/.local/share/applications/nashi.desktop`, `~/.local/share/icons/hicolor/256x256/apps/nashi.png`, and the two Nautilus scripts. It also removes the Ctrl+Alt+N shortcut that earlier versions added, keeping your other shortcuts. Keep the AppImage where it is, since those entries point at its path. To remove all of that and stop the background instance:

```sh
./Nashi-Linux-x86_64.AppImage --uninstall-integration
```

Showing the wheel at the pointer and the Ctrl-drag need an X11 session. On Wayland the wheel opens in the middle of the screen, and a one-time card says that Ctrl-drag needs X11.

## Coming

Media editors (trim, crop video and the rest), conversion options (DPI, PDF layout, fixed-layout EPUB, PDF passwords), OCR for scanned PDFs, web download, Library, settings and automatic updates.

## Build

Requires the .NET 10 SDK, `curl` and `sha256sum`. PDF rendering uses PDFium from the pinned `bblanchon.PDFium.Linux` package, which the build bundles.

```sh
scripts/build-linux-appimage.sh
```

The script publishes a self-contained build, downloads a pinned, SHA-256-verified appimagetool, and writes `build/linux/Nashi-Linux-x86_64.AppImage` plus its `.sha256`. Run the checks with:

```sh
dotnet run --project windows/Nashi.Verify -c Release
dotnet run --project linux/Nashi.Linux.Checks -c Release
```

The Linux checks run every offered conversion route once on a small fixture. Routes that need a missing tool are skipped and counted: set `NASHI_VERIFY_FFMPEG`, `NASHI_VERIFY_FFPROBE` and `NASHI_VERIFY_DENO` to tool paths, and install LibreOffice, to run all of them.
