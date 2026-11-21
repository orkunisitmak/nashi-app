#!/usr/bin/env python3
"""Pinned official gitleaks; redact all findings. No token or external service."""
import hashlib,io,platform,subprocess,tarfile,tempfile,urllib.request
from pathlib import Path
root=Path(__file__).resolve().parents[1]
if platform.system()=='Darwin' and platform.machine()=='arm64':
 name='gitleaks_8.30.1_darwin_arm64.tar.gz';expected='b40ab0ae55c505963e365f271a8d3846efbc170aa17f2607f13df610a9aeb6a5'
elif platform.system()=='Linux' and platform.machine() in ['x86_64','AMD64']:
 name='gitleaks_8.30.1_linux_x64.tar.gz';expected='551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb'
else:raise SystemExit('Use gitleaks 8.30.1 from its official release for this architecture')
url='https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/'+name
with urllib.request.urlopen(url,timeout=90) as r:data=r.read()
assert hashlib.sha256(data).hexdigest()==expected,'Scanner checksum mismatch'
work=root/'build/security';work.mkdir(parents=True,exist_ok=True)
with tarfile.open(fileobj=io.BytesIO(data),mode='r:gz') as t:(work/'gitleaks').write_bytes(t.extractfile('gitleaks').read())
(work/'gitleaks').chmod(0o755)
# The scanned tree must not configure or silence its own scan: pin the default rules and
# ignore any committed .gitleaks.toml, .gitleaksignore or inline gitleaks:allow comment.
if (root/'.gitleaksignore').exists() or (root/'.gitleaksignore').is_symlink():
 raise SystemExit('Refusing to scan: gitleaks always honours a .gitleaksignore at the scanned root; remove it')
with tempfile.TemporaryDirectory() as policy:
 (Path(policy)/'gitleaks.toml').write_text('[extend]\nuseDefault = true\n')
 subprocess.run([str(work/'gitleaks'),'git','--redact=100','--no-banner','--config',str(Path(policy)/'gitleaks.toml'),'--gitleaks-ignore-path',policy,'--ignore-gitleaks-allow','--log-opts=--all',str(root)],check=True)
