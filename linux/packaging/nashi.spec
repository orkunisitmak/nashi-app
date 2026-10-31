Name: nashi
Version: 0.7.10
Release: 0.linux1%{?dist}
Summary: Nashi file conversion and editing for Linux (preview)
License: MIT
URL: https://github.com/uahadov/nashi-app
Source0: nashi.tar.gz
Source1: nashi.desktop
Source2: nashi.png
AutoReqProv: no
%global __strip /bin/true
Requires: glibc >= 2.35, libgcc, libstdc++, libicu, openssl-libs, zlib, libX11, libICE, libSM, fontconfig, libgomp
Recommends: ffmpeg, poppler-utils, tesseract, perl-Image-ExifTool, libreoffice, mpv-libs, xdg-utils

%description
A native Linux port sharing Nashi's original conversion engines.
See the included feature parity matrix before treating this preview as feature complete.

%prep
%setup -q -c -T
 tar xzf %{SOURCE0}

%build

%install
mkdir -p %{buildroot}/opt/nashi %{buildroot}%{_bindir} %{buildroot}%{_datadir}/applications %{buildroot}%{_datadir}/icons/hicolor/256x256/apps
cp -a Nashi/. %{buildroot}/opt/nashi/
ln -s /opt/nashi/Nashi %{buildroot}%{_bindir}/Nashi
install -m 644 %{SOURCE1} %{buildroot}%{_datadir}/applications/nashi.desktop
install -m 644 %{SOURCE2} %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/nashi.png

%files
/opt/nashi
%{_bindir}/Nashi
%{_datadir}/applications/nashi.desktop
%{_datadir}/icons/hicolor/256x256/apps/nashi.png
