# Nashi interactive playground

The English and Turkish landing pages use an independent implementation of Nashi's drag-and-wheel interaction, inspired by Tangerine. Nashi is a system-wide utility, so the demo has no Nashi window or panel: the hero itself is a small desktop: one wallpaper and menu bar (including Nashi's status item) holding the pitch on the left and a Finder-like folder on the right, with the native wheel opening over it. The wheel opens at the pointer while dragging, or over the selected file from the keyboard, the on-screen keys or touch. No reference-site source, illustrations, audio or video is bundled.

## Interaction

- Drag a sample file, tap Shift (Convert), Command (Tools) or Control (Actions) on Mac; Ctrl opens Tools and Alt opens Actions on Windows, then drop on a wheel slice.
- The mixed Sample files folder starts in icon view. Favorites and the back/forward controls move through demo folders. On phones, open Folders to show Favorites. Switch to list view or search the current folder by filename.
- Dragging shows a Finder-style drag image (artwork and name) under the pointer while the original stays dimmed; the wheel opens around it. The folder area has a fixed height and scrolls internally, so switching folders or views never moves the page.
- Three keycaps at the foot of the hero copy (Shift, Command and Control on Mac; Shift, Ctrl and Alt on Windows) open the Formats, Tools and Actions wheels for the selected file, and light up while the physical key is held. Choosing a format converts right away. Like the app, the fresh copy appears next to the original without replacing an existing name, and Nashi's completion toast offers Save with the byte sizes before and after conversion.
- A short guided gesture plays once when the desktop scrolls into view (never with reduced motion); any pointer, key or focus inside the demo stops it, and Show me replays it.
- Below the folder are quiet Show me, Other file types, Add your file, Library and Keyboard. Appearance, wheel sound, Reset, Choose Files…, Download Link… and Recent Conversions live in the Nashi menu bar item, mirroring the app's status menu. Focus a file and press Enter to use the wheel directly. Number keys choose slices clockwise from the top. Arrow keys, Home/End, Enter and Escape also work.
- Shift-click selects multiple files. Batch wheels use the intersection of the app catalog's formats and tools.
- Samples covers ten file families. The sample input selector and the searchable Supported file formats groups expose all 46 input extensions and 276 conversion routes.
- All 27 tools have an interactive window. The feature filters show tools by file family.
- The wheel uses the native light and dark treatments and 320-point rounded hit geometry, including the area-centroid labels for three-, five- and eight-item menus. Conversion slices omit icons. The center shows the hovered or focused option. Tool names, Turkish translations, editor headings/hints/actions and the palette are generated from the desktop sources.
- Appearance switches only the interactive demo; the landing page keeps its own theme.
- Library stores the current visit's exports and explicitly labeled app previews. Float is a temporary shelf within the sample desktop: a thumbnail stack and paperclip expand into the narrow icon/list shelf, with multi-selection, Space preview, reference removal and drop-to-add. It does not expose local filesystem dragging from the browser.

## Real browser outputs

Images can export to PNG, JPEG, WebP and PDF. Image crop supports pointer selection, selection movement/resizing, native aspect-ratio segments, dimensions, Select All/Clear Selection, Shift ratio locking and Enter. It starts without a selection; saving is disabled until an area is selected. Edit, frame, annotation, collage, compression and metadata removal generate real image files. PDF splitting, page ordering/rotation and merging use the bundled PDF library. Audio trim, channel adjustment and bleep/silence output real PCM WAV. Video snapshots output PNG.

Sample file content is illustrative. Visitors can add up to 12 raster images or PDFs to the Added files location. The limits are 25 MB per file and 24 megapixels per image. Animated image imports produce still-image exports. Files are processed in the current tab and are never uploaded. Closing the tab ends the session; original inputs are unchanged.

Sample format conversions beyond the four image exports, system actions, archive extraction, QR detection and web downloading are **labeled interactive previews**. Copy Path copies a path from the demo desktop when clipboard access is available; otherwise it displays that path.

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

`assets/demo/orchard.jpg` and `hills.jpg` are original programmatic Nashi landscape illustrations. `weekend.mp4` animates that artwork; `soft-morning.wav` is an original synthesized chord. The folded file icons are original to the playground and reuse the project’s format-family glyphs. The real application recording remains separately available in `assets/showcase/`.

PDF processing uses pdf-lib 1.17.1, distributed under the MIT license in `assets/vendor/pdf-lib.LICENSE.md`. Scripts, media and fonts are served locally; there are no analytics, remote fonts, third-party script requests or uploaded file contents. The opt-in wheel feedback uses the same `Resources/Sounds/WheelSelection.wav` as the app (a three-voice pool, 50 ms throttling, 0.65 volume); it is unrelated to reduced-motion settings. Audio feedback defaults off, and motion respects reduced-motion preferences.
