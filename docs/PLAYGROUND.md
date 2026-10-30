# Nashi interactive playground

The English and Turkish landing pages use an independent implementation of Nashi's drag-and-wheel interaction, inspired by Tangerine. No reference-site source, illustrations, audio or video is bundled.

## Interaction

- Drag a sample file, tap Shift (Convert), Command (Tools) or Control (Actions) on Mac; Ctrl opens Tools and Alt opens Actions on Windows, then drop on a wheel slice.
- Click a mode button, or focus a file and press Enter. Arrow keys, Home/End, Enter and Escape work in the wheel.
- Shift-click selects multiple files. Batch wheels use the intersection of the app catalog's formats and tools.
- Touch users tap a file, then a mode and a wheel option.
- All 46 input extensions and 276 conversion routes can be explored using the sample-input selector.
- All 27 tools have an interactive window. The feature filters show tools by file family.
- The wheel follows the native 320-point rounded geometry, including the area-centroid labels for three-, five- and eight-item menus. Conversion petals omit icons, and the center shows the hovered option. Tool names, Turkish translations, editor headings/hints/actions and the light/dark palette are generated from the desktop sources.
- Appearance switches only the interactive demo; the landing page keeps its own theme.
- Library stores the current visit's exports and explicitly labeled app previews. Float is a temporary shelf within the sample desktop: a thumbnail stack and paperclip expand into the narrow icon/list shelf, with multi-selection, Space preview, reference removal and drop-to-add. It does not expose local filesystem dragging from the browser.

## Real browser outputs

Images can export to PNG, JPEG, WebP and PDF. Image crop supports pointer selection, selection movement/resizing, native aspect-ratio segments, dimensions, Select All/Clear Selection, Shift ratio locking and Enter. It starts without a selection; saving is disabled until an area is selected. Edit, frame, annotation, collage, compression and metadata removal generate real image files. PDF splitting, page ordering/rotation and merging use the bundled PDF library. Audio trim, channel adjustment and bleep/silence output real PCM WAV. Video snapshots output PNG. Text, subtitle and delimited-table sample conversions support the formats implemented in `playground.js`.

Sample file content is illustrative. The visitor can import their own supported raster images and PDFs, up to 25 MB per file, 12 files and 24 megapixels per image. Animated image imports produce still-image exports. Files are processed in the current tab and are never uploaded. Closing the tab ends the session; original inputs are unchanged.

Video encoding, other desktop-only conversions, system actions, archive extraction, QR detection and web downloading are **labeled interactive previews**. They never create misleading media downloads or claim to access a local shell, Finder, AirDrop or remote social-media content. Copy Path copies a path from the demo desktop when clipboard access is available; otherwise it displays that path.

Space plays/pauses preview media; arrow keys seek one second, Shift+arrow seeks one video frame or 0.1 second for audio, I/O sets In/Out and Shift+Space plays the selection. The transport follows the native full-width scrubber, frame-step buttons and millisecond clock. Trim handles have independent pointer capture. Native form controls retain their normal keyboard behavior.

## Development and checks

```sh
python3 scripts/build-site.py
node scripts/test-web-presentation.mjs
python3 -m http.server 8771 --bind 127.0.0.1 --directory docs
# In another terminal, with Playwright installed and Chrome available:
node scripts/test-web-demo.cjs http://127.0.0.1:8771
```

The presentation check compares all 45 browser petals and every candidate label center directly against the compiled native `WheelGeometry.swift` on macOS. It also checks the shared native catalog, Turkish labels, palette and byte-identical wheel sound.

The browser suite uses a separate temporary Chrome profile. It covers real drag/modifier/drop, keyboard wheel navigation, every catalog conversion option, multiple PDFs, square crop dimensions, independent annotation tools, media keys and In/Out dragging, all tool result flows, valid PDF pages, actual WAV duration, malformed-PDF handling, Instagram action classification, file actions, English and Turkish pages, and a 390px viewport. Reports and screenshots are written under the ignored `build/web-demo-proof/` directory.

## Assets and privacy

`assets/demo/orchard.jpg` and `hills.jpg` are original programmatic Nashi landscape illustrations. `weekend.mp4` animates that artwork; `soft-morning.wav` is an original synthesized chord. These are not user recordings. The real application recording remains separately available in `assets/showcase/`.

PDF processing uses pdf-lib 1.17.1, distributed under the MIT license in `assets/vendor/pdf-lib.LICENSE.md`. Scripts, media and fonts are served locally; there are no analytics, remote fonts, third-party script requests or uploaded file contents. The opt-in wheel feedback uses the same `Resources/Sounds/WheelSelection.wav` as the app (a three-voice pool, 50 ms throttling, 0.65 volume); it is unrelated to reduced-motion settings. Audio feedback defaults off, and motion respects reduced-motion preferences.
