#!/usr/bin/env bash
# Optional distro packages from the same self-contained portable bundle. Never installs the result.
set -euo pipefail
cd "$(dirname "$0")/../.."
task_format=${1:-deb}
task_rid=${2:-linux-x64}
case "$task_rid" in
  linux-x64) task_deb_arch=amd64; task_rpm_arch=x86_64;;
  linux-arm64) task_deb_arch=arm64; task_rpm_arch=aarch64;;
  *) echo 'Use linux-x64 or linux-arm64.' >&2; exit 2;;
esac
case "$task_format" in
  deb) command -v dpkg-deb >/dev/null || { echo 'dpkg-deb is needed to build DEB packages.' >&2; exit 2; };;
  rpm) command -v rpmbuild >/dev/null || { echo 'rpmbuild is needed to build RPM packages; no package was built or installed.' >&2; exit 2; };;
  *) echo 'Use deb or rpm.' >&2; exit 2;;
esac
bash linux/scripts/build-linux-portable.sh "$task_rid"
task_output="$PWD/build/linux"
task_stage=$(mktemp -d "$task_output/.distro.XXXXXX")
trap 'rm -rf "$task_stage"' EXIT
task_version=$(sed -n 's#.*<Version>\(.*\)</Version>.*#\1#p' windows/Directory.Build.props)
test -n "$task_version"
tar -xzf "$task_output/Nashi-$task_rid.tar.gz" -C "$task_stage"
task_root="$task_stage/root"
mkdir -p "$task_root/usr/lib/nashi" "$task_root/usr/bin" "$task_root/usr/share/applications" "$task_root/usr/share/pixmaps"
cp -a "$task_stage/Nashi/." "$task_root/usr/lib/nashi/"
cat > "$task_root/usr/bin/nashi" <<'LAUNCHER'
#!/bin/sh
exec /usr/lib/nashi/app/Nashi "$@"
LAUNCHER
chmod 755 "$task_root/usr/bin/nashi"
sed 's/^Exec=Nashi /Exec=nashi /' linux/packaging/nashi.desktop > "$task_root/usr/share/applications/nashi.desktop"
install -m 644 Resources/Brand/NashiIcon.png "$task_root/usr/share/pixmaps/nashi.png"
if [ "$task_format" = deb ]; then
  mkdir -p "$task_root/DEBIAN"
  cat > "$task_root/DEBIAN/control" <<CONTROL
Package: nashi
Version: $task_version
Section: utils
Priority: optional
Architecture: $task_deb_arch
Maintainer: Nashi contributors
Depends: libc6 (>= 2.35), libgcc-s1, libstdc++6, libfontconfig1, libx11-6, libice6, libsm6, libxcursor1, libxrandr2, libxfixes3, libxi6, libxext6, libssl3 | libssl3t64, zlib1g, libicu70 | libicu72 | libicu74 | libicu76 | libicu77 | libicu78
Recommends: tesseract-ocr, tesseract-ocr-eng, tesseract-ocr-tur
Homepage: https://github.com/orkunisitmak/nashi-app
Description: Nashi Linux desktop preview
 Convert and edit local files with a two-face radial menu.
 Preview build; detailed testing is ongoing. Office/media tools are optional.
CONTROL
  task_name="Nashi-$task_rid.deb"
  dpkg-deb --root-owner-group --build "$task_root" "$task_output/$task_name"
else
  mkdir -p "$task_stage/rpm" "$task_stage/results" "$task_stage/rpm-tmp"
  cat > "$task_stage/nashi.spec" <<SPEC
%global _build_id_links none
%global __os_install_post %{nil}
Name: nashi
Version: $task_version
Release: 1.preview
Summary: Nashi Linux desktop preview
License: MIT
URL: https://github.com/orkunisitmak/nashi-app
AutoReqProv: no
Requires: glibc >= 2.35, libgcc, libstdc++, fontconfig, libX11, libICE, libSM, libXcursor, libXrandr, libXfixes, libXi, libXext, libicu, openssl-libs, zlib
%description
Convert and edit local files with a two-face radial menu.
Preview build; detailed testing is ongoing. Office/media tools are optional.
%install
mkdir -p "%{buildroot}"
cp -a "%{_sourcedir}/root/." "%{buildroot}/"
%files
/usr/bin/nashi
/usr/lib/nashi
/usr/share/applications/nashi.desktop
/usr/share/pixmaps/nashi.png
SPEC
  rpmbuild -bb --target "$task_rpm_arch" --define "_topdir $task_stage/rpm" --define "_tmppath $task_stage/rpm-tmp" --define "_dbpath $task_stage/rpmdb" --define "_sourcedir $task_stage" --define "_rpmdir $task_stage/results" "$task_stage/nashi.spec"
  task_name="Nashi-$task_rid.rpm"
  task_rpms=("$task_stage/results/$task_rpm_arch/"*.rpm)
  test "${#task_rpms[@]}" -eq 1 && test -f "${task_rpms[0]}"
  cp "${task_rpms[0]}" "$task_output/$task_name"
fi
(cd "$task_output" && sha256sum "$task_name" > "$task_name.sha256")
echo "Built $task_output/$task_name (not installed)"
