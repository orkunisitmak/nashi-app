#!/usr/bin/env python3
"""Exercise the bundled policy's child command validation without network or tools."""

import importlib.util
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch


POLICY = (Path(__file__).resolve().parents[1] / 'Sources' / 'NashiCore' / 'Resources' /
          'yt-dlp-policy' / 'yt_dlp_plugins' / 'postprocessor' / 'nashi_media_policy.py')


class MediaPolicyTests(unittest.TestCase):
    def setUp(self):
        scratch = tempfile.TemporaryDirectory()
        self.addCleanup(scratch.cleanup)
        self.stage = Path(scratch.name).resolve() / 'stage'
        self.stage.mkdir()
        self.media = self.stage / 'input.mp4'
        self.media.write_bytes(b'fixture')
        self.tools = {name: str(self.stage.parent / f'{name}.exe') for name in ('ffmpeg', 'ffprobe', 'deno')}
        for path in self.tools.values():
            Path(path).touch()

        class FFmpegPostProcessor:
            def _determine_executables(self):
                raise AssertionError('PATH lookup was not replaced')

        modules = {
            'yt_dlp.postprocessor.common': SimpleNamespace(PostProcessor=object),
            'yt_dlp.postprocessor.ffmpeg': SimpleNamespace(FFmpegPostProcessor=FFmpegPostProcessor),
            'yt_dlp.version': SimpleNamespace(__version__='2026.08.19'),
            'test_policy._nashi_media_network': SimpleNamespace(install_network_policy=lambda _: None),
        }
        env = {f'NASHI_MEDIA_{name.upper()}': path for name, path in self.tools.items()}
        env.update(NASHI_MEDIA_PROXY='socks5h://nashi:test@127.0.0.1:1', NASHI_MEDIA_STAGING=str(self.stage))

        def record_command(process, args, *, shell=False, executable=None, cwd=None):
            process.command = args

        spec = importlib.util.spec_from_file_location('test_policy.nashi_media_policy', POLICY)
        self.policy = importlib.util.module_from_spec(spec)
        with patch.dict(sys.modules, modules), patch.dict(os.environ, env), patch.object(subprocess.Popen, '__init__', record_command):
            spec.loader.exec_module(self.policy)
            self.launch = subprocess.Popen.__init__
        self.assertIsNotNone(self.policy.NashiMediaPolicyPP)
        self.assertEqual(FFmpegPostProcessor()._determine_executables(),
                         {name: self.tools[name] for name in ('ffmpeg', 'ffprobe')})

    def command(self, tool, *arguments, **kwargs):
        process = SimpleNamespace()
        self.launch(process, [self.tools[tool], *arguments], **kwargs)
        return process.command

    def test_each_ffmpeg_input_and_direct_ffprobe_receive_policy(self):
        output = self.stage / 'output.mp4'
        result = self.command('ffmpeg', '-i', str(self.media), '-i', str(self.media), '-c', 'copy', str(output))
        for index, argument in enumerate(result):
            if argument == '-i':
                self.assertEqual(result[index-4:index], self.policy._INPUT_POLICY)
        self.assertEqual(result[-1], f'file:{output}')
        self.assertEqual(self.command('ffprobe', '-show_streams', str(self.media))[1:5], self.policy._INPUT_POLICY)
        self.assertTrue({'hls', 'dash', 'concat'}.isdisjoint(self.policy._FILE_FORMATS.split(',')))

    def test_refuses_remote_paths_escapes_and_override_options(self):
        outside = self.stage.parent / 'outside.mp4'
        outside.touch()
        for path in ('http://127.0.0.1/stream.mp4', r'\\server\share\file.mp4', str(outside)):
            with self.subTest(path=path), self.assertRaises(PermissionError):
                self.command('ffprobe', '-show_streams', path)
        for output in (str(outside), str(self.stage / '..' / 'outside.mp4'), str(self.stage / 'frame%d.jpg')):
            with self.subTest(output=output), self.assertRaises(PermissionError):
                self.command('ffmpeg', '-i', str(self.media), output)
        for options in (('-f', 'concat'), ('-protocol_whitelist', 'http'), ('-vf', 'movie=http://127.0.0.1/')):
            with self.subTest(options=options), self.assertRaises(PermissionError):
                self.command('ffmpeg', *options, '-i', str(self.media), str(self.stage / 'output.mp4'))

    def test_refuses_unknown_programs_and_shell_execution(self):
        with self.assertRaises(PermissionError):
            self.launch(SimpleNamespace(), [str(self.stage / 'other.exe'), '--version'])
        with self.assertRaises(PermissionError):
            self.command('ffmpeg', '-version', shell=True)
        with self.assertRaises(PermissionError):
            self.command('ffmpeg', '-version', executable=self.tools['deno'])

    def test_deno_ejs_requires_cached_permissions_free_stdin(self):
        flags = sorted(self.policy._DENO_REQUIRED)
        self.assertEqual(self.command('deno', 'run', *flags, '--no-npm', '-')[-1], '-')
        for options in (['--allow-net', *flags], [flag for flag in flags if flag != '--cached-only']):
            with self.subTest(options=options), self.assertRaises(PermissionError):
                self.command('deno', 'run', *options, '-')


if __name__ == '__main__':
    unittest.main()
