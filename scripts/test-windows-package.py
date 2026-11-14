#!/usr/bin/env python3
"""Regression fixtures for portable Windows package validation."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('package', Path(__file__).with_name('verify-windows-package.py'))
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


class PackageTests(unittest.TestCase):
    def setUp(self):
        """Create an isolated self-contained publish directory for each test."""
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.app = self.root / 'app'
        self.app.mkdir()
        for name in package.REQUIRED | {'licenses/runtime.txt', '.hidden-resource'}:
            path = self.app / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b'fixture')
        self.config = self.app / 'Nashi.runtimeconfig.json'
        self.config.write_text(json.dumps({'runtimeOptions': {'includedFrameworks': [{'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}]}}))
        self.zip = self.root / 'Nashi.zip'

    def archive(self, omit=None, extra=None):
        """Build an archive, optionally omitting a file or injecting an entry."""
        with zipfile.ZipFile(self.zip, 'w') as archive:
            for path in self.app.rglob('*'):
                if path.is_file() and path.name != omit:
                    archive.write(path, path.relative_to(self.app).as_posix())
            if extra:
                archive.writestr(*extra)

    def test_complete_package_including_hidden_files(self):
        self.archive()
        self.assertEqual(package.verify(self.app, self.zip)['files'], len(package.REQUIRED) + 2)

    def test_missing_runtime(self):
        (self.app / 'coreclr.dll').unlink()
        self.archive()
        with self.assertRaisesRegex(ValueError, 'Missing self-contained'):
            package.verify(self.app, self.zip)

    def test_framework_dependent_configuration(self):
        self.config.write_text(json.dumps({'runtimeOptions': {'framework': {'name': 'Microsoft.NETCore.App'}}}))
        self.archive()
        with self.assertRaisesRegex(ValueError, 'self-contained'):
            package.verify(self.app, self.zip)

    def test_missing_archive_file(self):
        self.archive(omit='.hidden-resource')
        with self.assertRaisesRegex(ValueError, 'file list differs'):
            package.verify(self.app, self.zip)

    def test_changed_payload(self):
        self.archive()
        (self.app / 'Nashi.dll').write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError, 'content differs'):
            package.verify(self.app, self.zip)

    def test_unsafe_and_duplicate_paths(self):
        """Reject traversal, absolute paths and Windows case collisions."""
        for name in ['../outside', '/outside', 'C:/outside', 'folder\\outside', 'nashi.EXE']:
            with self.subTest(name=name):
                self.archive(extra=(name, b'unexpected'))
                with self.assertRaises(ValueError):
                    package.verify(self.app, self.zip)

    def test_windows_device_directories(self):
        """Reject reserved names even in extra directory entries without files."""
        for name in ['CON/', 'con.txt/', 'nested/LPT9/', 'COM1/', 'COM\u00b9/',
                     'LPT\u00b3.txt/', 'PRN. /', 'aux.log/', 'nul/']:
            with self.subTest(name=name):
                self.archive(extra=(name, b''))
                with self.assertRaisesRegex(ValueError, 'Windows device name'):
                    package.verify(self.app, self.zip)

    def test_ordinary_directory_with_device_prefix(self):
        """Accept ordinary directory names that merely start with device names."""
        self.archive(extra=('Console/', b''))
        self.assertEqual(package.verify(self.app, self.zip)['files'], len(package.REQUIRED) + 2)

    def test_unexpected_file(self):
        self.archive(extra=('unexpected.txt', b'extra'))
        with self.assertRaisesRegex(ValueError, 'file list differs'):
            package.verify(self.app, self.zip)


if __name__ == '__main__':
    unittest.main()
