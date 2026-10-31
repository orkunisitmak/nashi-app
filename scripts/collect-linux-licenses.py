#!/usr/bin/env python3
"""Copy notices from restored NuGet packages, without downloading or running code."""
import json
import shutil
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

assets = json.loads(Path(sys.argv[1]).read_text())
destination = Path(sys.argv[2]) / "licenses" / "nuget"
destination.mkdir(parents=True, exist_ok=True)
folders = [Path(folder) for folder in assets["packageFolders"]]
index = ["# Bundled NuGet dependencies", "", "Generated from the exact restored package graph.", ""]
for identity, library in sorted(assets["libraries"].items()):
    if library["type"] != "package":
        continue
    package = next((base / library["path"] for base in folders if (base / library["path"]).is_dir()), None)
    if package is None:
        raise RuntimeError(f"Restored package missing: {identity}")
    license_text = "See upstream package metadata."
    for nuspec in package.glob("*.nuspec"):
        root = ET.parse(nuspec).getroot()
        for node in root.iter():
            if node.tag.rsplit("}", 1)[-1] == "license":
                license_text = node.text or license_text
    index.append(f"- **{identity}** — {license_text}")
    for source in package.rglob("*"):
        if not source.is_file() or source.suffix.lower() not in {"", ".txt", ".md", ".html"}:
            continue
        if not source.name.lower().startswith(("license", "licence", "notice", "thirdpartynotices", "copying", "copyright")):
            continue
        target = destination / identity.replace("/", "-") / source.relative_to(package)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
(destination / "INDEX.md").write_text("\n".join(index) + "\n")
