# Linux feature parity and release gate

The target is the complete Nashi experience. This file explicitly separates implemented Linux operations from remaining parity work; catalog availability alone is not validation.

| Area | Linux adaptation | Remaining for full parity |
| --- | --- | --- |
| Core contract | Original 45-format / 27-tool catalog; original single/multi-file intersections | Verify every advertised route and failure mode on the Linux CI architectures |
| Home / Library | Original artwork, palette and copy; new Linux rendering; searchable persisted original Library model; file picker, file drop, clipboard files/text/PNG/links | Exact layout and window behavior comparison at all sizes; clipboard payload variations across desktops |
| Images | Original PhotoCodec/PhotoAdjustments; image conversion, resize/rotate/flip, compression targets; crop coordinates/drag; annotation tools; redaction; frame/collage; PDF creation | Compare shared frame/collage options and previews; validate multi-area blur/pixel redaction; OCR/face assistance; independent annotation move/resize and crop edge handles |
| Media engine | Original MediaEngine and options, original process execution and export transactions | Full Linux regression coverage of every encoder option |
| Media editor | Embedded distro libmpv; pause, seek, range fields/marking/playback, keyboard controls; range timeline, filmstrip/waveform and processed preview | Verify filmstrip/waveform, draggable range handles, processed previews and file-order controls on both Linux CI architectures |
| Documents | Original Windows DocumentConversion.cs compiled directly; original OfficeDocuments/Pages reconstruction; Poppler raster adapter; Tesseract OCR; RTFD materialization | Document route fixtures with LibreOffice/Deno; metadata/tag-preserving round trips |
| PDF | Shared split/merge/organize; thumbnail page ordering, duplication and independent rotation; ported safe compression algorithm; Poppler image exports and QR | Verify thumbnail organizer and independent duplicate rotations; richer PDF preview |
| Archives / subtitles | Original ArchiveEngine, DocumentEngine.Zip and SubtitleCodec | Keep existing archive security/size/cancellation checks passing |
| Metadata | Original MetadataService; system ExifTool resolution; write/remove options | Verify the ported metadata editor and image conversion tag preservation with system ExifTool |
| Web | Original WebDownload/WebPhotos; correct system FFmpeg directory; public image/media download types | Platform-specific result previews, Instagram photo selection and quality UI |
| File actions / Float | Copy/move/path/ZIP; basic always-on-top reusable file shelf; external file preview | Native share/terminal, drag-out/add/collapse/edge pinning, robust transactional move failure UI |
| Preferences | Shared PreferencesStore; appearance, language, output mode/folder, download folder, reveal results and configurable distinct drag chords | Format default profiles, wheel appearance/feedback and login/tray options |
| Global integration | Ctrl/Shift/Alt file-drop wheels; X11 Xdnd/modifier observer and cursor-positioned radial drop targets | Real file-manager X11 integration checks; global native Wayland drag behavior, global copied-link shortcut, window-placement wheel and tray recovery require further work |
| Updates | Windows updater is not used for Linux | Linux release metadata, trusted signatures, atomic install/recovery and end-to-end public release verification |
| Distribution | Self-contained x64/ARM64 builds; AppImage/tar/deb/rpm packaging and CI | Actual CI results for each package; ARM64 execution and installation/upgrade checks on Debian/Ubuntu/Fedora/Arch and GNOME/KDE; hosted CI has not started successfully |

## Evidence from the development machine

- Native x64 and cross-compiled ARM64 Linux builds succeed with warnings treated as errors.
- Launching the installed app from a second process forwards to the existing window; only one installed Nashi process remains. This was checked across separate command sessions on the development Wayland desktop.
- `linux/Nashi.Linux.Verify`: 36 checks, including real image dimensions/pixels, original hashes, PDF page counts/rendering, archive/subtitle round trips, QR, cancellation and real FFmpeg conversion/trim.
- `windows/Nashi.Verify` on Linux with system FFmpeg/ffprobe: 1,327 shared engine/security checks pass. This is existing Nashi code, not a claim about the Windows GUI running on Linux.
- `Nashi --smoke-test`: 25 assertions in real Avalonia Linux windows, covering Home/editor construction, PDF thumbnails, metadata values, libmpv playback/seek, URI filtering and Ctrl/Shift/Alt routed drag/drop events. Captures light/dark/compact/editor/wheel PNGs. The drag tests inject application events, not operating-system input; they do not establish real global file-manager integration. Only the emitted successful `smoke.json` is evidence of a completed run.

CI results, screenshots, package checksums and successful installation must be linked before promoting this branch to a feature-complete Linux release. Remaining cells above are release blockers, not optional differences to silently omit.
