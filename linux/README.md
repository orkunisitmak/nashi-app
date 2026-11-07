# Nashi for Linux

Nashi for Linux is a native desktop app built with [Avalonia](https://avaloniaui.net/) 12 on top of the same `Nashi.Core` engine the Windows app uses. It shares the 46-format / 27-tool catalog, the pear artwork, the gold-and-sage palette, the English and Turkish copy, and the Home, wheel, Library, Settings and Finish setup workflows. It runs on X11 and on Wayland (through XWayland) with GNOME, KDE Plasma, Xfce, Cinnamon and other desktops.

## Install

1. Download `Nashi-Linux-x64.tar.gz` and its `.sha256`, then check the archive: `sha256sum -c Nashi-Linux-x64.tar.gz.sha256`.
2. Extract it: `tar -xzf Nashi-Linux-x64.tar.gz`.
3. Run `Nashi/install.sh` for your user only (no administrator password), or `sudo Nashi/install.sh --system` for every user (installs to `/opt/nashi`).

Nashi then appears in your applications menu and under **Open With** for supported files, and `nashi` works in a terminal. You can also run `Nashi/nashi` straight from the extracted folder without installing. `uninstall.sh` removes the app but keeps your Library and settings. The build is self-contained, so you don't need to install .NET.

## Hot edge: just drag

Drag files (from Dolphin, the desktop or anywhere else) to the **left edge of the screen**, with no key. A small wheel of the three wheels opens. Rest on Tools, Convert or File actions for a moment to open that wheel, then drop on a petal. The edge is an invisible 2-pixel Wayland layer-shell strip. Its corners are left free for the desktop's own corner actions. You can choose the left, right or bottom edge, or turn it off, under **Settings → Shortcuts & gestures → Hot edge**. It needs a Wayland compositor with layer-shell: KDE Plasma, Sway or Hyprland. With several monitors the strip runs along the outer edge only (a left strip never sits between two screens, where every drag across would hit it), and the drop layer covers every monitor. It also works after Show Desktop (Meta+D or a screen corner): the strip and the wheel are docks to KWin, so your windows stay hidden while you drag from the desktop.

## Drag gestures and system-wide shortcuts

On macOS and Windows you hold a modifier while dragging a file anywhere to get the wheel. Wayland doesn't show an app another app's drag, so on Linux you press a **system-wide shortcut while dragging** instead. Nashi registers these with KDE Plasma (kglobalaccel). You can change them under **Settings → Shortcuts & gestures** by clicking a shortcut and pressing the new keys. Nashi warns you when another app already uses a combination. A function key on its own (for example `F8`) is allowed too, so the gesture is one key for the other hand while you drag.

| Default | What it does |
| --- | --- |
| `Meta+Space` | **Gesture:** while dragging files (from Dolphin, the desktop, anywhere), press it anywhere on the screen, then give the drag a short push (about 40 px) ← for File actions, → for Convert or ↑ for Tools. The wheel opens under the pointer; drop on a petal. ↓ cancels, and so does `Esc`. Each direction can be changed in Settings. |
| `Meta+Alt+C` / `Meta+Alt+T` / `Meta+Alt+A` | Open the Convert, Tools or File actions wheel straight away; drop the dragged files on a petal |
| `Meta+Alt+V` | Wheel for the files you copied in your file manager (`Ctrl+C`), with no dragging needed |
| `Meta+Alt+N` | Show Nashi |
| `Meta+Alt+D` | Download a copied link |

While the gesture is open, Nashi also listens directly on Wayland through a transparent layer-shell overlay on KDE Plasma, Sway, Hyprland and other wlroots compositors. Drags therefore arrive even when the compositor's XWayland drag bridge stops passing them, which was seen on Plasma 6.8 mid-session. The dragged files are read while you are still dragging, so the wheel shows the real choices before you drop.

When you drop on a conversion or a simple tool, it runs straight away with its saved export defaults and a desktop notification reports the result. Tools that need your input, like crop, redact or trim, open their editor instead. You can turn this off in Settings. Nashi stays in the system tray when you close its window, so the shortcuts keep working. **Start Nashi in the tray when I log in** keeps them available after a restart. On desktops without kglobalaccel, use `Ctrl+Space` inside Nashi, or hold Ctrl, Shift or Alt while dropping files on its window.

## Themes

**Settings → Appearance** previews each theme live, in light or dark:

- **Classic:** the pear palette shared with macOS and Windows.
- **Glass:** a see-through window with tinted panes.
- **Frosted glass:** your desktop is blurred behind Nashi, using KDE's blur.
- **Brutalist:** square corners, heavy outlines, hard shadows and loud yellow.

The wheel uses the macOS petal layout in every theme.

## Watched folders

**Settings → Automation** converts new files that land in a folder while Nashi runs, including from the tray. For example, HEIC photos synced into Downloads appear as JPG next to the originals. Each rule has a folder, extensions and a target format, and uses your saved export defaults. Originals are never changed. Partial downloads (`.part`, `.crdownload`) and hidden files are skipped, and Nashi ignores its own results so a rule can't loop. Each result goes to Library and shows a notification.

## Screenshots and copied images

`Meta+Alt+S` lets you select part of the screen with your desktop's screenshot tool (Spectacle, GNOME Screenshot, Xfce Screenshooter or Flameshot), saves the capture to Library and opens the Tools wheel on it, so you can annotate, redact, crop, read a QR code or convert it. `Meta+Alt+V` does the same for an image on the clipboard, or for files copied in your file manager. New results can be dragged straight out of the progress card or a Library preview into other apps.

## Everyday touches

- **Quick look:** click a file on Home for a large preview with its size, pixels or length. ← and → move through the selection, and Space or Escape closes it.
- **Again:** your last tool or conversion appears as the first petal on the Tools wheel and as a button on Home whenever it fits the new files. Nashi remembers it after a restart.
- **Library filters:** All, Conversions, Tools, Downloads, Pasted and Screenshots, each with a count. Drag a preview to take its files into another app.

## Linux extras

On top of the shared 27-tool catalog, the Linux app adds:

- **Copy Text:** reads the text in images and PDF pages with Tesseract and puts it on the clipboard. Combined with `Meta+Alt+S`, you can grab any text on screen. Install more languages with your distribution, for example `tesseract-data-tur` on Arch or `tesseract-ocr-tur` on Debian/Ubuntu.
- **Rename:** batch-renames files where they are, with a live preview. Patterns can use `{name}`, `{n}`, `{date}`, `{today}` and `{ext}`, plus a letter-case option. Clashes are shown before anything changes.
- **Send to Phone:** sends files to a phone paired in KDE Connect, the Linux counterpart to the Windows share sheet.
- **Set as Wallpaper:** for a single image. It uses `plasma-apply-wallpaperimage` on KDE Plasma and `gsettings` on GNOME. Nashi keeps its own copy in `~/.local/share/Nashi/Wallpapers` (the last five), so the wallpaper stays when the original is moved or deleted. HEIC, AVIF, TIFF and other formats are saved as PNG first.
- **Fit for Discord / WhatsApp:** videos larger than the upload limit (Discord 10 MB, WhatsApp 16 MB) get a petal that shrinks them under it in one go. The compress tools for images, videos and PDFs also have one-click **Fit for** targets: Discord, WhatsApp and email (25 MB).

## From the terminal

```sh
nashi convert --to jpg ~/Pictures/*.heic       # each new file is printed on its own line
nashi tool compress clip.mp4 --out ~/Videos     # tools that work with their saved defaults
nashi formats report.pdf                        # what a file can become, and which tools fit
nashi help
```

The terminal commands use the same engines and saved export defaults as the app, record their results in Library, and never change the originals. They exit with 0 on success, 1 on failure and 2 when the command is not valid.

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

**Ctrl+O** chooses files and **Ctrl+V** pastes files, file paths, links, images or text outside text fields. **Ctrl+Space** opens the wheel for the selected files: **Tab** switches between Tools, Convert and File actions, the arrow keys move, **Enter** chooses and **Escape** closes. **Ctrl+1/2** switch between Home and Library, **Ctrl+F** searches Library, **Ctrl+Shift+D** downloads a copied public link and **Delete** clears the selection on Home (the files themselves stay where they are), and the ✕ on a file preview removes just that one.

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
