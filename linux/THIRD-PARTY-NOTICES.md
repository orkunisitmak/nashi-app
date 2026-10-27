# Nashi Linux third-party notices

Nashi's original source and pear artwork are distributed under the [repository MIT license](https://github.com/orkunisitmak/nashi-app/blob/main/LICENSE). This Linux port retains that attribution.

The self-contained package includes the .NET runtime (MIT) and Avalonia/Skia dependencies. NuGet libraries shared with Nashi Windows retain their licenses: Magick.NET / ImageMagick (Apache-2.0 / ImageMagick license), PDFsharp (MIT), PdfPig (Apache-2.0), SharpCompress (MIT), ZXing.Net (Apache-2.0), BouncyCastle (MIT) and System.Security.Cryptography.Pkcs (MIT). Package versions are recorded in the project files and lock files. Consult each package's included NuGet metadata for complete license text.

`PagesInterop.js` is the same vendored cupertino-files runtime used by the original project. Its complete license and provenance are included under `licenses/cupertino-files`.

FFmpeg, yt-dlp, Deno, LibreOffice, Poppler, Tesseract, ExifTool and libmpv are optional system dependencies, resolved from the Linux installation. They are not downloaded by the application or bundled in these packages. Their licenses and codec availability are governed by the distribution packages. Nashi's MIT license does not replace their licenses. libmpv is used dynamically for embedded playback; distribution builds may be GPL licensed.

AppImage build tooling is pinned to appimagetool 1.9.1 and type2-runtime 20251108 with official release SHA-256 digests. The AppImage runtime carries its upstream license; see https://github.com/AppImage/type2-runtime.
