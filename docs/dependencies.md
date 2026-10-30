# Runtime setup and third-party licenses

The public Nashi packages contain Nashi, the platform runtime where needed, and the open-source libraries listed in their notices. Large media/Office executables are downloaded **directly from upstream**, after the user chooses setup. They are not relicensed under Nashi’s MIT license. The pinned hashes in source are checked before a tool directory is published. A failed or cancelled setup keeps existing tools and removes its own staging files. No GitHub token or browser cookies are used.

## macOS

First launch opens setup after the welcome tour, including when the tour is skipped. Choose **Download missing tools** to install the missing Media and Office groups in sequence, or use each group's download button. The app does not report full readiness until both groups are available. Cancelled/failed downloads retain completed groups and can be retried with **Finish setup** in Home. Incomplete installations are repaired without asking users to remove folders.

Media tools enable video/audio conversion, media previews, less common image encoders and web downloads. Office tools enable Office conversions. Native image/PDF features can be used without those downloads. Readiness uses the same executable resolution as actual conversions: bundled tools, existing Nashi tools, and supported system locations (including `/Applications/LibreOffice.app`) do not require duplicate downloads.

- [FFmpeg / ffprobe 9.0 Apple Silicon, OSXExperts](https://www.osxexperts.net/), GPL 2.0 or later. Downloaded executables are matched against the upstream published SHA-256. [FFmpeg sources](https://github.com/FFmpeg/FFmpeg). This is an end-user acquisition, not a redistribution of the upstream build by Nashi.
- [LibreOffice 26.8.1, The Document Foundation](https://www.libreoffice.org/download/), MPL 2.0 and included third-party licenses. Official 285 MB DMG; SHA-256 pinned from its official mirror listing. The publisher’s app signature is retained. [Sources](https://www.libreoffice.org/about-us/source-code/).
- [yt-dlp 2026.08.19](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19), GPLv3+ standalone combination. [Sources and complete third-party licenses](https://github.com/yt-dlp/yt-dlp/tree/2026.08.19).
- [Deno 2.9.7](https://github.com/denoland/deno/releases/tag/v2.9.7), MIT and included dependency licenses. [Sources](https://github.com/denoland/deno/tree/v2.9.7).

Exact URLs, limits and hashes: [`ExternalToolSetup.swift`](../Sources/NashiCore/ExternalToolSetup.swift). Other bundled library notices: [`Resources/THIRD-PARTY-NOTICES.txt`](../Resources/THIRD-PARTY-NOTICES.txt). ExifTool includes its complete Perl source; Sparkle includes its full license.

## Windows x64 preview

The first foreground launch opens the same two-group setup as macOS. Choose **Download missing tools**; media tools and Office tools install in sequence. Reopening an unfinished setup retries only missing groups. Image, PDF and ZIP features work immediately. Setup uses these pinned upstream releases:

- [FFmpeg 9.0.2 full build, gyan.dev](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2), GPL. gyan.dev is listed by [FFmpeg’s official download page](https://ffmpeg.org/download.html). [Build information and sources](https://www.gyan.dev/ffmpeg/builds/).
- The same yt-dlp and Deno releases above, using their Windows x64 assets.

Exact URLs and hashes: [`Toolchain.cs`](../windows/Nashi.Core/Toolchain.cs). [Windows availability and shortcuts](../windows/README.md). [Bundled Windows library notices](../windows/THIRD-PARTY-NOTICES.txt). The preview is self-contained and does not require .NET to be installed separately.

## Linux x64 preview

Linux ships as an unsigned AppImage. Image, audio/video (with media tools), archive, subtitle, PDF and document conversions are described in [`linux/README.md`](../linux/README.md). The AppImage bundles PDFium 155.0.8044.0 for PDF rendering through [`bblanchon.PDFium.Linux` 155.0.8044](https://github.com/bblanchon/pdfium-binaries/releases/tag/chromium%2F8044) (Apache-2.0 packaging; PDFium BSD-3-Clause and bundled dependency licenses in [`PDFium-THIRD-PARTY-NOTICES.txt`](../linux/licenses/PDFium-THIRD-PARTY-NOTICES.txt)), the same PDFium build the Windows app pins. Office, text, EPUB and Pages conversions use the system LibreOffice (`soffice` on `PATH`, installed with the distribution's package manager); Nashi does not download or bundle it on Linux. Media setup downloads these pinned upstream releases (HTTPS only, SHA-256 verified before anything is extracted); an `ffmpeg`/`ffprobe` already on `PATH` is used instead of downloading:

- [FFmpeg / ffprobe 9.0.2 GPL build, BtbN FFmpeg-Builds `autobuild-2026-10-29-19-17`](https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-10-29-19-17), GPL. `ffmpeg-n9.0.2-37-gae19868753-linux64-gpl-9.0.tar.xz`, SHA-256 `83537b0f6a97905f325080f6f5916e70ec252d1ef3e4ce4746f755b88720cbf5`. The archive's `LICENSE` is kept next to the tools. [FFmpeg sources](https://github.com/FFmpeg/FFmpeg).
- [yt-dlp 2026.08.19, `yt-dlp_linux`](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19), GPLv3+ standalone combination, SHA-256 `58162f9bfdc27458ea47bfcb311cf47028f17d8154a8bf7d689861d46399230a`.
- [Deno 2.9.7, `deno-x86_64-unknown-linux-gnu.zip`](https://github.com/denoland/deno/releases/tag/v2.9.7), MIT and included dependency licenses, SHA-256 `c6527f24f4b16031d3ae4fa9f658d5f11534c8d84ce7dc8502420280919c3490`.
- [ExifTool 13.59, `Image-ExifTool-13.59.tar.gz`](https://exiftool.org/), Perl Artistic License or GPL, SHA-256 `668ea3acececb7235fbd0f4900e72d5f12c9b07e5c778fd36cb1e9b5828fd65a`. Includes its complete Perl source; needs `perl`.
- [appimagetool 1.9.0](https://github.com/AppImage/appimagetool/releases/tag/1.9.0), MIT, used only by the build script (SHA-256 `46fdd785094c7f6e545b61afcfb0f3d98d8eab243f644b4b17698c01d06083d1`); it is not shipped.

Exact URLs and hashes: [`Toolchain.cs`](../windows/Nashi.Core/Toolchain.cs), [`MetadataService.cs`](../windows/Nashi.Core/MetadataService.cs) and [`build-linux-appimage.sh`](../scripts/build-linux-appimage.sh). The AppImage is self-contained and carries all bundled library license texts in `usr/share/doc/nashi/licenses/` ([notices](../linux/THIRD-PARTY-NOTICES.txt)).

We do not change operating-system security settings or request administrator access for this setup. OS publisher trust and Nashi’s pinned update-signature verification are separate: the first public builds are not Apple-notarized or Windows Authenticode signed.
