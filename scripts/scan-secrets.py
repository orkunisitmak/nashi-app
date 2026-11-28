#!/usr/bin/env python3
"""Pinned official gitleaks; redact all findings. No token or external service."""
import hashlib,io,os,platform,re,subprocess,tarfile,tempfile,urllib.request
from pathlib import Path
root=Path(__file__).resolve().parents[1]

def scan(gitleaks,repo):
 """Scan all history of repo with the default rules only and return gitleaks' exit status."""
 # The scanned tree must not configure or silence its own scan: pin the default rules and
 # ignore any committed .gitleaks.toml, .gitleaksignore or inline gitleaks:allow comment.
 repo=Path(repo)
 if (repo/'.gitleaksignore').exists() or (repo/'.gitleaksignore').is_symlink():
  raise SystemExit('Refusing to scan: gitleaks always honours a .gitleaksignore at the scanned root; remove it')
 # A committed .gitattributes marking a file binary or -diff would leave only "Binary files
 # differ" in git log -p, so git reads attributes from the empty tree instead (git 2.40+).
 version=subprocess.run(['git','version'],check=True,capture_output=True,text=True).stdout
 if tuple(map(int,re.search(r'(\d+)\.(\d+)',version).groups()))<(2,40):
  raise SystemExit('Use git 2.40 or newer, which honours GIT_ATTR_SOURCE')
 empty=subprocess.run(['git','-C',str(repo),'hash-object','-t','tree','/dev/null'],check=True,capture_output=True,text=True).stdout.strip()
 with tempfile.TemporaryDirectory() as policy:
  (Path(policy)/'gitleaks.toml').write_text('[extend]\nuseDefault = true\n')
  return subprocess.run([str(gitleaks),'git','--redact=100','--no-banner','--config',str(Path(policy)/'gitleaks.toml'),'--gitleaks-ignore-path',policy,'--ignore-gitleaks-allow','--log-opts=--all',str(repo)],env={**os.environ,'GIT_ATTR_SOURCE':empty}).returncode

if __name__=='__main__':
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
 raise SystemExit(scan(work/'gitleaks',root))
