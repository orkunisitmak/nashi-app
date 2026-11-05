# Fluent Icons for Nashi Windows

This directory contains a curated selection of **Microsoft Fluent UI System Icons (Regular, 24px)**.

- Source: [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons)
- License: [MIT](LICENSE)
- Exact revision, source URLs, hashes, and semantic mappings: [SOURCE.json](SOURCE.json).
- The SVGs are **build-time source assets**, not fetched or parsed at runtime. `windows/Nashi.Windows/FluentIcons.cs` embeds their path geometry for native WPF rendering.
- To regenerate deterministically, run `python scripts/generate-fluent-icons.py` from any directory; it requires internet **only while regenerating**.
- For new icons, add a semantic mapping in the generator script, choose an available 24px Regular Fluent icon, regenerate, and update the snapshot tests. Never hand-draw an icon and call it Fluent.
- File-type artwork deliberately stays native to Windows Shell (`FileArtwork.cs`) and is **not** replaced by Fluent assets.

Windows is the only affected platform. The macOS app continues using SF Symbols.
