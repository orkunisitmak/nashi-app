# Nashi for Linux

Nashi runs natively on Linux desktops (X11 and Wayland via XWayland) with the same 45-format / 27-tool catalog, Home, Library, tools wheel, paper-sage-gold light/dark palette and English/Turkish UI as Windows and macOS. It is built with Avalonia on .NET 10 and reuses the shared `windows/Nashi.Core` engine, so conversions follow the same code, transactions and safety rules as the Windows app.

## Install

```sh
git clone https://github.com/orkunisitmak/nashi-app.git
cd nashi-app
linux/install.sh
```

The script needs the .NET 10 SDK only to build (`sudo pacman -S dotnet-sdk`, `sudo apt install dotnet-sdk-10.0`, `sudo dnf install dotnet-sdk-10.0`, or the user-level `dotnet-install.sh --channel 10.0`). It installs a self-contained app for the current user without `sudo`:

| Path | Contents |
| --- | --- |
| `~/.local/lib/nashi` | the app, including its own .NET runtime, PDFium, Skia and Magick.NET |
| `~/.local/bin/nashi` | command-line entry point |
| `~/.local/share/applications/nashi.desktop` | app menu entry and **Open with › Nashi** for every supported file type |
| `~/.local/share/icons/hicolor` | 256 and 512 px icons |

`linux/install.sh --uninstall` removes exactly those files. Library and settings stay in `${XDG_DATA_HOME:-~/.local/share}/Nashi`. For a system-wide install: `PREFIX=/opt/nashi BIN_DIR=/usr/local/bin DATA_DIR=/usr/local/share sudo -E linux/install.sh`.

## System tools

Windows downloads pinned Windows builds of FFmpeg, yt-dlp, Deno, ExifTool and LibreOffice. On Linux, Nashi uses your distribution's packages instead, so they receive security updates with the rest of the system. **Finish setup** in the app (or `nashi doctor`) shows what was found and the exact command for your distribution; Nashi never runs `sudo` itself.

| Tool | Used for | Arch / CachyOS | Debian / Ubuntu | Fedora |
| --- | --- | --- | --- | --- |
| FFmpeg | video/audio tools, HEIC export | `ffmpeg` | `ffmpeg` | `ffmpeg` (RPM Fusion) or `ffmpeg-free` |
| yt-dlp | web downloads | `yt-dlp` | `yt-dlp` | `yt-dlp` |
| Deno or Node.js | yt-dlp JavaScript challenges, Pages files | `deno` | `nodejs` | `nodejs` |
| LibreOffice | documents, slides, spreadsheets | `libreoffice-fresh` | `libreoffice` | `libreoffice` |
| ExifTool | metadata editing (optional) | `perl-image-exiftool` | `libimage-exiftool-perl` | `perl-Image-ExifTool` |
| Tesseract | text from scanned PDFs (optional) | `tesseract tesseract-data-eng tesseract-data-tur` | `tesseract-ocr tesseract-ocr-eng tesseract-ocr-tur` | `tesseract tesseract-langpack-eng tesseract-langpack-tur` |

Images, PDF tools, archives, subtitles and QR codes work with no extra packages. A binary placed in `~/.local/share/Nashi/Tools/v1/` (for example a static FFmpeg build) takes precedence over the system package. Pages files run through Deno when installed, otherwise through Node.js with its `--permission` model limited to the exact input and output paths.

## Use

- **Open with › Nashi** from Files/Nautilus, Dolphin, Nemo, Thunar and other file managers, drop files on the window, **Ctrl+O** or **Ctrl+V** (files, an image, text or a link).
- A drop opens the tools wheel. **Ctrl+Space** reopens it; arrows/Tab move, Enter opens, Escape closes.
- **Ctrl+1/2** Home/Library, **Ctrl+F** search Library, **Ctrl+,** Settings, **Ctrl+Shift+D** use a copied media link.
- Crop, redact and annotate regions are dragged on the preview (right-click or Delete removes the last one). Video crop and redaction use the first frame.
- Outputs go beside the original, to a fixed folder, or to a folder you choose each time (Settings). Originals are never overwritten; a failed or cancelled batch publishes nothing.
- **Move & share** opens the containing folder (through the `org.freedesktop.FileManager1` D-Bus interface), copies paths, copies/moves files and opens your terminal (`$TERMINAL`, then GNOME Console/Terminal, Konsole, Xfce, kitty, Alacritty, WezTerm, foot, Tilix, xterm).
- Downloads go to the XDG download folder, including localized names such as `~/İndirilenler`.

The same engine is scriptable:

```sh
nashi doctor
nashi list photo.heic
nashi convert png photo.heic --out ~/Pictures
nashi tool trimVideo clip.mp4 --start 3 --end 9.5
nashi tool redactImage scan.jpg --region 40,60,300,80 --style pixelate
nashi qr ticket.pdf
nashi download https://example.com/video --kind audio
```

`nashi --help` lists every option. `nashi --render out.png --page home|library|wheel|tools|settings [--theme dark] [--lang tr]` renders the real window off-screen, and `--tool convert:webp --run FILE` presses Run in it; CI uses both on machines without a display.

## Linux compatibility notes

What did not work on Linux before this port, and how it is handled now:

| Area | Before | Linux behaviour |
| --- | --- | --- |
| Desktop app | macOS app uses AppKit/SwiftUI/PDFKit/Vision; Windows app uses WPF, Win32 hooks, WinRT OCR and `msi.dll` — none run on Linux | New Avalonia front end (`linux/Nashi.Linux`) over the shared engine |
| Tool setup | `Toolchain` expected `ffmpeg.exe`, `yt-dlp.exe`, `deno.exe` and downloaded Windows archives | Distribution packages found on `PATH` (plus Arch's `/usr/bin/vendor_perl`), Node.js accepted for yt-dlp, install command per distribution |
| ExifTool / LibreOffice | Windows ZIP/MSI installers | `exiftool`, `soffice`/`libreoffice` from packages or the official `/opt/libreoffice*` tarball |
| Image engine | WPF `BitmapSource` encoders | Magick.NET (already bundled for Linux) with the same orientation, sRGB, metadata-stripping and size limits |
| PDF rendering | PDFium Win32 package | Same PDFium build from `bblanchon.PDFium.Linux` |
| Scanned PDF text | `Windows.Media.Ocr` | Tesseract when installed |
| Download folder | `~/Downloads` hard-coded | XDG `user-dirs.dirs` (`XDG_DOWNLOAD_DIR`) |
| Library paths | case-insensitive de-duplication | case-sensitive on Linux, where `Photo.png` and `photo.png` are different files |
| Pages runtime | Deno only | Deno, or Node.js with `--permission` |
| Reveal / terminal / open | `explorer.exe` | FileManager1 D-Bus, `xdg-open`, the user's terminal |
| Updates | signed Windows ZIP replaces `Nashi.exe` | not self-replacing; update through `git pull && linux/install.sh` or a package |

Not available on Linux by design: system-wide drag gestures while dragging in another app (Wayland does not let apps watch global input; use **Open with**, drop or the CLI instead), the tray icon and the Float shelf. HEIC export needs FFmpeg with libx265, which the standard Arch, Debian/Ubuntu and RPM Fusion builds include.

## Build and verify

```sh
dotnet run --project windows/Nashi.Verify -c Release        # shared engine + Linux checks
dotnet publish linux/Nashi.Linux -c Release -o build/linux/Nashi
scripts/test-linux-cli.sh build/linux/Nashi/nashi           # every engine path end to end
```

Set `NASHI_VERIFY_FFMPEG=/usr/bin/ffmpeg NASHI_VERIFY_FFPROBE=/usr/bin/ffprobe` to include the real FFmpeg checks. `.github/workflows/linux.yml` runs all three on Ubuntu with LibreOffice, Tesseract and ExifTool from apt.
