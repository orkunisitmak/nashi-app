# Nashi for Linux (preview)

Nashi for Linux is a milestone 1 preview built with .NET 10 and Avalonia. It shares the conversion engine with the Windows app. The AppImage is self-contained: no separate .NET install is required. It is unsigned, and the CI artifact is not an official signed update. Verify the download against its `.sha256` file.

The [AppImage link](https://github.com/orkunisitmak/nashi-app/releases/latest/download/Nashi-Linux-x86_64.AppImage) works once a maintainer publishes a release that includes it; until then, download the "Linux preview" CI artifact. Then `chmod +x Nashi-Linux-x86_64.AppImage` and run it. Running needs libfuse2, or use `--appimage-extract-and-run`. `--version` prints the app version.

## What works in milestone 1

- Home with the file garden, light and dark appearance, and English or Turkish.
- File intake: **Choose files**, drag from your file manager, or **Ctrl+V** to paste copied files.
- **Ctrl+Space** opens the conversion wheel for the selected files, with live progress.
- Images: format conversion. HEIC output is not available on Linux because no encoder is available.
- PDF: merge, create from images and compress.
- Audio and video: convert, compress, mute and normalize. These need media tool setup from Home or a system `ffmpeg`.
- Archives and subtitles.

## Coming

Editors (crop, annotate, trim), Office documents, web download, Library, settings and automatic updates.

## Build

Requires the .NET 10 SDK, `curl` and `sha256sum`.

```sh
scripts/build-linux-appimage.sh
```

The script publishes a self-contained build, downloads a pinned, SHA-256-verified appimagetool, and writes `build/linux/Nashi-Linux-x86_64.AppImage` plus its `.sha256`. Run the checks with:

```sh
dotnet run --project windows/Nashi.Verify -c Release
dotnet run --project linux/Nashi.Linux.Checks -c Release
```
