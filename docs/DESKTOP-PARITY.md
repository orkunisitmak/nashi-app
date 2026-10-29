# Mac and Windows desktop parity

Both implementations use the same 46 input-format / 27-tool catalog. `scripts/check-feature-parity.py` checks conversion destinations, tool names and single/multi-file eligibility against the Swift source; it is not a substitute for running the tools.

| Workflow | Shared behavior |
| --- | --- |
| Home | Pear artwork, local-file drop card, selected-file chips with individual removal, three action tiles, quick conversion picks and recent history |
| Library | 50 persistent jobs/imports, search by file or action, missing-file status, preview, reuse and reveal; searching/previewing never deletes originals |
| Clipboard | Files **and folders**, Unicode text with line breaks, full-size PNG images, HTTP(S) links into the download flow; editable fields retain ordinary paste |
| Preview | Library and Float batch navigation, images, paged PDFs/documents, selectable text, audio/video; media Space play/pause and one-second seeking; Escape closes |
| File wheels | Convert/tools/actions while dragging, centered complete captions, 320-point/DIP default diameter, adjustable size, sound feedback, subtle animation and keyboard selection |
| Images | Format conversion, resize/rotate, crop, adjustments/effects, annotation, backgrounds, collages, redaction, image PDFs and metadata |
| Crop | Drag a new region; Shift makes a square in Free mode, or preserves an existing selection's ratio when resizing; presets, moving, resizing and Enter to save |
| Media | Conversion, trim, crop, speed, mute, joins/splits, snapshots, normalization, channels, visualization and timed redaction; timeline/waveform, selected-range playback and processed audio/redaction preview |
| Precise media keys | Arrows seek 1 second; Shift+arrows use the source video FPS or 0.1 seconds for audio; I/O set trim range; Shift+Space plays the selected range |
| PDF/documents | Merge, split, reorder/duplicate/rotate pages, uniform widths, compression, QR reading, editable/preserved document layouts, spreadsheets, EPUB and Pages interoperability |
| Archives/subtitles | ZIP/TAR/GZIP/RAR workflows and SRT/VTT conversion; original files retained and output collisions handled safely |
| Float/actions | Compact paperclip stack; hover/click opens a slim thumbnail shelf; stack carries all files, clip moves the shelf; multi-selection, preview, copy/path, reveal/share, icon/list modes and edge attachment; removal never deletes originals |
| Window management | Eight layouts: thirds, halves, two-thirds and Maximize; actual title-bar/resize drag, target preview, release placement and Escape cancellation; usable display work area; app minimum widths keep the selected left/center/right anchor |
| Progress/results | Borderless progress/success notices, cancellable work, real progress where measurable and indeterminate state otherwise; success fades after three seconds |
| Settings/updates | Appearance, feedback, motion, drag chords, format profiles, output/download locations, login launch, automatic-check preference, visible update banner and verified public GitHub updates; Library/imports live outside the app bundle |

## Shortcut mapping

| Action | macOS | Windows |
| --- | --- | --- |
| Home / Library | Command+1 / Command+2 | Ctrl+1 / Ctrl+2 |
| Find in Library | Command+F | Ctrl+F |
| Choose files | Command+O | Ctrl+O |
| Paste content | Command+V | Ctrl+V |
| Open selected-file tools | Return / Command+Return | Enter / Ctrl+Enter; Ctrl+Space remains supported |
| Library result | Up/Down, Space preview, Return reuse | Up/Down, Space preview, Enter reuse |
| Download copied link | Control+Shift+D | Ctrl+Shift+D |
| Drag: convert / tools / actions | Shift / Command / Control | Shift / Ctrl / Alt |
| Arrange a dragged window | Shift or Command | Shift or Alt |
| Preview batch / media | Arrows navigate files; for media arrows seek, Option+arrows change files | Arrows navigate files; for media arrows seek, Alt+arrows change files |
| Settings / updates | Command+, / Command+U | Ctrl+, / Ctrl+U |

Drag chords are configurable. Text entry keeps normal editing shortcuts; tool shortcuts are scoped to their own active editor. Library search Down moves focus into its matching results.

## Native platform differences

- AirDrop is an Apple service. Windows uses the native Windows Share dialog; available destinations depend on the computer's installed share providers. File sharing is available on both platforms.
- Mac uses Quick Look/AVFoundation and Sparkle; Windows uses WPF, a pinned CPU PDFium renderer, Windows media APIs, local conversion previews and an Ed25519-verified update helper. PDF form appearances remain visible in read-only previews; previews do not execute document scripts or open link actions. Unsupported Windows media codecs use a local FFmpeg preview proxy. Office previews require the same optional private office runtime as Windows document conversion; originals stay unchanged.
- macOS window control needs the user's system permission. Windows normally uses WinEvent/window APIs, but elevated/protected applications can reject resizing. Both platforms report refused placements.
- Native fonts, title-bar controls, file pickers and system sharing dialogs follow the operating system. App page structure, palette, actions and keyboard behavior are shared.
- HTML to PDF: Mac prints the page with the system WebKit engine and needs no office runtime. Page scripts run, so script-drawn content such as e-invoice QR codes prints, but without WebRTC, camera, microphone or location access. Only regular files inside the page's folder load, and every remote resource is blocked. Windows converts sanitized HTML with the optional office runtime, which first removes scripts, linked files and remote resources, so script-drawn QR codes are missing there.
- Authenticated/protected web media and upstream service availability are not guaranteed by either version.

## Reproducible verification

- `python3 scripts/check-feature-parity.py`
- `swift test` (includes real image/document/media exports; live web-service checks are opt-in)
- Installed signed Mac app: `--desktop-parity-smoke <output-directory>` runs real keyboard input against a disposable Home/Library fixture, a private pasteboard and isolated defaults. It does not replace the user's Library or clipboard. It records screenshots and a JSON report.
- Installed Mac app: `--window-management-smoke <manager-pid> <output-directory>` tests real window drags against the bounded cross-process fixture described in [WINDOW-MANAGEMENT.md](WINDOW-MANAGEMENT.md).
- `dotnet run --project windows/Nashi.Verify -c Release` checks engine safety, file transactions, formats, source FPS and real FFmpeg operations when tools are configured.
- Windows executable: `--smoke-test` runs native SendInput/OLE/WPF checks, real OS clipboard imports, Library restart/search/reuse, preview navigation, crop/annotation/export, media controls and processed playback, original-file integrity, Explorer gestures and all eight window layouts. GitHub's Windows runner uploads screenshots, errors and JSON results.

Windows native verification runs on an isolated Windows runner. This is not a claim of a physical Windows Computer Use session or listening to audio on every hardware configuration. Regression tests cover additional display geometries; not every display/codec/share-provider configuration has been exercised on physical hardware.


## Published 0.7.5 verification

[Mac and Windows 0.7.5](https://github.com/orkunisitmak/nashi-app/releases/tag/v0.7.5-36) are published. The Mac feed advances from build 33 to build 36.

- Mac: 273 source tests passed, two opt-in live-web cases skipped; the exact signed public package passed 38 native desktop checks and 18 physical normal/minimum-width window gestures.
- Windows: 198 engine checks and two complete native runs of 231 assertions, both actual OS exit code 0. [Release-code evidence](https://github.com/orkunisitmak/nashi-app/actions/runs/39454458596).
- Web demo: 41 checks passed, including the catalog, wheel, crop/annotation/trim controls, exports and mobile layout.
- The [real public update test](https://github.com/orkunisitmak/nashi-app/actions/runs/39458173367) passed 0.7.1.0 → 0.7.5.0 through the native update UI: anonymous signed download, replacement, restart, clean normal exit, unchanged Library/text/image/preferences, and 231 updated native assertions. Its disposable profile uses KeepInTray=false so X exits; the product default still stays in the tray.

Mac Computer Use's control bridge failed during this session; final interaction checks used the previously authorized bounded native input fixtures. Windows checks use a real isolated runner, not a physical Windows workstation. Hardware audio/haptics and every third-party application's resizing behavior remain outside this evidence.
