# Nashi for Linux

Nashi for Linux is a native desktop app built with [Avalonia](https://avaloniaui.net/) on top of the same `Nashi.Core` engine the Windows app uses. It shares the 46-format / 27-tool catalog, the pear artwork, the gold-and-sage palette, the English and Turkish copy, and the Home, wheel, Library, Settings and Finish setup workflows. It runs on X11 and on Wayland (through XWayland) with GNOME, KDE Plasma, Xfce, Cinnamon and other desktops.

## Install

1. Download `Nashi-Linux-x64.tar.gz` and its `.sha256`, then check the archive: `sha256sum -c Nashi-Linux-x64.tar.gz.sha256`.
2. Extract it: `tar -xzf Nashi-Linux-x64.tar.gz`.
3. Run `Nashi/install.sh` for your user only (no administrator password), or `sudo Nashi/install.sh --system` for every user (installs to `/opt/nashi`).

Nashi then appears in your applications menu and under **Open With** for supported files, and `nashi` works in a terminal. You can also run `Nashi/nashi` straight from the extracted folder without installing. `uninstall.sh` removes the app but keeps your Library and settings. The build is self-contained, so you don't need to install .NET.

## Finish setup

Core image, PDF, QR, archive and subtitle tools work right away. **Finish setup** shows what is still missing and how to add it:

| Helper | Unlocks | How |
| --- | --- | --- |
| FFmpeg, yt-dlp, Deno | Audio and video tools, media conversion, web downloads, Pages | **Download missing tools** fetches the pinned, SHA-256-verified Linux x64 builds (`Toolchain.cs`). FFmpeg or yt-dlp already on your `PATH` are used as they are. |
| LibreOffice | Documents, slides and spreadsheets | Install it from your distribution, for example `sudo apt install libreoffice`. Nashi only runs its headless `--convert-to` filters. Snap and Flatpak builds are skipped because their sandbox can't read Nashi's private work folders. |
| Tesseract OCR | Text from scanned PDF pages (optional) | `sudo apt install tesseract-ocr tesseract-ocr-tur` |
| ExifTool | Reading, editing and removing photo and PDF metadata | **Download ExifTool** (needs Perl) |

Setup shows the right install command for Debian/Ubuntu, Fedora, Arch and openSUSE.

## Shortcuts

**Ctrl+O** chooses files and **Ctrl+V** pastes files, file paths, links, images or text outside text fields. **Ctrl+Space** opens the wheel for the selected files: **Tab** switches between Tools, Convert and File actions, the arrow keys move, **Enter** chooses and **Escape** closes. **Ctrl+1/2** switch between Home and Library, **Ctrl+F** searches Library, **Ctrl+Shift+D** downloads a copied public link and **Delete** removes the selected entries from Nashi without deleting the files themselves.

You can drag files from any file manager onto the window. **Float** keeps files in a small always-on-top window you can drag them out of. **Copy** puts files on the clipboard the way file managers expect (`text/uri-list` and `x-special/gnome-copied-files`), and **Show in Folder** selects the file in your file manager when it supports `org.freedesktop.FileManager1`. If another Nashi window is already open, a second launch (for example **Open With → Nashi**) passes its files to that window.

## Where things live

| What | Where |
| --- | --- |
| Library, settings, pasted files | `~/.local/share/Nashi` (`$XDG_DATA_HOME/Nashi`) |
| Downloaded tools | `~/.local/share/Nashi/Tools` |
| Downloads | Your XDG download folder by default; you can change it in Settings |
| User install | `~/.local/share/nashi-app`, `~/.local/bin/nashi`, `~/.local/share/applications/nashi.desktop` |

Exports never overwrite your originals. Each batch is staged in a hidden folder next to its destination and appears only once every file has finished. If you cancel or something fails, nothing is published.

## How it differs from Windows

The Windows image engine uses WPF imaging. On Linux, images go through Magick.NET, which `Nashi.Core` already includes for HEIC, WebP, AVIF and photo adjustments, with the same options, limits and output names. PDFs render with the same PDFium release (`bblanchon.PDFium.Linux` 155.0.8044). Scanned-PDF OCR uses Tesseract instead of `Windows.Media.Ocr`. LibreOffice comes from your distribution instead of a private download. **Share** (the Windows share sheet) has no common Linux equivalent, so Linux offers **Open** and **Show in Folder** instead. Signed in-app updates are Windows- and macOS-only for now: on Linux, install a new release over the old one with `install.sh`.

## Build and verify

You need the .NET 10 SDK.

```sh
scripts/build-linux.sh                                   # build/linux/Nashi and Nashi-Linux-x64.tar.gz
build/linux/Nashi/nashi --self-test /tmp/nashi-check      # 34 real exports, no display needed
```

`--self-test` converts generated images, PDFs, archives, documents, audio and video, then reads the results back. Steps that need LibreOffice, Tesseract or FFmpeg are skipped when that tool is missing. `linux/Nashi.Linux.Snapshots` renders every main screen headlessly to PNG (light, dark and Turkish). It then drives 17 tool flows through the real UI (open the tool, press its default button, wait for the export) and checks each output and its Library entry:

```sh
XDG_DATA_HOME=$(mktemp -d) dotnet run --project linux/Nashi.Linux.Snapshots -c Release -- /tmp/nashi-ui /tmp/nashi-check/fixtures
```

The **Linux engine** workflow runs the Nashi.Core verification, the self-test, the screenshots, the UI flows and the archive build on every push and pull request.
