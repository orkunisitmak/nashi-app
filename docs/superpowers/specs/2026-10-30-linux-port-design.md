# Nashi for Linux — design

## Intent

Ship a Linux desktop version of Nashi that people can download, contributed upstream to `orkunisitmak/nashi-app` through a pull request from a fork. The user wants "an interface like Nashi": the Linux app looks and behaves like the macOS app (palette, Home layout, conversion wheel, editors), not like a generic converter.

## Decisions

- **Look and behavior follow macOS** (`Sources/NashiApp`, `docs/assets/showcase/mac-*.png`, `DesignSystem.swift` palette).
- **Engine is the existing .NET `windows/Nashi.Core`.** It already uses cross-platform libraries (Magick.NET, PDFsharp, PdfPig, ZXing, SharpCompress). Baseline: `dotnet run --project windows/Nashi.Verify -c Release` passes 1,387 checks on Ubuntu 22.04 unchanged. The user worried about trusting the Windows code, so the Linux PR must run those checks on Linux in CI.
- **UI toolkit: Avalonia 11** (my call): closest to WPF, runs on X11 and Wayland (XWayland).
- **Distribution: AppImage, x86_64** (my call): one file that runs on most distributions.
- **Windows behavior must not change.** Core edits are additive and gated by `OperatingSystem.IsLinux()`/`IsWindows()`, and Windows CI must stay green.
- **Shared contract:** `Resources/FeatureCatalog.json` (46 inputs, 27 tools, 276 routes) stays the source of truth. A tool the Linux build cannot run yet is hidden from the wheel, never shown broken.

## Milestones (each its own plan and PR-able state)

1. **Foundation + first usable app (this plan):** Core runs on Linux with Linux tool downloads, Linux CI, an Avalonia app with macOS-style Home, file intake (Choose files, drag from file manager, Ctrl+V), a tools/conversion wheel (Ctrl+Space), real conversions for images/PDF/media/archives/subtitles, one-click tools (compress, mute, normalize, extract, create/merge PDF), media tool setup, a progress toast with cancel, light/dark, EN/TR, and an AppImage build.
2. **Image editors:** crop, edit photo, annotate, add BG, collage, redact, QR, metadata.
3. **Media and PDF editors:** trim/split/crop/speed/snapshots/redact/visualizer/channels/join; split and organize PDF.
4. **The rest:** Office and document conversion (system LibreOffice), web download, Library, Settings, Float shelf, signed updates.

## Non-goals for milestone 1

Global drag gestures from the file manager with modifier keys (Wayland forbids global input capture), tray icon, auto-update, ARM64.

## Anti-goals

- Changing Windows or macOS behavior.
- Showing a tool in the wheel that fails when chosen.
- Unpinned or unverified downloads. Every upstream tool is pinned by URL and SHA-256, like Windows.
