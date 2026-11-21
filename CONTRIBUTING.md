# Contributing

Please open an issue describing the problem, platform, app version and expected
behavior before proposing a large change. Small fixes can go directly to a PR.
Use synthetic, redistributable fixtures; never attach private documents or keys.

macOS: Xcode with Swift 6, macOS 13+, Apple Silicon. Run `swift test`.
Windows source and build instructions live under `windows/`.

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

## How pull requests are reviewed

This project is maintained by one person, with automated help. To keep reviews fair and fast:

- **Features start with an issue.** Before building a new feature or changing how the app looks or behaves, open an issue that describes the idea. Once the maintainer accepts it (the `accepted` label or a maintainer comment), a pull request is welcome. Feature pull requests opened without an accepted issue are closed with a friendly note and can be reopened once the issue is accepted.
- **Bug fixes, translations, documentation and tests** can go straight to a pull request.
- **At most 5 open pull requests per contributor.** If you already have 5 open, please wait until some are merged or closed. Newer pull requests over the limit are closed with a note.
- **One change per pull request.** Small, focused pull requests are reviewed first; large mixed ones are asked to be split.
- **Keep your branch current.** Pull requests that conflict with `main` or fail CI wait for their author. If a useful fix stays stuck for a long time, the maintainer may finish it and credit you as co-author.
- Pull requests are reviewed about a day after their last commit, a few per day, oldest first. New dependencies, CI, release, signing and update changes always get a closer look.
