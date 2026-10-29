# Isolated UI audit

Nashi's native application has an explicit audit mode for reviewing real views without manually dragging files. It starts before the normal application delegate/single-instance setup. It does not enable global hooks, start the updater, install tools, request Accessibility/Screen Recording permissions, or read the user's Library or system clipboard.

The Mac audit uses a temporary UserDefaults suite and generated temporary files. The Windows audit keeps generated fixtures and its private Library/preferences below the chosen output directory. Output must be empty or contain the audit's `.nashi-ui-audit` marker; unrelated directories are rejected.

## Run

macOS, from a GUI login session:

```sh
swift build --product Nashi
.build/debug/Nashi --ui-audit build/ui-audit-mac
.build/debug/Nashi --range-accessibility-smoke build/range-accessibility-mac
.build/debug/Nashi --file-artwork-smoke build/file-artwork-mac
```

An installed candidate app's `Contents/MacOS/Nashi` accepts the same flags. Prefer a packaged candidate for review of bundled resources. Use `--hold` on the Mac audit to inspect its final scene with Computer Use; `--keep-range-open` keeps the range fixture open after its checks.

Windows, from a GUI login session:

```powershell
dotnet build windows/Nashi.Windows -c Release
& .\windows\Nashi.Windows\bin\Release\net10.0-windows10.0.19041.0\win-x64\Nashi.exe --ui-audit build\ui-audit-windows
```

The regular Windows native smoke suite also runs the full audit, artwork checks and range accessibility checks. Its CI artifacts include the results from both native smoke passes.

On Mac, `python3 scripts/test-native-ui.py` runs the matrix, 18 adversarial audit-validator cases, range/artwork checks, invalid-argument checks and the output overwrite guard. It wraps a bare SwiftPM executable in a disposable QA bundle with the actual localization resources and framework; otherwise older macOS versions can silently fall back to English for native controls. This minimal QA bundle does not package or install optional tools. The source workflow publishes its `mac-native-ui` artifact. Pass `--app /absolute/path/to/Nashi.app/Contents/MacOS/Nashi` to review a full packaged candidate with the same script.

Both audit commands accept these optional filters:

```text
--scenes editor.crop-image,wheel.pdf-multiple
--appearance light|dark|both
--language en|tr|fr|both
```

Unknown scenes/options, empty selections, repeated scenes and invalid themes/languages fail. No filter means the complete matrix: **10 scenes × 2 appearances × 2 languages = 40 captures per platform**.

| Scene ID | Production surface |
| --- | --- |
| home.empty | Home with no selected input |
| home.selected | Home with one generated image |
| library | Image, text, PDF and missing-file entries |
| settings.general | General preferences |
| settings.formats | Format preferences |
| settings.updates | Update preferences, without checking for an update |
| wheel.pdf-multiple | Tools offered for two generated PDFs |
| editor.crop-image | Image crop at minimum supported editor size |
| editor.annotate-image | Annotation editor at minimum size |
| editor.frame-image | Frame editor after its asynchronous preview is ready |

Fixtures include long Turkish/Unicode filenames. The app's actual localization and theme are used. Editor probes only run in audit mode; normal SwiftUI rendering does not add geometry-reader traffic.

## Evidence

`manifest.json` has schema version 1, platform, `passed`, rendering limitations and a `captures` array. Every capture identifies scene, appearance, language, image, status and issues. A nonzero exit means at least one requested scene or setup check failed. Geometry checks cover visible bounds, footer controls, primary label centering and wheel text containment; both platforms reject blank renders.

The artwork report is `file-artwork.json`. Range reports are `mac-range-accessibility.json` / `windows-range-accessibility.json`; both include the actual production semantic controls and bounded-action tests. Mac range checks also replay pointer handlers directly; existing desktop smoke tests continue to exercise OS input separately.

These snapshots are **own-view renders**, not screen recordings. They do not prove live glass, native titlebars, real drag/modifier timing, system focus routing, or a full VoiceOver/Narrator session. Inspect a held candidate through Computer Use for those visible details and retain the existing native gesture/keyboard/export tests. Do not claim all 27 editors are visually covered by these initial ten scenes.

Do not run the audit as a replacement for engine tests. It verifies view readiness and layout; exported files still need the appropriate format, dimension, duration, metadata and original-preservation checks.
