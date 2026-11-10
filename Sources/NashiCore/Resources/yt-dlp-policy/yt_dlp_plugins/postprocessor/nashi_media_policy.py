"""Constrain native tools launched by the bundled yt-dlp release."""

import inspect
import os
import subprocess
from pathlib import Path

from yt_dlp.postprocessor.common import PostProcessor as _PostProcessor
from yt_dlp.postprocessor.ffmpeg import FFmpegPostProcessor as _FFmpegPostProcessor
from yt_dlp.version import __version__ as _yt_dlp_version

from ._nashi_media_network import install_network_policy


_SUPPORTED_VERSION = '2026.08.19'
# Exclude manifest and concat demuxers. In particular, FFmpeg's file protocol can
# access UNC shares on Windows if a demuxer opens an attacker-supplied child URL.
_FILE_FORMATS = (
    'mov,mp4,m4a,3gp,3g2,mj2,matroska,webm,flv,avi,asf,mpegts,mpeg,mpegvideo,'
    'aac,ac3,eac3,mp3,ogg,wav,flac,aiff,amr,dts,'
    'image2,image2pipe,gif,jpeg_pipe,png_pipe,webp_pipe,bmp_pipe,tiff_pipe,avif'
)
_INPUT_POLICY = ['-protocol_whitelist', 'file', '-format_whitelist', _FILE_FORMATS]
_OUTPUT_FORMATS = {
    'mp4', 'mov', 'ipod', 'matroska', 'webm', 'mpeg', 'mpegts', 'flv', 'avi', 'asf',
    'adts', 'mp3', 'ogg', 'opus', 'wav', 'flac', 'aiff', 'image2', 'gif',
}
_FFMPEG_FLAGS = {'-y', '-n', '-hide_banner', '-nostdin', '-vn', '-an', '-sn', '-dn', '-ignore_unknown'}
_FFMPEG_VALUES = {
    '-loglevel', '-v', '-f', '-pattern_type', '-map', '-c', '-acodec', '-vcodec',
    '-bsf', '-movflags', '-b', '-q', '-vbr', '-aspect', '-ss', '-t', '-update', '-vf',
}
_BITSTREAM_FILTERS = {'aac_adtstoasc', 'mjpeg2jpeg', 'setts=ts=TS-STARTPTS'}
_DENO_REQUIRED = {
    '--ext=js', '--no-code-cache', '--no-prompt', '--no-remote', '--no-lock',
    '--node-modules-dir=none', '--no-config', '--cached-only',
}
_DENO_OPTIONAL = {'--no-npm', '--v8-flags=--jitless'}


def _local_path(argument):
    """Accept a native absolute path without UNC, device, or alternate-stream syntax."""
    if not argument or not os.path.isabs(argument) or '\x00' in argument:
        raise PermissionError('A media tool path is not an absolute local path')
    if os.name == 'nt':
        drive, tail = os.path.splitdrive(argument)
        if len(drive) != 2 or drive[1] != ':' or not drive[0].isascii() or not drive[0].isalpha() or ':' in tail:
            raise PermissionError('A media tool path uses a network share, device, or alternate stream')
    return Path(argument)


def _media_path(argument, stage, *, existing):
    """Limit FFmpeg I/O to regular files inside this operation's staging directory."""
    if stage is None:
        raise PermissionError('This media request has no local staging directory')
    if argument.startswith('file:'):
        argument = argument[5:]
    path = _local_path(argument).resolve(strict=existing)
    _local_path(str(path))
    if not path.is_relative_to(stage) or path == stage or ((existing or path.exists()) and not path.is_file()):
        raise PermissionError('A media tool attempted to access a file outside its staging directory')
    if os.name == 'nt' and path.is_reserved():
        raise PermissionError('A media tool requested a reserved Windows file name')
    # FFmpeg also interprets percent escapes in image sequence names. yt-dlp's
    # restricted filenames do not need them, even in a directory component.
    if '%' in str(path):
        raise PermissionError('Image sequence names are unavailable for web downloads')
    return f'file:{path}'


def _ffmpeg_arguments(args, stage):
    """Check yt-dlp's merge, remux, thumbnail, and audio conversion arguments."""
    result = []
    index = 0
    inputs = 0
    forced_format = pattern = None
    while index < len(args):
        option = args[index]
        index += 1
        if option == '-i':
            if index == len(args) or (forced_format, pattern) not in {(None, None), ('image2', 'none')}:
                raise PermissionError('A media tool requested an unsafe input format')
            result.extend([*_INPUT_POLICY, option, _media_path(args[index], stage, existing=True)])
            index += 1
            inputs += 1
            forced_format = pattern = None
        elif option in _FFMPEG_FLAGS:
            result.append(option)
        elif option.startswith('-'):
            name = option.split(':', 1)[0]
            if name not in _FFMPEG_VALUES or index == len(args):
                raise PermissionError('A media tool requested an unsupported FFmpeg option')
            value = args[index]
            index += 1
            if option == '-f':
                forced_format = value
            elif option == '-pattern_type':
                pattern = value
            elif name == '-vf' and value != 'setpts=PTS-STARTPTS':
                raise PermissionError('A media tool requested an unsafe video filter')
            elif name == '-bsf' and value not in _BITSTREAM_FILTERS:
                raise PermissionError('A media tool requested an unsafe bitstream filter')
            elif name in {'-f', '-pattern_type'}:
                raise PermissionError('A media tool requested an unsupported FFmpeg option')
            result.extend([option, value])
        else:
            if not inputs or index != len(args) or pattern is not None or (
                forced_format is not None and forced_format not in _OUTPUT_FORMATS
            ):
                raise PermissionError('A media tool requested an unsafe output format')
            result.append(_media_path(option, stage, existing=False))
    if not inputs:
        raise PermissionError('A media tool requested FFmpeg without a local input')
    return result


def _ffprobe_arguments(args, stage):
    """Guard direct ffprobe invocations, which bypass yt-dlp postprocessor arguments."""
    if not args or args[-1].startswith('-'):
        raise PermissionError('A media tool requested ffprobe without a local input')
    flags = args[:-1]
    if flags not in (['-show_streams'], ['-hide_banner', '-show_format', '-show_streams', '-print_format', 'json']):
        raise PermissionError('A media tool requested an unsupported ffprobe option')
    return [*_INPUT_POLICY, *flags, _media_path(args[-1], stage, existing=True)]


def _tool_arguments(tool, args, stage):
    """Permit capability probes, sandboxed EJS, and local FFmpeg processing only."""
    if tool == 'deno':
        if args == ['--version']:
            return args
        flags = set(args[1:-1])
        if args[:1] == ['run'] and args[-1:] == ['-'] and _DENO_REQUIRED <= flags <= _DENO_REQUIRED | _DENO_OPTIONAL:
            return args
        raise PermissionError('A media tool requested an unsafe Deno invocation')
    if args in (['-bsfs'], ['-version']):
        return args
    return _ffmpeg_arguments(args, stage) if tool == 'ffmpeg' else _ffprobe_arguments(args, stage)


def _install_subprocess_policy():
    """Install the child-process guard before the CLI can resolve the required plugin."""
    if _yt_dlp_version != _SUPPORTED_VERSION or not callable(getattr(_FFmpegPostProcessor, '_determine_executables', None)):
        raise RuntimeError('Set up the supported bundled yt-dlp version to download media')
    tools = {name: str(_local_path(os.environ[f'NASHI_MEDIA_{name.upper()}'])) for name in ('ffmpeg', 'ffprobe', 'deno')}
    if any(not Path(path).is_file() for path in tools.values()):
        raise RuntimeError('Set up the media tools before downloading')
    approved = {os.path.normcase(os.path.normpath(path)): name for name, path in tools.items()}
    stage = os.environ.get('NASHI_MEDIA_STAGING')
    if stage:
        stage = _local_path(stage).resolve(strict=True)
        _local_path(str(stage))
        if not stage.is_dir():
            raise RuntimeError('The media staging path is not a directory')

    original = subprocess.Popen.__init__
    signature = inspect.signature(original)
    if not {'args', 'shell', 'executable', 'cwd'} <= signature.parameters.keys():
        raise RuntimeError('The bundled yt-dlp process API is unsupported')

    def guarded_popen(self, *args, **kwargs):
        """Replace child arguments with the checked command before starting a process."""
        call = signature.bind(self, *args, **kwargs)
        params = call.arguments
        command = params['args']
        if params.get('shell') or params.get('executable') is not None or params.get('cwd') is not None:
            raise PermissionError('A media tool requested an unsafe child process')
        if not isinstance(command, (list, tuple)) or not command or not all(isinstance(arg, str) for arg in command):
            raise PermissionError('A media tool requested an unsupported child process')
        executable = _local_path(command[0])
        tool = approved.get(os.path.normcase(os.path.normpath(executable)))
        if tool is None:
            raise PermissionError('A media tool requested an unapproved executable')
        params['args'] = [tools[tool], *_tool_arguments(tool, list(command[1:]), stage or None)]
        return original(*call.args, **call.kwargs)

    # PATH lookup must never silently select an alternate ffprobe or FFmpeg.
    _FFmpegPostProcessor._determine_executables = lambda self: {name: tools[name] for name in ('ffmpeg', 'ffprobe')}
    subprocess.Popen.__init__ = guarded_popen


install_network_policy(os.environ['NASHI_MEDIA_PROXY'])
_install_subprocess_policy()


class NashiMediaPolicyPP(_PostProcessor):
    """CLI gate. This class exists only after both policies were installed."""

    def run(self, information):
        """Leave extraction metadata untouched once the import-time guards are active."""
        return [], information
