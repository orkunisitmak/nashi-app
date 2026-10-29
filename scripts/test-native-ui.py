#!/usr/bin/env python3
"""Own-window Mac QA. Requires a GUI login session, no TCC/global input grants."""
import argparse
import json
from pathlib import Path
import plistlib
import shutil
import subprocess


def bundle_for_audit(binary, output):
    """SwiftUI Text reads Bundle.main's lproj resources, even in a CLI replay."""
    if any(parent.suffix == ".app" for parent in binary.parents):
        return binary
    repository = Path(__file__).resolve().parent.parent
    candidate = output / "application/Nashi UI Audit.app"
    marker = candidate.parent / ".nashi-ui-audit-app"
    if candidate.exists():
        if not marker.is_file():
            raise AssertionError("Refusing to replace an unrelated audit application")
        shutil.rmtree(candidate)
    contents = candidate / "Contents"
    resources = contents / "Resources"
    (contents / "MacOS").mkdir(parents=True)
    resources.mkdir()
    shutil.copy2(binary, contents / "MacOS/Nashi")
    with (repository / "Resources/Info.plist").open("rb") as stream:
        info = plistlib.load(stream)
    info.update(CFBundleIdentifier="local.nashi.ui-audit", CFBundleDisplayName="Nashi UI Audit",
                NashiExternalTools=True)
    # This candidate is only used by the three explicit own-window QA entrypoints.
    info.pop("CFBundleDocumentTypes", None)
    with (contents / "Info.plist").open("wb") as stream:
        plistlib.dump(info, stream)
    for localization in (repository / "Resources/Localizations").glob("*.lproj"):
        shutil.copytree(localization, resources / localization.name)
    core_resources = binary.parent / "Nashi_NashiCore.bundle"
    if core_resources.is_dir():
        shutil.copytree(core_resources, resources / core_resources.name)
    framework = repository / ".build/artifacts/sparkle/Sparkle/Sparkle.xcframework/macos-arm64_x86_64/Sparkle.framework"
    shutil.copytree(framework, contents / "Frameworks/Sparkle.framework", symlinks=True)
    subprocess.run(["python3", str(repository / "scripts/configure-updates.py"),
                    str(contents / "Info.plist"), str(repository / "Configuration/Updates.public.plist")],
                   check=True, capture_output=True)
    subprocess.run(["codesign", "--force", "--sign", "-", "--options", "0", "--timestamp=none", str(candidate)],
                   check=True, capture_output=True)
    marker.write_text("Generated Nashi own-window QA bundle; no optional tool installers.\n")
    return contents / "MacOS/Nashi"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", default=".build/debug/Nashi")
    parser.add_argument("--output", default="build/native-ui")
    args = parser.parse_args()
    app, output = Path(args.app).resolve(), Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    app = bundle_for_audit(app, output)

    def invoke(arguments, expected=0, timeout=120):
        result = subprocess.run([str(app), *map(str, arguments)], capture_output=True,
                                text=True, timeout=timeout)
        if result.returncode != expected:
            raise AssertionError(f"{arguments[0]} returned {result.returncode}, expected {expected}\n"
                                 + result.stdout[-2000:] + result.stderr[-2000:])
        return result

    # The report directory belongs to this test. Guard fixtures are not user data.
    protected = output / "protected-output"
    protected.mkdir(exist_ok=True)
    sentinel = protected / "keep.txt"
    sentinel.write_text("Unrelated files must survive an audit preflight failure.\n")
    before = {p.name: p.read_bytes() for p in protected.iterdir()}
    invoke(["--ui-audit", protected], expected=1)
    assert before == {p.name: p.read_bytes() for p in protected.iterdir()}
    for invalid in (["--scenes", "unknown"], ["--scenes", ""],
                    ["--scenes", "library,library"], ["--appearance", "unknown"],
                    ["--language", "unknown"], ["--scenes"], ["--unknown", "value"]):
        invoke(["--ui-audit", output / "invalid", *invalid], expected=64)
        assert not (output / "invalid").exists()

    invoke(["--ui-audit", output / "gallery"])
    gallery = json.loads((output / "gallery/manifest.json").read_text())
    assert gallery["passed"] and len(gallery["captures"]) == 40
    assert len(gallery["validatorChecks"]) >= 18
    expected = {(s, a, l) for s in ["home.empty", "home.selected", "library",
        "settings.general", "settings.formats", "settings.updates", "wheel.pdf-multiple",
        "editor.crop-image", "editor.annotate-image", "editor.frame-image"]
        for a in ["light", "dark"] for l in ["en", "tr"]}
    assert {(c["scene"], c["appearance"], c["language"]) for c in gallery["captures"]} == expected
    for capture in gallery["captures"]:
        assert capture["status"] == "captured" and not capture["issues"]
        assert (output / "gallery" / capture["image"]).stat().st_size > 100

    invoke(["--pdf-workspace-smoke", output / "pdf-workspace"])
    pdf_workspace = json.loads((output / "pdf-workspace/pdf-workspace.json").read_text())
    assert pdf_workspace["passed"] and len(pdf_workspace["checks"]) >= 13
    invoke(["--range-accessibility-smoke", output / "range"])
    ranges = json.loads((output / "range/mac-range-accessibility.json").read_text())
    assert ranges["passed"] and len(ranges["checks"]) >= 20
    invoke(["--file-artwork-smoke", output / "artwork"])
    artwork = json.loads((output / "artwork/file-artwork.json").read_text())
    assert artwork["passed"] >= 11 and artwork["asyncProviderCalls"] == 1
    print(f"PASS: 40 native UI captures; {len(gallery['validatorChecks'])} audit guardrails; "
          f"{len(ranges['checks'])} range checks; {len(pdf_workspace['checks'])} PDF workspace checks; "
          f"{artwork['passed']} artwork checks; parser/output safety")


if __name__ == "__main__":
    main()
