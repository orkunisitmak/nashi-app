# Contributing

Please open an issue describing the problem, platform, app version and expected
behavior before proposing a large change. Small fixes can go directly to a PR.
Use synthetic, redistributable fixtures; never attach private documents or keys.

macOS: Xcode with Swift 6, macOS 13+, Apple Silicon. Run `swift test`.
Windows source and build instructions live under `windows/`.
Linux source, build instructions and verification commands live under `linux/`.

For native UI changes, use the [isolated UI audit](docs/testing/UI-AUDIT.md) to
check light/dark and English/Turkish scenes without dragging personal files.
It complements the native gesture, keyboard and export tests; it does not replace them.

New output formats and tools must preserve originals, choose unique output names,
clean partial exports on cancellation, and avoid shell interpolation. Test these
boundaries and actual output metadata rather than only checking file existence.

Nashi is independently developed and inspired by Tangerine's drag-and-wheel
interaction. Do not contribute proprietary Tangerine code, binaries, artwork,
screenshots or decompiled resources. Third-party licenses must be preserved.

## Owner pull requests

Ready PRs authored by `orkunisitmak` from branches in this repository merge automatically
once the required GitHub Actions security, Mac and Windows checks pass. Drafts,
external forks and other authors remain manual. Keep a PR in draft while it is not
ready to merge. A new commit must pass the checks again. Automatic merges also
request post-merge CI and the existing GitHub Pages site build.
