# Linux alpha candidate

The first milestone is an installable, usable Nashi Linux alpha sharing the original source, engines, artwork and data models. Complete platform parity remains the target in FEATURE-PARITY.md; this milestone does not claim every original gesture or desktop integration is ready.

Before presenting this candidate upstream:

- Build the current source on x64 and ARM64; run the shared-engine, Linux-export and native UI checks on their Ubuntu CI runners.
- Execute the x64 AppImage and an isolated per-user installation, including an installation path containing spaces. Verify that an upgrade retains the previous installation and leaves synthetic Library/preferences data intact.
- Exercise actual conversion/editing exports, cancellation and source preservation. Native UI checks must include playback, audio/video preview replacement, file drops into Nashi, editor save options, OCR, PDF organization, Library and per-format defaults.
- Keep failures visible. Routed application drag events do not prove global file-manager dragging. A failed ARM64 job is not a passing ARM64 build verification.

## Compatibility scope

The CI targets are Ubuntu 22.04 x64 and Ubuntu 24.04 ARM64. The development machine is an Arch derivative using Wayland with XWayland. Packages include .NET; conversion tools and libmpv remain optional system dependencies documented in README.md. Other distributions, actual ARM64 package installation, native Wayland integration, 32-bit systems and musl systems need further work or verification.

## Known alpha limitations

Hold Ctrl/Shift/Alt while dragging files **onto Nashi** to use the wheels. Global X11 detection has an implementation but still needs real file-manager validation; global native Wayland dragging is incomplete. Tray/login integration, desktop-wide shortcuts, signed updates, remaining editor details and broad distro installation/upgrade checks are unfinished. Optional tools must be installed through the distribution's package manager. Hosted playback tests use a null audio sink and cannot validate speakers.

This is a fork's experimental Linux port, not an official release approved by the upstream maintainer. Submit the work as a draft PR with the current CI results and limitations, referencing the existing Linux discussion (#28), for coordination and review.
