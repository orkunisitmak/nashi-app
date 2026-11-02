#!/usr/bin/env python3
"""Run real package exports and isolated native Mac controls, without user data."""
import argparse
import hashlib
import importlib.util
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent
sys.dont_write_bytecode = True
# Test-only FFmpeg from imageio-ffmpeg 0.6.0. Immutable PyPI wheel URLs and SHA-256
# digests come from https://pypi.org/pypi/imageio-ffmpeg/0.6.0/json.
ENCODERS = {
    "arm64": ("40/5c/f3d8a657d362cc93b81aab8feda487317da5b5d31c0e1fdfd5e986e55d17/"
              "imageio_ffmpeg-0.6.0-py3-none-macosx_11_0_arm64.whl",
              "b1ae3173414b5fc5f538a726c4e48ea97edc0d2cdc11f103afee655c463fa742"),
    "x86_64": ("da/58/87ef68ac83f4c7690961bce288fd8e382bc5f1513860fc7f90a9c1c1c6bf/"
               "imageio_ffmpeg-0.6.0-py3-none-macosx_10_9_intel.macosx_10_9_x86_64.whl",
               "9d2baaf867088508d4a3458e61eeb30e945c4ad8016025545f66c4b5aaef0a61"),
}


def webp_encoder(output):
    candidates = [os.environ.get("NASHI_FFMPEG"), shutil.which("ffmpeg")]
    for candidate in filter(None, candidates):
        result = subprocess.run([candidate, "-hide_banner", "-encoders"], capture_output=True, text=True, timeout=10)
        if result.returncode == 0 and " libwebp " in result.stdout:
            return candidate
    asset, digest = ENCODERS[platform.machine()]
    cache = output / "encoder"
    cache.mkdir(exist_ok=True)
    wheel = cache / "imageio-ffmpeg.whl"
    if not wheel.exists() or hashlib.sha256(wheel.read_bytes()).hexdigest() != digest:
        request = urllib.request.Request("https://files.pythonhosted.org/packages/" + asset)
        with urllib.request.urlopen(request, timeout=30) as response:
            data = response.read(100_000_001)
        assert len(data) <= 100_000_000 and hashlib.sha256(data).hexdigest() == digest, "Test encoder checksum mismatch"
        wheel.write_bytes(data)
    with zipfile.ZipFile(wheel) as archive:
        names = [name for name in archive.namelist() if "/binaries/ffmpeg-" in name]
        assert len(names) == 1
        binary = cache / "ffmpeg"
        binary.write_bytes(archive.read(names[0]))
        binary.chmod(0o755)
    return str(binary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", default=".build/debug/Nashi")
    parser.add_argument("--output", default="build/image-export-packages")
    args = parser.parse_args()
    output = (ROOT / args.output).resolve()
    marker = output / ".nashi-image-package-tests"
    if output.exists() and any(output.iterdir()) and not marker.exists():
        raise AssertionError("Choose an empty package test output directory")
    output.mkdir(parents=True, exist_ok=True)
    marker.write_text("Generated package tests and test-only encoder.\n")
    environment = os.environ.copy()
    environment["NASHI_FFMPEG"] = webp_encoder(output)
    subprocess.run(["swift", "test", "--filter", "ImageExportPackageTests"], cwd=ROOT, env=environment, check=True)
    spec = importlib.util.spec_from_file_location("native_audit", ROOT / "scripts/test-native-ui.py")
    native = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(native)
    candidate = native.bundle_for_audit((ROOT / args.app).resolve(), output)
    for arguments in (["--image-package-smoke"], ["--image-package-smoke", "--unknown"],
                      ["--image-package-smoke", str(output / "unused"), "--unknown"]):
        result = subprocess.run([str(candidate), *arguments], cwd=ROOT, env=environment, capture_output=True, timeout=10)
        assert result.returncode == 64, "Malformed QA arguments must not start the normal app"
    protected = output / "protected"
    protected.mkdir(exist_ok=True)
    sentinel = protected / "keep.txt"
    sentinel.write_text("Unrelated files must survive QA preflight.\n")
    before = {p.name: p.read_bytes() for p in protected.iterdir()}
    result = subprocess.run([str(candidate), "--image-package-smoke", str(protected)], cwd=ROOT, env=environment, capture_output=True, timeout=10)
    assert result.returncode == 1 and before == {p.name: p.read_bytes() for p in protected.iterdir()}
    subprocess.run([str(candidate), "--image-package-smoke", str(output / "proof")], cwd=ROOT, env=environment, check=True, timeout=90)
    import json
    report = json.loads((output / "proof/mac-image-packages.json").read_text())
    assert report["passed"] and len(report["checks"]) >= 20
    assert len(list((output / "proof").glob("mac-image-package-*.png"))) == 6
    print("Image packages: 12 engine tests and", len(report["checks"]), "native checks passed")


if __name__ == "__main__":
    main()
