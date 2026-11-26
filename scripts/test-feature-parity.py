"""Regressions for check-feature-parity.py, run against temporary copies of the catalogs."""
import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
SWIFT = "Sources/NashiCore/FeatureCatalog.swift"
INPUTS = [SWIFT, "Sources/NashiCore/Models.swift", "Resources/FeatureCatalog.json",
          "windows/Nashi.Core/FeatureCatalog.cs", "scripts/check-feature-parity.py"]
JPG = '        "jpg": ["png", "webp", "heic", "tiff", "avif", "bmp", "pdf"],\n'
COMPRESS = '        .compress: "Compress",\n'
UNPARSED = "a Swift catalog entry could not be parsed"


def check(old=None, new=None, count=1):
    """Run the check on a copy of the catalogs, with `old` replaced by `new` in the Swift one."""
    with tempfile.TemporaryDirectory() as folder:
        for name in INPUTS:
            target = pathlib.Path(folder, name)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(ROOT / name, target)
        if old is not None:
            swift = pathlib.Path(folder, SWIFT)
            text = swift.read_text()
            assert text.count(old) >= count, old
            swift.write_text(text.replace(old, new, count))
        result = subprocess.run([sys.executable, str(pathlib.Path(folder, INPUTS[-1]))],
                                capture_output=True, text=True)
        return result.returncode, result.stdout + result.stderr


class FeatureParityTests(unittest.TestCase):
    def test_current_tree_passes(self):
        code, output = check()
        self.assertEqual(code, 0, output)

    def test_entry_the_strict_pattern_cannot_read_fails(self):
        for old, extra in [(JPG, '        "tga" : ["png"],\n'), (JPG, '        "jpeg-xl": ["png"],\n'),
                           (JPG, '        "MAC": ["pdf"],\n'), (COMPRESS, '        .extraTool : "Extra",\n')]:
            code, output = check(old, old + extra)
            self.assertNotEqual(code, 0, extra)
            self.assertIn(UNPARSED, output, extra)

    def test_second_entry_on_the_same_line_fails(self):
        # JPG is the conversionMatrix line; the empty lists are in singleTools and multipleTools.
        for old, extra, count in [(JPG, ' "MAC": ["pdf"],', 1), ('        "svg": [],\n', ' "MAC": [],', 1),
                                  ('        "avif": [],\n', ' "MAC" : [],', 2), (COMPRESS, ' .extraTool : "Extra",', 1)]:
            code, output = check(old, old[:-1] + extra + "\n", count)
            self.assertNotEqual(code, 0, extra)
            self.assertIn(UNPARSED, output, extra)
        code, output = check(JPG, JPG[:-1] + ' "zzz": ["pdf"],\n')
        self.assertNotEqual(code, 0)
        self.assertIn("diverged", output)

    def test_commented_out_entry_is_ignored(self):
        for old, extra in [(JPG, '        // "example": ["png"],\n'), (JPG, '        // "MAC" : ["png"],\n'),
                           (JPG, '        /* "example": ["png"],\n           "other": ["png"], */\n'),
                           (JPG, JPG.replace('        "jpg"', '        // "jpg"')),
                           (COMPRESS, '        // .extraTool: "Extra",\n'),
                           (COMPRESS, '        /* .extraTool : "Extra", */\n')]:
            code, output = check(old, old + extra)
            self.assertEqual(code, 0, extra + output)
        code, output = check(JPG, JPG[:-1] + ' // "example": ["png"],\n')
        self.assertEqual(code, 0, output)

    def test_comment_markers_inside_a_tool_name_stay_part_of_it(self):
        code, output = check(COMPRESS, '        .compress: "Compress // .extraTool: x",\n')
        self.assertNotEqual(code, 0)
        self.assertIn("toolNames: Windows and macOS catalogs diverged", output)


if __name__ == "__main__":
    unittest.main()
