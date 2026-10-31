# Nashi for Linux (preview)

Nashi for Linux is a milestone 1 preview built with .NET 10 and Avalonia. It shares the conversion engine with the Windows app. The AppImage is self-contained: no separate .NET install is required. It is unsigned, and the CI artifact is not an official signed update. Verify the download against its `.sha256` file.

The [AppImage link](https://github.com/orkunisitmak/nashi-app/releases/latest/download/Nashi-Linux-x86_64.AppImage) works once a maintainer publishes a release that includes it; until then, download the "Linux preview" CI artifact. Then `chmod +x Nashi-Linux-x86_64.AppImage` and run it. Running needs libfuse2, or use `--appimage-extract-and-run`. `--version` prints the app version.

## What works in milestone 1

- Home with the file garden, light and dark appearance, and English or Turkish.
- File intake: **Choose files**, drag from your file manager, or **Ctrl+V** to paste copied files.
- **Ctrl+Space** opens the conversion wheel for the selected files, with live progress. With nothing selected, it opens the file picker first. The **Actions** chip opens the actions wheel: Share (shows the files in your file manager), Copy, Move, Copy Path and Terminal. Float is not available yet.
- The wheel looks and sounds like the macOS one: icons above upper-case labels, a gold highlight, a short opening animation (skipped when GNOME animations are off) and the selection sound: softer on every petal change (arrow keys, Tab or the pointer), full on a choice. It plays in-process through PulseAudio (`libpulse-simple`, which PipeWire also serves), else through `paplay`, `pw-play` or `aplay` when one is installed.
- Images: format conversion. HEIC output is not available on Linux because no encoder is available.
- PDF: merge, create from images and compress. PDF to PNG or JPG (one image per page) and to TXT or Markdown work right away; PDF to DOCX, PPTX, XLSX, EPUB and Pages need LibreOffice.
- Documents: every conversion the macOS app offers for Word, PowerPoint, Excel, OpenDocument, CSV/TSV, RTF/RTFD, HTML, EPUB, Markdown, plain text, text clippings and Pages files, including pages as PNG or JPG images. These need LibreOffice (see below). Pages files also need Deno, which **Download missing tools** in setup fetches.
- Audio and video: convert, compress, mute and normalize. These need media tool setup from Home or a system `ffmpeg`.
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
- **Ctrl+Alt+N** after copying files (Ctrl+C in Files) shows the wheel for the copied files.
- **Open With → Nashi** works for images, audio, video, PDF and ZIP files.

Choose a tool and a format as in the app. New files are saved next to the first file, or in a folder you pick when that one is read-only. A card at the bottom of the screen says when they are ready and offers **Show in folder**.

It writes only `~/.config/autostart/nashi.desktop`, `~/.local/share/applications/nashi.desktop`, `~/.local/share/icons/hicolor/256x256/apps/nashi.png`, the two Nautilus scripts and one GNOME custom shortcut. Keep the AppImage where it is, since those entries point at its path. To remove all of that and stop the background instance:

```sh
./Nashi-Linux-x86_64.AppImage --uninstall-integration
```

Showing the wheel at the pointer needs an X11 session. On Wayland the wheel opens in the middle of the screen. The keyboard shortcut needs GNOME. Without it the installer skips the shortcut and says so.

## Coming

Editors (crop, annotate, trim), conversion options (DPI, PDF layout, fixed-layout EPUB, PDF passwords), OCR for scanned PDFs, web download, Library, settings and automatic updates.

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
