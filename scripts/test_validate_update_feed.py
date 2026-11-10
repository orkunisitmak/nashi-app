"""Regression tests for signed Sparkle feed archive lookup (no network or real signatures)."""
from pathlib import Path
import runpy
import tempfile
import unittest

validate = runpy.run_path(str(Path(__file__).with_name('validate-update-feed.py')))['validate']

class FeedArchiveNameTests(unittest.TestCase):
    def test_archive_lookup_ignores_download_url_query_and_fragment(self):
        with tempfile.TemporaryDirectory(prefix="nashi-feed-") as root:
            folder=Path(root)
            (folder / "Nashi-macOS-arm64.zip").write_bytes(b"TEST")
            feed=folder / "appcast.xml"
            def verify(suffix):
                feed.write_text(
                    '<rss xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">'
                    '<channel><item><sparkle:version>0.7.8</sparkle:version>'
                    '<enclosure url="https://github.com/orkunisitmak/nashi-app/releases/download/v0.7.8/'
                    'Nashi-macOS-arm64.zip' + suffix + '" sparkle:edSignature="synthetic" length="4" />'
                    '</item></channel></rss>', encoding="utf-8")
                validate(feed, "0.7.8")
            verify("")
            verify("?download=1")
            verify("#mirror")
            verify("?download=1#mirror")
            feed.write_text(
                '<rss xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">'
                '<channel><item><sparkle:version>0.7.8</sparkle:version>'
                '<enclosure url="https://github.com/orkunisitmak/nashi-app/releases/download/v0.7.8/'
                'Missing-archive.zip?download=1" sparkle:edSignature="synthetic" length="4" />'
                '</item></channel></rss>', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "archive is missing"):
                validate(feed, "0.7.8")

if __name__ == "__main__":
    unittest.main()
