#!/usr/bin/env python3
"""Check the portable ZIP against its self-contained publish directory."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import stat
import zipfile

REQUIRED = {
    'Nashi.exe', 'Nashi.dll', 'Nashi.Core.dll', 'Nashi.deps.json', 'Nashi.runtimeconfig.json',
    'pdfium.dll', 'wpfgfx_cor3.dll', 'Magick.Native-Q8-x64.dll',
    'Magick.NET-Q8-AnyCPU.dll', 'Magick.NET.Core.dll',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll',
    'PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll',
    'PagesInterop.js', 'pages-windows.js', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt',
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def verify(directory, archive):
    directory, archive = Path(directory), Path(archive)
    files = {}
    for path in directory.rglob('*'):
        if path.is_symlink():
            raise ValueError(f'Symlink in publish directory: {path}')
        if path.is_file():
            files[path.relative_to(directory).as_posix()] = path
    missing = REQUIRED - files.keys()
    if missing:
        raise ValueError(f'Missing self-contained app files: {sorted(missing)}')
    config = json.loads(files['Nashi.runtimeconfig.json'].read_text(encoding='utf-8-sig'))
    runtime = config['runtimeOptions']
    if runtime.get('framework') or runtime.get('frameworks') or not runtime.get('includedFrameworks'):
        raise ValueError('Runtime configuration must be self-contained')
    if not any(name.startswith('licenses/') for name in files):
        raise ValueError('Missing third-party license directory')
    with zipfile.ZipFile(archive) as package:
        members = {}
        names = set()
        for entry in package.infolist():
            name = entry.filename
            parts = PurePosixPath(name).parts
            if not parts or name.startswith('/') or '\\' in name or ':' in name or any(p in {'.', '..'} for p in name.split('/') if p):
                raise ValueError(f'Unsafe ZIP path: {name}')
            key = name.rstrip('/').casefold()
            if key in names:
                raise ValueError(f'Duplicate Windows ZIP path: {name}')
            names.add(key)
            if stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError(f'Symlink in ZIP: {name}')
            if not entry.is_dir():
                members[name] = entry
        if members.keys() != files.keys():
            raise ValueError(f'ZIP file list differs: missing={sorted(files.keys() - members.keys())}, extra={sorted(members.keys() - files.keys())}')
        for name, path in files.items():
            if digest(package.read(members[name])) != digest(path.read_bytes()):
                raise ValueError(f'ZIP content differs: {name}')
    return {'files': len(files), 'sha256': digest(archive.read_bytes()), 'archive': archive.name}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('archive', type=Path)
    args = parser.parse_args()
    report = verify(args.directory, args.archive)
    checksum = args.archive.with_name(args.archive.name + '.sha256')
    checksum.write_text(f"{report['sha256']}  {report['archive']}\n", encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
