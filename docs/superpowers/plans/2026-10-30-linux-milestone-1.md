# Nashi for Linux — Milestone 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A downloadable Linux AppImage of Nashi with the macOS-style Home, file intake, a tools/conversion wheel and real conversions, built on the existing .NET engine and verified on Linux in CI.

**Architecture:** `windows/Nashi.Core` stays the shared engine; it gets additive, OS-gated changes so tool downloads and executable names work on Linux. A new Avalonia app `linux/Nashi.Linux` mirrors the macOS UI. Testable logic (selection, capabilities, image engine, operation dispatch) lives in plain classes, checked by `linux/Nashi.Linux.Checks`, a console runner in the same `Check(bool,string)` style as `windows/Nashi.Verify`.

**Tech Stack:** .NET 10, Avalonia 11.3 (Desktop, Fluent theme, Headless for checks), Magick.NET (already in Core), FFmpeg/ffprobe, appimagetool.

**Spec:** `docs/superpowers/specs/2026-10-30-linux-port-design.md`

## Global Constraints

- .NET SDK `10.0.x`. Repo-wide settings come from `windows/Directory.Build.props` (`TreatWarningsAsErrors`, `Nullable`, `RestorePackagesWithLockFile`). Linux projects import it from `linux/Directory.Build.props`, and every new project commits a `packages.lock.json`.
- No Windows behavior change. `dotnet run --project windows/Nashi.Verify -c Release` must pass on Linux after every Core task, and the Windows CI workflows are not edited.
- GitHub Actions use the exact SHAs in `.github/pinned-actions.json` (checkout `d23441a4…`, setup-dotnet `26b0ec14…`, upload-artifact `b7c566a7…`).
- Every downloaded upstream tool is pinned by HTTPS URL and SHA-256 through `VerifiedDownload.FetchAsync`. A hash mismatch installs nothing.
- Originals are never modified. Outputs go through `ExportTransaction` and get unique names via `SafePaths.Unique`.
- UI strings go through `AppLocalization.Text(source, language)`, `language ∈ {"en","tr"}`, defaulting from `CultureInfo.CurrentUICulture`. A new English string with no Turkish entry is added to `Sources/NashiCore/Resources/Turkish.json`.
- Palette (sRGB 0–1, light / dark), copied from `Sources/NashiApp/DesignSystem.swift`: canvas `(0.976,0.965,0.930)/(0.065,0.100,0.079)`; surface `(1,0.993,0.967)/(0.110,0.155,0.122)`; inset `(0.945,0.930,0.880)/(0.082,0.121,0.094)`; forest `(0.19,0.33,0.23)/(0.70,0.82,0.62)`; sand `(0.965,0.906,0.746)/(0.235,0.205,0.130)`; sage `(0.877,0.921,0.824)/(0.140,0.221,0.156)`; goldInk `(0.44,0.31,0.09)/(0.91,0.77,0.46)`; buttonInk `(0.17,0.21,0.11)`; goldEnd `(0.96,0.82,0.49)`; gold is `NashiArtwork.gold` (read the value from `Sources/NashiApp/NashiArtwork.swift`). Card radius is 22, button radius 13, and primary buttons use a gold→goldEnd horizontal gradient.
- Home copy matches `docs/assets/showcase/mac-home.png`, with "Mac" changed to "Linux"/"computer": "Your files. Their next form." / "Convert, fine-tune, and send them on their way." / "START HERE" / "Drop something good." / "Images, videos, documents, and all the little things in between." / "Choose files" / "Download a link" / "A little less file fuss. A little more flow." / pill "On your computer".

## Review Focus

1. **File names with spaces, Turkish characters (ğ, ş, ı, İ) or leading dashes.** Conversions must work and must not treat a name as a command-line flag. Owner: Task 5 and Task 6 checks use `"-ğüş photo.png"`.
2. **The same file dropped twice, a folder, or a vanished file in the selection.** Dedupe, ignore folders, and drop missing files with a status message. Owner: Task 3.
3. **Mixed selection (image + video).** The wheel offers only formats and tools valid for every file (`FeatureCatalog.Formats/Tools` intersection, then the Linux-supported filter). An empty wheel shows a status message, never an empty ring. Owner: Task 4.
4. **Cancel mid-conversion.** No partial output is left in the destination folder. Owner: Task 6.
5. **Media chosen before tools are set up.** Show a clear "Set up tools" prompt instead of a crash. Owner: Task 6, with `MissingToolsException` from the runner.

---

### Task 1: Core works with Linux tool names and Linux downloads

**Files:**
- Modify: `windows/Nashi.Core/ToolSetupStatus.cs` (add to `ToolInstallation`)
- Modify: `windows/Nashi.Core/Toolchain.cs` (names, package list, executable bit)
- Modify: `windows/Nashi.Core/MetadataService.cs` (Linux ExifTool)
- Modify: `windows/Nashi.Verify/ToolSetupChecks.cs`
- Modify: `windows/Nashi.Verify/Program.cs` (only if needed to call the new checks)

**Interfaces:**
- Produces: `ToolInstallation.ExecutableName(string name) -> string` (`name + ".exe"` on Windows, otherwise `name`); `ToolInstallation.MarkExecutable(string path) -> void` (no-op on Windows, `UserRead|UserWrite|UserExecute|GroupRead|GroupExecute|OtherRead|OtherExecute` elsewhere). `Toolchain` and `MetadataService.Executable` behave identically on Windows.

- [ ] **Step 1: Write the failing checks** in `ToolSetupChecks.Run`. Replace the hard-coded `name+".exe"` in the PATH fixtures with `ToolInstallation.ExecutableName(name)`, then add:
  - `check(ToolInstallation.ExecutableName("ffmpeg")==(OperatingSystem.IsWindows()?"ffmpeg.exe":"ffmpeg"),"Tool names must follow the platform")`
  - On non-Windows: write a 4-byte file, call `MarkExecutable`, and check `File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute)`.
  - On non-Windows: a `Toolchain(bundled, installedDir)` whose `installedDir` contains `ffmpeg`, `ffprobe`, `yt-dlp` and `deno` without extensions reports `Ready`, and `Executable("ffmpeg")` ends with `/ffmpeg`.
- [ ] **Step 2: Run the checks and confirm they fail.** `~/.dotnet/dotnet run --project windows/Nashi.Verify -c Release` exits non-zero with the "Tool names" message.
- [ ] **Step 3: Implement.** Use `ExecutableName` everywhere `Toolchain` builds a path. Choose the package list by OS: the Windows list stays byte-for-byte; the Linux list is:
  - FFmpeg: a versioned BtbN `linux64-gpl` 9.0 `.tar.xz` from a dated `autobuild-*` release tag. It contains `bin/ffmpeg` and `bin/ffprobe`. Extract with SharpCompress, which Core already references.
  - yt-dlp: `https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp_linux`
  - Deno: `https://github.com/denoland/deno/releases/download/v2.9.7/deno-x86_64-unknown-linux-gnu.zip`

  Download each one, record its SHA-256 in the code, and call `MarkExecutable` on every extracted or moved tool. `MetadataService` on Linux uses `https://downloads.sourceforge.net/project/exiftool/Image-ExifTool-13.59.tar.gz` (pinned SHA-256). Its executable is `metadata-13.59/exiftool`, a Perl script run directly with its executable bit set. The extraction path keeps the existing traversal checks: go through `ArchiveEngine.Extract`, or reject entries outside the stage directory.
- [ ] **Step 4: Run** `~/.dotnet/dotnet run --project windows/Nashi.Verify -c Release`. Expected: `{"passed":N,...}` with N > 1387. Then run `NASHI_VERIFY_DOWNLOAD_TOOLS=1 NASHI_VERIFY_TOOLS_DIRECTORY=$(mktemp -d) ~/.dotnet/dotnet run --project windows/Nashi.Verify -c Release`. Expected: it passes, which means the real Linux downloads verified and the media exports ran.
- [ ] **Step 5: Commit** `feat(core): Linux tool names and pinned Linux tool downloads`.

### Task 2: Linux CI workflow

**Files:**
- Create: `.github/workflows/linux.yml`

**Interfaces:** Consumes Task 1. Later tasks append steps to this file.

- [ ] **Step 1: Write the workflow** `name: Linux preview`. Use the same triggers, `permissions: contents: read` and concurrency pattern as `windows.yml` (group `linux-${{ github.ref }}`). Use `runs-on: ubuntu-24.04` and `timeout-minutes: 25`, with checkout (`persist-credentials: false`) and setup-dotnet `10.0.x` at the pinned SHAs. Steps: `dotnet restore windows/Nashi.Verify --locked-mode`, then `dotnet run --project windows/Nashi.Verify -c Release` with `NASHI_VERIFY_DOWNLOAD_TOOLS: '1'` and `NASHI_VERIFY_TOOLS_DIRECTORY: ${{ runner.temp }}/Nashi-Test-Tools`.
- [ ] **Step 2: Validate locally.** Run `python3 -c "import yaml,sys;yaml.safe_load(open('.github/workflows/linux.yml'))"`, and if `scripts/` contains a pinned-action or workflow audit, run it. Expected: no errors.
- [ ] **Step 3: Commit** `ci: run engine checks on Linux`.

### Task 3: Linux app project, design system and Home

**Files:**
- Create: `linux/Directory.Build.props` (imports `../windows/Directory.Build.props`)
- Create: `linux/Nashi.Linux/Nashi.Linux.csproj` (WinExe, `net10.0`, `RuntimeIdentifier linux-x64`, `SelfContained true`, `AssemblyName Nashi`, references `../../windows/Nashi.Core`, Avalonia.Desktop + Avalonia.Themes.Fluent 11.3.x, brand PNGs from `Resources/Brand/` as `AvaloniaResource`)
- Create: `linux/Nashi.Linux/Program.cs`, `App.axaml(.cs)`, `Theme/Palette.axaml` (light/dark `ThemeDictionaries` with the Global Constraints colors), `Theme/Controls.axaml` (classes `card`, `primary`, `secondary`, `quiet`, `pill`)
- Create: `linux/Nashi.Linux/MainWindow.axaml(.cs)` (the Home layout from `mac-home.png`: header with mark, "Nashi", "Set up tools" button, "On your computer" pill and settings icon; hero title and subtitle; Start card with Choose files and Convert/Tools/Actions chips; "Download a link" card, disabled with a "Coming soon" tooltip in this milestone; footer)
- Create: `linux/Nashi.Linux/Selection.cs`
- Create: `linux/Nashi.Linux.Checks/Nashi.Linux.Checks.csproj` (Exe, references Nashi.Linux and Avalonia.Headless) and `Program.cs`

**Interfaces:**
- Produces: `public sealed class Selection` with `IReadOnlyList<string> Files`, `AddResult Add(IEnumerable<string> paths)`, `void Remove(string path)`, `void Clear()`, `event Action? Changed`; `public sealed record AddResult(int Added, int Skipped)`. Paths are stored as `Path.GetFullPath`. Only existing regular files are added, without duplicates, preserving order.
- Produces: Linux checks runner `linux/Nashi.Linux.Checks/Program.cs` with `Check(bool,string)` that prints `{"passed":N,"scope":"Linux app"}` and exits 1 on failure. Later tasks add their checks here.

- [ ] **Step 1: Write the failing checks.** In a temp folder with `a.png`, `"-ğüş photo.png"` and a subfolder `dir`:
  - `Add([a, a, dir, missing, ş])` returns `AddResult(2,3)` and `Files == [full(a), full(ş)]`.
  - `Remove(a)` leaves 1. `Changed` fires once per mutating call.
  - A headless render of `MainWindow` at 820×780 contains a `TextBlock` with "Your files. Their next form." and a `Button` with "Choose files", and its `Background` resolves to canvas light `#F9F6ED` (±1 per channel). Save the PNG to `$NASHI_SMOKE_DIRECTORY/home-light.png` when that variable is set.
- [ ] **Step 2: Run** `~/.dotnet/dotnet run --project linux/Nashi.Linux.Checks -c Release`. Expected: it fails to build or fails the checks.
- [ ] **Step 3: Implement** `Selection`, the theme and the Home window. Choose files uses `StorageProvider.OpenFilePickerAsync(AllowMultiple=true)`. The window accepts `DragDrop` file drops and Ctrl+V (`Clipboard.GetDataAsync(DataFormats.Files)`). Once files are selected, the Start card shows their names as removable chips plus a status line.
- [ ] **Step 4: Run the checks.** Expected: they pass. Run `~/.dotnet/dotnet run --project linux/Nashi.Linux` and confirm the window looks like `mac-home.png`, then attach a screenshot to the task report.
- [ ] **Step 5: Add a CI step** in `linux.yml`: restore both Linux projects `--locked-mode`, run the Linux checks with `NASHI_SMOKE_DIRECTORY`, and upload that folder with upload-artifact. **Commit** `feat(linux): Avalonia app with macOS-style Home and file intake`.

### Task 4: What the wheel offers on Linux

**Files:**
- Create: `linux/Nashi.Linux/Capabilities.cs`
- Modify: `linux/Nashi.Linux.Checks/Program.cs`

**Interfaces:**
- Produces: `public static class Capabilities` with `static readonly IReadOnlySet<string> SupportedTools = {"compress","muteVideo","normalizeAudio","extractArchive","createPDF","mergePDF"}`; `static string[] Formats(IReadOnlyList<string> files)` = `FeatureCatalog.Formats(files)` minus formats that need Office (`docx`,`pptx`,`xlsx`,`pages`,`epub`,`rtf`,`html`,`md`,`txt`); `static string[] Tools(IReadOnlyList<string> files)` = `FeatureCatalog.Tools(files)` ∩ `SupportedTools`, in catalog order.

- [ ] **Step 1: Write the failing checks.**
  - `Formats([x.png])` contains `jpg`, `webp` and `pdf`, and not `png`.
  - `Tools([x.png])` is `["compress"]`.
  - `Tools([x.mp4])` contains `compress` and `muteVideo`.
  - `Formats([x.png, y.mp4])` and `Tools([x.png, y.mp4])` contain no item outside the catalog intersection.
  - `Tools([a.pdf, b.pdf])` contains `mergePDF`.
  - `Tools([])` is empty.
- [ ] **Step 2: Run the checks and confirm they fail. Step 3: Implement. Step 4: Run the checks and confirm they pass. Step 5: Commit** `feat(linux): Linux wheel capabilities`.

### Task 5: Linux image engine (Magick.NET)

**Files:**
- Create: `linux/Nashi.Linux/ImageOperations.cs`
- Modify: `linux/Nashi.Linux.Checks/Program.cs`

**Interfaces:**
- Consumes: `PhotoCodec.Read(path) -> MagickImage` (Core; handles HEIC/AVIF/SVG and orientation), `ExportTransaction`, `SafePaths.Unique`.
- Produces: `public static class ImageOperations` with `IReadOnlyList<string> Convert(IReadOnlyList<string> inputs, string format, string destination, IProgress<OperationProgress>? progress, CancellationToken token)`, where format is one of `png,jpg,webp,heic,avif,bmp,gif,tiff,pdf` (pdf: one multi-page PDF named after the first input, `+ ".pdf"`); `IReadOnlyList<string> Compress(IReadOnlyList<string> inputs, string destination, IProgress<OperationProgress>?, CancellationToken)` (same format, quality 70, metadata stripped, suffix `" Compressed"`; keep the original bytes when the result is not smaller).

- [ ] **Step 1: Write the failing checks.** Start from a 64×48 gradient PNG named `"-ğüş photo.png"`.
  - Converting to each of the 9 formats produces exactly one file in the destination, with the right extension, that `PhotoCodec.Read` opens at 64×48 (for pdf, check the PDF page count is 1 with PdfPig).
  - Converting two inputs to pdf produces one file with 2 pages.
  - Compress on a 1000×1000 noisy JPG produces a smaller file.
  - The source file hash is unchanged.
  - A pre-cancelled token throws `OperationCanceledException` and leaves the destination empty.
- [ ] **Step 2: Run the checks and confirm they fail. Step 3: Implement** with `ExportTransaction` (stage, then `Publish`). **Step 4: Run the checks and confirm they pass. Step 5: Commit** `feat(linux): image conversion and compression`.

### Task 6: Operation runner

**Files:**
- Create: `linux/Nashi.Linux/OperationRunner.cs`
- Modify: `linux/Nashi.Linux.Checks/Program.cs`

**Interfaces:**
- Consumes: Tasks 4 and 5, `MediaEngine(ffmpeg,ffprobe).ConvertAsync/ToolAsync(…, new MediaOptions(), …)`, `DocumentEngine.Pdf(tool, inputs, destination, token: t)` (catalog `mergePDF` maps to `"mergePDFs"`), `ArchiveEngine.Extract/Convert`, `SubtitleCodec.Convert`, `Toolchain`.
- Produces: `public sealed class OperationRunner(Toolchain tools)` with `Task<IReadOnlyList<string>> RunAsync(string choiceId, IReadOnlyList<string> files, string destination, IProgress<OperationProgress> progress, CancellationToken token)`. `choiceId` is `"convert:<format>"` or a tool id from `Capabilities.Tools`. It throws `MissingToolsException` (new, in the same file) when media work needs tools that are not `Ready`, and `NotSupportedException` for anything outside `Capabilities`.

- [ ] **Step 1: Write the failing checks.**
  - `convert:jpg` on a PNG returns one `.jpg`.
  - `mergePDF` on two 1-page PDFs (make them with `ImageOperations.Convert(...,"pdf")`) returns one 2-page PDF.
  - `extractArchive` on a ZIP with `"ğ.txt"` extracts it.
  - `convert:vtt` on an SRT returns a `.vtt` that starts with `WEBVTT`.
  - `convert:mp3` with a `Toolchain` pointed at an empty folder and empty search path throws `MissingToolsException`.
  - When the env vars `NASHI_VERIFY_FFMPEG`/`NASHI_VERIFY_FFPROBE` are set, `convert:mp3` on a 1-second ffmpeg-generated WAV returns an `.mp3`, and `muteVideo` on a generated MP4 returns a video with no audio stream (check with `InspectAsync`).
  - Cancelling after start leaves no file in the destination.
- [ ] **Step 2: Run the checks and confirm they fail. Step 3: Implement. Step 4: Run the checks and confirm they pass** (also with `NASHI_VERIFY_FFMPEG=/usr/bin/ffmpeg NASHI_VERIFY_FFPROBE=/usr/bin/ffprobe`). **Step 5: Commit** `feat(linux): operation runner`.

### Task 7: Wheel and progress toast, wired to Home

**Files:**
- Create: `linux/Nashi.Linux/Wheel.cs` (custom `Control` that draws a ring like `docs/assets/showcase/mac-wheel.png`: up to 8 segments per page, a center label showing the highlighted item, a gold highlight, and Next/Previous pages beyond 8)
- Create: `linux/Nashi.Linux/WheelWindow.axaml(.cs)` (borderless, transparent, centered over the main window)
- Create: `linux/Nashi.Linux/ProgressToast.axaml(.cs)` (title, progress bar from `OperationProgress.Fraction`, message, Cancel; on success "Saved N new file(s). Originals kept." with "Show in folder" via `xdg-open` on the folder; auto-close after 4 s)
- Modify: `MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `Capabilities`, `OperationRunner`, `FeatureCatalog.ToolNames`.
- Produces: `public sealed record WheelItem(string Id, string Title)`; `Wheel.Items`, `Wheel.HighlightedIndex`, `event Action<WheelItem>? Chosen`; `static int Wheel.IndexAt(Point p, Size size, int count)` returning -1 inside the center or outside the ring (pure geometry, checked).

- [ ] **Step 1: Write the failing checks.** For size 320, 8 items: `IndexAt` at the top-center of the ring returns 0, at the right-middle returns 2, at the center returns -1 and outside the ring returns -1. Headless: Right/Left move `HighlightedIndex` with wrap-around, Enter raises `Chosen` with the highlighted item, Esc closes the window. Save a PNG of the wheel.
- [ ] **Step 2: Run the checks and confirm they fail. Step 3: Implement** and wire it up. The Tools chip and Ctrl+Space open the tools wheel (`Convert` first when `Formats` is not empty). The Convert chip opens the format wheel. Choosing runs `OperationRunner` into the first file's folder (writability is checked; otherwise a folder picker opens), showing the toast. `MissingToolsException` opens the tool setup prompt (Task 8). An empty wheel shows a status message instead.
- [ ] **Step 4: Run the checks and confirm they pass**, then convert a real PNG→JPG in the running app. **Step 5: Commit** `feat(linux): conversion wheel and progress`.

### Task 8: Tool setup window

**Files:**
- Create: `linux/Nashi.Linux/ToolSetupWindow.axaml(.cs)`
- Create: `linux/Nashi.Linux/AppPaths.cs`

**Interfaces:**
- Produces: `public static class AppPaths` with `string Data` (= `$XDG_DATA_HOME/Nashi`, or `~/.local/share/Nashi`), `string Tools` (= `Data/tools`), `Toolchain CreateToolchain()` (= `new Toolchain(Tools, Environment.GetEnvironmentVariable("PATH"))`, so system ffmpeg is reused).

- [ ] **Step 1: Write the failing checks.** With `XDG_DATA_HOME=/tmp/x`, `AppPaths.Tools == "/tmp/x/Nashi/tools"`. With it unset, the path ends with `.local/share/Nashi/tools`.
- [ ] **Step 2: Run the checks and confirm they fail. Step 3: Implement** the window: Media tools status (Ready / Missing), a "Download missing tools" button that runs `InstallAsync` with progress and Cancel, error text on failure, and a Retry. The header's "Set up tools" button opens it, and it shows "Tools ready" when complete.
- [ ] **Step 4: Run the checks and confirm they pass. Step 5: Commit** `feat(linux): media tool setup`.

### Task 9: Language and appearance

**Files:**
- Create: `linux/Nashi.Linux/L10n.cs`
- Modify: `Sources/NashiCore/Resources/Turkish.json` (only new strings)
- Modify: all Linux `.axaml` files to use `L10n` (a markup extension `{l:T 'English text'}`)

**Interfaces:**
- Produces: `public static class L10n { string Language {get;set;} string T(string english); }`. The default comes from `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName` (`tr` → `tr`, else `en`). The env var `NASHI_LANGUAGE` overrides it.

- [ ] **Step 1: Write the failing checks.** With `Language="tr"`, `T("Choose files")` is not "Choose files". Every literal English string in the Linux `.axaml`/`.cs` UI files (scan for the `{l:T '…'}` markup and `L10n.T("…")` calls) has a Turkish entry. With the language set to tr, a headless Home render contains no English hero title.
- [ ] **Step 2: Run the checks and confirm they fail. Step 3: Implement** `L10n`. Theme follows the system (`RequestedThemeVariant = Default`). Dark mode uses the dark palette. **Step 4: Run the checks and confirm they pass** (render `home-dark.png` too). **Step 5: Commit** `feat(linux): Turkish and dark appearance`.

### Task 10: AppImage, docs and release artifact

**Files:**
- Create: `scripts/build-linux-appimage.sh`
- Create: `linux/packaging/nashi.desktop`, `linux/packaging/AppRun`
- Create: `linux/README.md`
- Modify: `README.md`, `README.tr.md` (a Linux download bullet and a build line), `.github/workflows/linux.yml` (build and upload `Nashi-Linux-x86_64.AppImage` plus its `.sha256`)

**Interfaces:** Consumes everything above. The script output is `build/linux/Nashi-Linux-x86_64.AppImage`.

- [ ] **Step 1: Write the script.** `dotnet publish linux/Nashi.Linux -c Release -r linux-x64 --self-contained -o build/linux/AppDir/usr/lib/nashi`; copy `AppRun`, `nashi.desktop` and `Resources/Brand/NashiIcon.png` as `nashi.png`; copy `LICENSE` and the license notices into `usr/share/doc/nashi`. Download appimagetool from a pinned release URL with a pinned SHA-256, verify it, then run it with `ARCH=x86_64`. Write `sha256sum` output next to the AppImage.
- [ ] **Step 2: Run** `bash scripts/build-linux-appimage.sh && ./build/linux/Nashi-Linux-x86_64.AppImage --version`. Expected: it prints `0.7.12`; this needs a `--version` flag in `Program.cs`. Launch it once without flags, convert a file, and confirm it works.
- [ ] **Step 3: Write the docs.** `linux/README.md`: what works in this milestone, what is coming, build steps, and that it is unsigned. In `README.md`/`README.tr.md`, add the Linux bullet linking `releases/latest/download/Nashi-Linux-x86_64.AppImage` and marked "preview".
- [ ] **Step 4: Run the checks and both verify runners one last time**, then **commit** `feat(linux): AppImage packaging and docs`.
