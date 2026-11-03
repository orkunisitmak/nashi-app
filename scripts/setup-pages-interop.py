#!/usr/bin/env python3
"""Reproduce the offline Pages JavaScript bundle from pinned public sources.

This is an optional dependency build step. The generated bundle is checked in
and shipped with the app, so conversions never fetch packages or launch Node.
"""
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
WORK = ROOT / "Tools/PagesBuild"
SOURCES = [
    {"name": "cupertino-files", "version": "0.4.0", "url": "https://registry.npmjs.org/cupertino-files/-/cupertino-files-0.4.0.tgz", "sha256": "d75183c0dc0168b43e2d28462943b0b47f51428466df48b48a0d408100d54327", "directory": "cupertino", "file": "cupertino.tgz", "gitHead": "8a539ad4b75d9650a42c876083e96f1c4ddf3ccc", "license": "MIT; BSD-3-Clause LZVN decoder"},
    {"name": "@esbuild/darwin-arm64", "version": "0.25.12", "url": "https://registry.npmjs.org/@esbuild/darwin-arm64/-/darwin-arm64-0.25.12.tgz", "sha256": "2e0d011f852e45046ca5c107db54cfc56338f77aca49d227a063696d958451d7", "directory": "esbuild", "file": "esbuild.tgz", "gitHead": "208f539945b145e7c9d6d844290f81c3fe5af320", "license": "MIT (build tool only)"},
    {"name": "PicoMLX/PicoDocs empty-table donor source", "url": "https://raw.githubusercontent.com/PicoMLX/PicoDocs/5c18743d3d8120a76da124bd512a3cf5bcc28e82/Tests/PicoDocsTests/Resources/sample.pages", "sha256": "4714477138d0a4090fc2ee2ba2ebb6adcd0fb6ce20a28897a6247a8e17d1ddce", "file": "picodocs-sample.pages", "gitHead": "5c18743d3d8120a76da124bd512a3cf5bcc28e82", "license": "MIT"},
    {"name": "PicoMLX/PicoDocs license", "url": "https://raw.githubusercontent.com/PicoMLX/PicoDocs/5c18743d3d8120a76da124bd512a3cf5bcc28e82/LICENSE", "sha256": "73d43facffd8a09b91940ab1596f1601b49d3301acf48da00d8fa74aaf4005b0", "file": "PicoDocs-LICENSE", "license": "MIT"}
]

def acquire(item):
    target = WORK / item["file"]
    data = target.read_bytes() if target.exists() else urllib.request.urlopen(item["url"], timeout=90).read()
    if hashlib.sha256(data).hexdigest() != item["sha256"]:
        raise RuntimeError("Digest mismatch: " + item["name"])
    # Replace atomically so an interrupted write never leaves a truncated cache.
    temporary = target.with_name(target.name + ".download")
    try:
        temporary.write_bytes(data)
        os.replace(temporary, target)
    except BaseException:
        temporary.unlink(missing_ok=True)
        raise
    if "directory" in item:
        destination = WORK / item["directory"]
        destination.mkdir(parents=True, exist_ok=True)
        with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as archive:
            for member in archive:
                path = Path(member.name)
                if path.is_absolute() or ".." in path.parts or not (member.isfile() or member.isdir()):
                    raise RuntimeError("Unsafe package entry: " + member.name)
                output = destination / path
                if member.isdir():
                    output.mkdir(parents=True, exist_ok=True)
                else:
                    output.parent.mkdir(parents=True, exist_ok=True)
                    output.write_bytes(archive.extractfile(member).read())
                    output.chmod(member.mode & 0o777)

def main():
    WORK.mkdir(parents=True, exist_ok=True)
    for item in SOURCES: acquire(item)
    node = os.environ.get("NASHI_NODE") or shutil.which("node")
    if not node:
        candidate = Path.home() / ".cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node"
        if candidate.is_file(): node = str(candidate)
    if not node: raise RuntimeError("Set NASHI_NODE to a Node 22+ executable for this build step.")
    subprocess.run([node, str(ROOT / "scripts/pages-make-seed.mjs")], cwd=ROOT, check=True)
    bundle = ROOT / "Resources/PagesInterop.js"
    subprocess.run([str(WORK / "esbuild/package/bin/esbuild"), "scripts/pages-interop-entry.js", "--bundle", "--format=iife", "--platform=browser", "--target=safari16", "--global-name=NashiPages", "--loader:.pages=binary", "--legal-comments=inline", "--outfile=" + str(bundle)], cwd=ROOT, check=True)
    licenses = ROOT / "Resources/licenses/cupertino-files"
    licenses.mkdir(parents=True, exist_ok=True)
    package = WORK / "cupertino/package"
    for source, name in [(package / "LICENSE", "MIT.txt"), (package / "LICENSES/BSD-3-Clause-lzfse.txt", "BSD-3-Clause-lzfse.txt"), (package / "THIRD-PARTY-NOTICES.md", "THIRD-PARTY-NOTICES.md"), (WORK / "PicoDocs-LICENSE", "PicoDocs-MIT.txt")]:
        shutil.copyfile(source, licenses / name)
    provenance = {"sources": [{key:value for key,value in item.items() if key not in ("directory", "file")} for item in SOURCES], "bundle": {"path": "Resources/PagesInterop.js", "sha256": hashlib.sha256(bundle.read_bytes()).hexdigest(), "bytes": bundle.stat().st_size}, "seed": {"sha256": hashlib.sha256((WORK / "table-seed.pages").read_bytes()).hexdigest(), "transformation": "Public PicoDocs MIT fixture body, all text storages and cell values emptied; table merges removed; headers and footer bands disabled; previews and Data media stripped. No original reference-app resource is used."}, "adapter": {"path": "scripts/pages-interop-entry.js", "sha256": hashlib.sha256((ROOT / "scripts/pages-interop-entry.js").read_bytes()).hexdigest()}, "runtime": "macOS JavaScriptCore; no Node process, network or Apple Pages automation"}
    (licenses / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    print(str(bundle) + " " + provenance["bundle"]["sha256"])

if __name__ == "__main__": main()
