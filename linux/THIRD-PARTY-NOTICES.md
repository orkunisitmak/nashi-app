# Linux third-party notices

Nashi's original source is MIT (see the root LICENSE). The portable Linux build
includes separately licensed dependencies; they are not relicensed by Nashi.
Exact direct and transitive package versions are recorded in `packages.lock.json`
under `linux/` and `windows/Nashi.Core/`.

- [Avalonia](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md), including Fluent/Inter integration: MIT. The packaged Inter font is SIL Open Font License 1.1; see [Inter](https://github.com/rsms/inter/blob/master/LICENSE.txt).
- [.NET](https://github.com/dotnet/runtime): MIT, with third-party notices copied beside the Linux executable during packaging.
- [Magick.NET / ImageMagick](https://github.com/dlemstra/Magick.NET): Apache 2.0 / ImageMagick license and its native codec notices. See the repository's existing `windows/licenses/Magick.NET-THIRD-PARTY-NOTICES.txt` and upstream distribution notices.
- [PDFsharp](https://github.com/empira/PDFsharp): MIT.
- [Bouncy Castle](https://www.bouncycastle.org/licence.html): MIT.
- [SharpCompress](https://github.com/adamhathcock/sharpcompress): MIT.
- [PdfPig](https://github.com/UglyToad/PdfPig): Apache 2.0.
- [ZXing.Net](https://github.com/micjahn/ZXing.Net): Apache 2.0.
- [SkiaSharp / Skia](https://github.com/mono/SkiaSharp): MIT / BSD 3-Clause, with third-party notices.
- [HarfBuzzSharp / HarfBuzz](https://github.com/harfbuzz/harfbuzz): MIT integration / HarfBuzz's MIT-style license.
- [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus): MIT.

The build script includes the existing shared dependency license files as well
as these notices. Consult each package's upstream source for complete licensing
and native dependencies. Distribution-managed FFmpeg, yt-dlp, Deno and LibreOffice
are optional external executables; this port does not bundle their binaries.
Their distribution/upstream licenses apply separately. No Apple, Windows or
proprietary Tangerine runtime/artwork is bundled by the Linux port.
