# Nashi for Linux (preview)

Nashi for Linux is a milestone 1 preview built with .NET 10 and Avalonia. It shares the conversion engine with the Windows app. The AppImage is self-contained: no separate .NET install is required. It is unsigned, and the CI artifact is not an official signed update. Verify the download against its `.sha256` file.

The [AppImage link](https://github.com/orkunisitmak/nashi-app/releases/latest/download/Nashi-Linux-x86_64.AppImage) works once a maintainer publishes a release that includes it; until then, download the "Linux preview" CI artifact. Then `chmod +x Nashi-Linux-x86_64.AppImage` and run it. Running needs libfuse2, or use `--appimage-extract-and-run`. `--version` prints the app version.

## What works in milestone 1

- Home with the file garden, light and dark appearance, and English or Turkish.
- File intake: **Choose files**, drag from your file manager, or **Ctrl+V** to paste copied files.
- **Ctrl+Space** opens the conversion wheel for the selected files, with live progress. With nothing selected, it opens the file picker first. The **Actions** chip opens the actions wheel: Share (shows the files in your file manager), Copy, Move, Copy Path and Terminal. Float is not available yet.
- The wheel looks and sounds like the macOS one: icons above upper-case labels, a gold highlight, a short opening animation (skipped when GNOME animations are off) and the selection sound through `paplay`, `pw-play` or `aplay` when one is installed.
- Images: format conversion. HEIC output is not available on Linux because no encoder is available.
- PDF: merge, create from images and compress.
- Audio and video: convert, compress, mute and normalize. These need media tool setup from Home or a system `ffmpeg`.
- Archives and subtitles.

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
