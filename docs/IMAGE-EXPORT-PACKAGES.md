# Image export packages

On macOS and Windows, select images on Home and choose **Export image package**.
Each output row sets a format, longest edge in pixels and subfolder. Choose
**Export package…**, then select a destination folder.

The example package creates these outputs for every selected image:

| Subfolder | Format | Longest edge |
| --- | --- | --- |
| `web` | WebP | 1600 px |
| `thumbnail` | JPG | 400 px |
| `archive` | JPG | Original size |

Sizes preserve the aspect ratio and never enlarge the source. Enter `0` to keep
the original dimensions. PNG and WebP retain transparency; JPG composites it onto
white. Outputs omit the source's GPS, comments and other descriptive metadata.
Encoding uses Nashi's existing image engines. WebP on macOS can require FFmpeg
from **Finish setup**, as it does for ordinary WebP conversion.

The preview lists output names and the total file count. A new package folder
keeps every variant together. If that name already exists, a numbered folder is
created. Inputs with the same filename get numbered outputs within each variant.
Originals and earlier exports are kept. Cancellation or failure removes the
staged package. All successful outputs appear in one Library entry.

Use **Save package** to reuse a named package. Saving with the same name updates
its settings. **Delete saved package** removes the definition and keeps exported
files. Package names and subfolders are plain folder names, without paths or
Windows reserved names. A package allows 1–12 outputs, up to 1000 input images,
and size limits of 1–16384 pixels. Up to 50 named packages can be saved locally.
This is a native Home action; the conversion wheel and browser playground keep
their existing catalog.

## Verification

```sh
swift build --product Nashi
python3 scripts/test-image-export-packages.py
dotnet run --project windows/Nashi.Verify -c Release
```

The Mac runner checks real formats, dimensions, orientation, alpha, source
metadata, duplicate names, rollback and saved packages. It also runs production
Save/Export controls with isolated preferences, records a Library job and saves
six EN/TR/FR light/dark renders. It uses a checksum-pinned, test-only FFmpeg
from the imageio-ffmpeg 0.6.0 wheel when the selected FFmpeg lacks WebP support.
The encoder stays below the test output directory; it is not installed or bundled
with Nashi. No user clipboard, updater or global input hooks are started.

`Nashi.Verify` exercises the Windows package engine on supported host platforms.
On Windows, `Nashi --smoke-test` also runs native package validation, persistence,
Export/Cancel controls and EN/TR light/dark captures. A cross-build alone does
not verify those native controls.
