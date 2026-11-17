"""Regression tests for ZIP output accidentally placed inside an app bundle."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).with_name("archive-app.py")


class ArchiveOutputBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="nashi-archive-boundary-")
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.app = self.folder / "Nashi.app"
        contents = self.app / "Contents"
        contents.mkdir(parents=True)
        (contents / "Info.plist").write_bytes(b"synthetic plist")
        (contents / "sample.txt").write_text("synthetic input", encoding="utf-8")

    def run_archive(self, output):
        return subprocess.run(
            [sys.executable, str(SCRIPT), str(self.app), str(output)],
            capture_output=True, text=True, encoding="utf-8",
        )

    def test_archive_outside_bundle_contains_only_app_files(self):
        output = self.folder / "Nashi.zip"
        result = self.run_archive(output)
        self.assertEqual(result.returncode, 0, result.stderr + result.stdout)
        with zipfile.ZipFile(output) as archive:
            names = set(archive.namelist())
        self.assertIn("Nashi.app/Contents/Info.plist", names)
        self.assertIn("Nashi.app/Contents/sample.txt", names)
        self.assertNotIn("Nashi.app/Nashi.zip", names)

    def test_output_inside_bundle_is_rejected_before_writing(self):
        output = self.app / "Nashi.zip"
        result = self.run_archive(output)
        self.assertNotEqual(result.returncode, 0, "must reject self-inclusion")
        self.assertFalse(output.exists(), "refused archive must not be created")

    def test_output_nested_inside_bundle_is_rejected_before_writing(self):
        output = self.app / "Contents" / "release.zip"
        result = self.run_archive(output)
        self.assertNotEqual(result.returncode, 0, "must reject nested self-inclusion")
        self.assertFalse(output.exists(), "refused archive must not be created")


if __name__ == "__main__":
    unittest.main()
