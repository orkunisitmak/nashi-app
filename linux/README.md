# Nashi for Linux — alpha candidate

This is a native Linux port of Nashi, inside the original repository. It shares Nashi's original feature catalog, media engine, photo adjustments/codecs, archives, subtitles, QR decoder, metadata, Library/preferences and Office document reconstruction. It is not an independent lookalike. The Linux window uses Avalonia because SwiftUI and WPF are tied to their host platforms. Original Nashi branding, copy, artwork and palette are retained.

**This branch is an experimental alpha candidate, not a declaration of complete Mac/Windows parity.** See [ALPHA.md](ALPHA.md) for the initial usability gate and supported test scope. See [FEATURE-PARITY.md](FEATURE-PARITY.md) for the remaining work. Do not advertise all 27 tools being listed as proof that every editor behavior has been ported.

## Run

Packages include .NET; users do not need the SDK. Select **x86_64** for Intel/AMD 64-bit PCs or **aarch64** for ARM64 machines.

- **AppImage:** make executable (`chmod +x Nashi-Linux-x86_64.AppImage`) and open it. If FUSE is unavailable, run `./Nashi-Linux-x86_64.AppImage --appimage-extract-and-run`.
- **tar.gz:** extract the complete Nashi folder and run `./Nashi`. Optionally run `./install.sh` to add it to your own application menu without sudo.
- **deb:** `sudo apt install ./Nashi-Linux-x86_64.deb`.
- **rpm:** `sudo dnf install ./nashi-*.x86_64.rpm`.

Keep the complete application folder together. AppImages include the application and .NET, not optional conversion tools or every OS library. They do not bypass the host's glibc requirements.

The intended build baseline is Ubuntu 22.04 (x64) / Ubuntu 24.04 (ARM64), with glibc 2.35+ and .NET 10 native dependencies. Modern Ubuntu/Mint, Debian, Fedora and Arch derivatives are targets. X11 and Wayland through XWayland are supported by this configuration. Alpine/musl, 32-bit Linux, old distributions below that baseline and every desktop environment have **not** been verified. We do not promise universal Linux compatibility on the strength of one machine's results. See [.NET Linux requirements](https://learn.microsoft.com/en-us/dotnet/core/install/linux) and [Avalonia Linux support](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).

## Optional system tools

Nashi uses executable paths from your `PATH` and never installs Windows EXE/MSI packages on Linux. The setup screen shows what is available. Install optional programs with your distribution's package manager:

```sh
# Debian / Ubuntu / Mint
sudo apt install ffmpeg libreoffice poppler-utils tesseract-ocr libimage-exiftool-perl xdg-utils
# Embedded playback: libmpv1 on Ubuntu 22.04, libmpv2 on newer distributions.
sudo apt install libmpv2

# Fedora (FFmpeg codecs may require your distribution's optional codec repository)
sudo dnf install ffmpeg-free libreoffice poppler-utils tesseract perl-Image-ExifTool mpv-libs xdg-utils

# Arch / Garuda / Manjaro
sudo pacman -S ffmpeg libreoffice-fresh poppler tesseract tesseract-data-eng perl-image-exiftool mpv xdg-utils yt-dlp deno
```

Public web-media downloads also need `yt-dlp` and Deno. Use your distribution's packages or their official upstream instructions. Nashi does not read browser cookies or bypass sign-in/DRM. Office/Pages conversion needs LibreOffice; Pages additionally needs Deno. OCR needs Tesseract and installed language data. Image/PDF-organizing/archive/subtitle features do not require all media tools to be installed.

Library and preferences use the Linux .NET `LocalApplicationData` location (normally `$XDG_DATA_HOME/Nashi` or `~/.local/share/Nashi`), outside the application folder. File paths are case sensitive on Linux.

## Build and verify

Install the .NET 10 SDK, then:

```sh
dotnet run --project linux/Nashi.Linux -c Release
dotnet run --project linux/Nashi.Linux.Verify -c Release
NASHI_VERIFY_FFMPEG=/usr/bin/ffmpeg NASHI_VERIFY_FFPROBE=/usr/bin/ffprobe \
  dotnet run --project windows/Nashi.Verify -c Release
scripts/build-linux.sh linux-x64  # or linux-arm64
NASHI_APPIMAGE=1 scripts/package-linux.sh linux-x64
# Optional: keep review candidates apart from an existing tested alpha.
NASHI_APPIMAGE=1 NASHI_DIST_DIR="$PWD/build/linux/candidates" scripts/package-linux.sh linux-x64
NASHI_SMOKE_DIRECTORY="$PWD/build/linux-smoke" build/linux/linux-x64/Nashi/Nashi --smoke-test
```

`package-linux.sh` always creates a tarball. It also creates deb/rpm packages when `dpkg-deb` (or `ar`)/`rpmbuild` are present; `NASHI_APPIMAGE=1` downloads hash-pinned official AppImage tooling and runtime. Cross-packaging uses the build host’s appimagetool with the verified target-architecture runtime; actually running ARM64 packages still needs an ARM64 machine. GitHub Actions performs independent x64 and ARM64 builds and uploads packages plus native UI evidence. An artifact or checksum is not a signed update channel or an official upstream release.

While dragging files **onto Nashi**, hold **Ctrl** for tools, **Shift** for conversion or **Alt** for file actions, then drop onto a wheel segment. An Xdnd monitor also implements global wheels for X11 sources while Nashi is open. Native Wayland sources do not expose global drag data/modifier state to arbitrary applications; global behavior is not verified there.

**Ctrl+O** chooses files, **Ctrl+V** imports files/text/images/links outside text inputs, **Ctrl+Space** opens the tool wheel, **Ctrl+1/2** switches Home/Library, **Ctrl+F** searches Library. Embedded playback uses Space, arrow keys and I/O. Image crop uses drag and Shift for a square; annotation supports Ctrl+Z and Select/Move. Originals are exported as new copies through Nashi's transaction layer.

## License

The original [MIT license](../LICENSE) and author attribution remain. See [Linux third-party notices](THIRD-PARTY-NOTICES.md). The fork's Linux preview must not be described as an official release from the upstream maintainer.

Float supports adding local files through the picker or file drops, removing shelf entries, reusing individual/all files, and collapsing the shelf. Drag/Drag all exports file references with copy semantics so removing a shelf entry or dragging does not delete the original. Real drag-out interoperability across file managers and desktops still requires manual verification.

Redact Photo supports multiple independent areas, resize handles, numeric coordinates, area selection/deletion and undo/redo. Solid, blur and pixelate have live previews; Show original temporarily hides the masks. Detect text uses local Tesseract and a selected installed recognition language, with [documented word coordinates](https://tesseract-ocr.github.io/tessdoc/Command-Line-Usage.html#tsv-output). Detected areas can be reviewed and adjusted before saving. Detection can be cancelled; Clear and Reset also cancel it. Face detection is still a parity gap.
