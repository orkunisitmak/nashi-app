#!/usr/bin/env bash
set -euo pipefail
umask 022
NASHI_ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$NASHI_ROOT"
NASHI_RID=${1:-linux-x64}
case "$NASHI_RID" in linux-x64) NASHI_ARCH=x86_64; NASHI_DEB_ARCH=amd64;; linux-arm64) NASHI_ARCH=aarch64; NASHI_DEB_ARCH=arm64;; *) exit 2;; esac
NASHI_APP="$NASHI_ROOT/build/linux/$NASHI_RID/Nashi"
[ -x "$NASHI_APP/Nashi" ] || { echo 'Run scripts/build-linux.sh first.' >&2; exit 1; }
NASHI_DIST="$NASHI_ROOT/build/linux/dist"
mkdir -p "$NASHI_DIST"
tar -czf "$NASHI_DIST/Nashi-Linux-$NASHI_ARCH.tar.gz" -C "$(dirname "$NASHI_APP")" Nashi
NASHI_STAGE=$(mktemp -d "$NASHI_ROOT/build/linux/package.XXXXXXXX")
trap 'rm -rf -- "$NASHI_STAGE"' EXIT
NASHI_APPDIR="$NASHI_STAGE/Nashi.AppDir"
mkdir -p "$NASHI_APPDIR/usr/lib/nashi" "$NASHI_APPDIR/usr/bin"
ln -s ../lib/nashi/Nashi "$NASHI_APPDIR/usr/bin/Nashi"
cp -a "$NASHI_APP/." "$NASHI_APPDIR/usr/lib/nashi/"
install -m 755 linux/packaging/AppRun "$NASHI_APPDIR/AppRun"
install -m 644 linux/packaging/nashi.desktop "$NASHI_APPDIR/nashi.desktop"
install -m 644 Resources/Brand/NashiIcon.png "$NASHI_APPDIR/nashi.png"
ln -s nashi.png "$NASHI_APPDIR/.DirIcon"
if [ "${NASHI_APPIMAGE:-0}" = 1 ]; then
    # Pinned official tool AND runtime. No moving 'continuous' dependency.
    case "$NASHI_ARCH" in
      x86_64) NASHI_RUNTIME_HASH=2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d;;
      aarch64) NASHI_RUNTIME_HASH=00cbdfcf917cc6c0ff6d3347d59e0ca1f7f45a6df1a428a0d6d8a78664d87444;;
    esac
    NASHI_HOST_ARCH=$(uname -m)
    case "$NASHI_HOST_ARCH" in
      x86_64) NASHI_TOOL_HASH=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0;;
      aarch64) NASHI_TOOL_HASH=f0837e7448a0c1e4e650a93bb3e85802546e60654ef287576f46c71c126a9158;;
      *) echo 'AppImage tooling requires an x86_64 or aarch64 build host.' >&2; exit 2;;
    esac
    curl -fL --retry 3 "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-$NASHI_HOST_ARCH.AppImage" -o "$NASHI_STAGE/appimagetool"
    curl -fL --retry 3 "https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-$NASHI_ARCH" -o "$NASHI_STAGE/runtime"
    printf '%s  %s\n%s  %s\n' "$NASHI_TOOL_HASH" "$NASHI_STAGE/appimagetool" "$NASHI_RUNTIME_HASH" "$NASHI_STAGE/runtime" | sha256sum -c -
    chmod +x "$NASHI_STAGE/appimagetool"
    ARCH="$NASHI_ARCH" "$NASHI_STAGE/appimagetool" --appimage-extract-and-run --runtime-file "$NASHI_STAGE/runtime" "$NASHI_APPDIR" "$NASHI_DIST/Nashi-Linux-$NASHI_ARCH.AppImage"
fi
if command -v dpkg-deb >/dev/null 2>&1 || command -v ar >/dev/null 2>&1; then
    NASHI_DEB="$NASHI_STAGE/deb"
    mkdir -p "$NASHI_DEB/DEBIAN" "$NASHI_DEB/opt/nashi" "$NASHI_DEB/usr/bin" "$NASHI_DEB/usr/share/applications" "$NASHI_DEB/usr/share/icons/hicolor/256x256/apps"
    cp -a "$NASHI_APP/." "$NASHI_DEB/opt/nashi/"
    ln -s /opt/nashi/Nashi "$NASHI_DEB/usr/bin/Nashi"
    install -m 644 linux/packaging/nashi.desktop "$NASHI_DEB/usr/share/applications/nashi.desktop"
    install -m 644 Resources/Brand/NashiIcon.png "$NASHI_DEB/usr/share/icons/hicolor/256x256/apps/nashi.png"
    cat > "$NASHI_DEB/DEBIAN/control" <<CONTROL
Package: nashi
Version: 0.7.10~linux1
Section: utils
Priority: optional
Architecture: $NASHI_DEB_ARCH
Maintainer: Nashi Linux contributors
Depends: libc6 (>= 2.35), libgcc-s1, libstdc++6, libicu70 | libicu72 | libicu74 | libicu76 | libicu78, libssl3 | libssl3t64, zlib1g, libx11-6, libice6, libsm6, libfontconfig1, libgomp1
Recommends: ffmpeg, poppler-utils, tesseract-ocr, libimage-exiftool-perl, libreoffice, libmpv2, xdg-utils
Description: Nashi file conversion and editing for Linux (preview)
 A native Linux port sharing Nashi's original conversion engines.
CONTROL
    if command -v dpkg-deb >/dev/null 2>&1; then
        dpkg-deb --root-owner-group --build "$NASHI_DEB" "$NASHI_DIST/Nashi-Linux-$NASHI_ARCH.deb"
    else
        printf '2.0\n' > "$NASHI_STAGE/debian-binary"
        tar --owner=0 --group=0 --numeric-owner -czf "$NASHI_STAGE/control.tar.gz" -C "$NASHI_DEB/DEBIAN" .
        tar --owner=0 --group=0 --numeric-owner --exclude=./DEBIAN -czf "$NASHI_STAGE/data.tar.gz" -C "$NASHI_DEB" .
        ar crD "$NASHI_DIST/Nashi-Linux-$NASHI_ARCH.deb" "$NASHI_STAGE/debian-binary" "$NASHI_STAGE/control.tar.gz" "$NASHI_STAGE/data.tar.gz"
    fi
fi
if command -v rpmbuild >/dev/null 2>&1; then
    NASHI_RPM="$NASHI_STAGE/rpm"
    mkdir -p "$NASHI_RPM/SPECS" "$NASHI_RPM/SOURCES"
    cp "$NASHI_DIST/Nashi-Linux-$NASHI_ARCH.tar.gz" "$NASHI_RPM/SOURCES/nashi.tar.gz"
    cp linux/packaging/nashi.desktop "$NASHI_RPM/SOURCES/nashi.desktop"
    cp Resources/Brand/NashiIcon.png "$NASHI_RPM/SOURCES/nashi.png"
    rpmbuild -bb --define "_topdir $NASHI_RPM" --define "_build_id_links none" --define "debug_package %{nil}" --target "$NASHI_ARCH" linux/packaging/nashi.spec
    find "$NASHI_RPM/RPMS" -name '*.rpm' -exec cp {} "$NASHI_DIST/" \;
fi
(cd "$NASHI_DIST"; sha256sum "Nashi-Linux-$NASHI_ARCH.tar.gz" > "SHA256SUMS-$NASHI_ARCH.txt"; for file in "Nashi-Linux-$NASHI_ARCH.AppImage" "Nashi-Linux-$NASHI_ARCH.deb"; do if [ -f "$file" ]; then sha256sum "$file" >> "SHA256SUMS-$NASHI_ARCH.txt"; fi; done; for file in nashi-*.$NASHI_ARCH.rpm; do if [ -f "$file" ]; then sha256sum "$file" >> "SHA256SUMS-$NASHI_ARCH.txt"; fi; done)
printf 'Packages: %s\n' "$NASHI_DIST"
