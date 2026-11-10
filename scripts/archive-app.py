#!/usr/bin/env python3
"""Archive only app content and POSIX modes, without Finder/xattr metadata."""
from pathlib import Path
import os,stat,sys,zipfile
root=Path(sys.argv[1]).resolve();output=Path(sys.argv[2])
if root.suffix!='.app' or not (root/'Contents/Info.plist').is_file():sys.exit('Choose a built .app bundle')
if output.exists():sys.exit('Refusing to replace an existing release archive')
if output.resolve().is_relative_to(root):sys.exit('Release archive output must be outside the app bundle')
with zipfile.ZipFile(output,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
    for path in [root,*sorted(root.rglob('*'))]:
        if path.name=='.DS_Store':continue
        relative=root.name if path==root else root.name+'/'+path.relative_to(root).as_posix()
        mode=path.lstat().st_mode
        if stat.S_ISLNK(mode):
            if not path.resolve().is_relative_to(root):raise ValueError('App symlink leaves bundle')
            data=os.readlink(path).encode()
        elif stat.S_ISDIR(mode):relative+='/';data=b''
        elif stat.S_ISREG(mode):data=path.read_bytes()
        else:raise ValueError('Unsupported app file type')
        info=zipfile.ZipInfo(relative,date_time=(2026,1,1,0,0,0));info.create_system=3;info.external_attr=mode<<16
        if stat.S_ISDIR(mode):info.external_attr|=0x10
        info.compress_type=zipfile.ZIP_DEFLATED;archive.writestr(info,data)
print('Archived app content without extended attributes:',output.name)
