#!/usr/bin/env python3
"""Release guardrails after Sparkle's cryptographic feed verification."""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

SPARKLE = "{http://www.andymatuschak.org/xml-namespaces/sparkle}"


def version(value):
    if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){0,2}", value or ""):
        raise ValueError("Release builds must use numeric CFBundleVersion values")
    parts = tuple(map(int, value.split(".")))
    return parts + (0,) * (3 - len(parts))


def validate(feed_path, build_number, must_be_newer=False):
    root = ET.parse(feed_path).getroot()
    items = root.findall("./channel/item")
    builds = [item.findtext(SPARKLE + "version") for item in items]
    current = version(build_number)
    if must_be_newer:
        if any(version(build) >= current for build in builds):
            raise ValueError("The build number must be greater than every previous release")
        return
    matching = [item for item, build in zip(items, builds) if version(build) == current]
    if len(matching) != 1:
        raise ValueError("The generated feed does not contain exactly one entry for this build")
    enclosure = matching[0].find("enclosure")
    if enclosure is None or not enclosure.get(SPARKLE + "edSignature") or int(enclosure.get("length", "0")) <= 0:
        raise ValueError("The release download is missing its Ed25519 signature or length; check that the signing key matches the public key")
    archive_name = enclosure.get("url", "").rsplit("/", 1)[-1]
    archive = Path(feed_path).parent / archive_name
    if not archive.is_file():
        raise ValueError("The signed release archive is missing")
    if archive.stat().st_size != int(enclosure.get("length", "0")):
        raise ValueError("The signed release archive size does not match its feed enclosure length")


if __name__ == "__main__":
    try:
        validate(sys.argv[1], sys.argv[2], "--must-be-newer" in sys.argv[3:])
    except (ValueError, OSError, ET.ParseError) as error:
        sys.exit(str(error))
