"""Synthetic release-feed checks for mismatched archive sizes (no signing keys)."""
import runpy
import tempfile
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

validate = runpy.run_path(str(Path(__file__).with_name("validate-update-feed.py")))["validate"]
SPARKLE = "{http://www.andymatuschak.org/xml-namespaces/sparkle}"


class ReleaseArchiveLengthTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="nashi-feed-size-")
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.archive = self.folder / "Nashi-macOS-arm64.zip"
        self.feed = self.folder / "appcast.xml"

    def write_feed(self, claimed_size):
        rss = ET.Element("rss")
        channel = ET.SubElement(rss, "channel")
        item = ET.SubElement(channel, "item")
        ET.SubElement(item, SPARKLE + "version").text = "13"
        ET.SubElement(item, "enclosure", {
            "url": "https://github.com/orkunisitmak/nashi-app/releases/download/v0.7.13/Nashi-macOS-arm64.zip",
            SPARKLE + "edSignature": "synthetic-test-signature",
            "length": str(claimed_size),
        })
        ET.ElementTree(rss).write(self.feed, encoding="utf-8")

    def test_matching_archive_length_is_accepted(self):
        self.archive.write_bytes(b"PK\x03\x04")
        self.write_feed(4)
        validate(self.feed, "13")

    def test_truncated_archive_is_rejected(self):
        self.archive.write_bytes(b"PK\x03\x04")
        self.write_feed(12)
        with self.assertRaisesRegex(ValueError, "archive size does not match"):
            validate(self.feed, "13")

    def test_empty_archive_is_rejected(self):
        self.archive.write_bytes(b"")
        self.write_feed(4)
        with self.assertRaisesRegex(ValueError, "archive size does not match"):
            validate(self.feed, "13")


if __name__ == "__main__":
    unittest.main()
