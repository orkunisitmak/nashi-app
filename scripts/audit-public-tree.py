#!/usr/bin/env python3
"""Check tracked source, without ever printing matched secrets."""
from pathlib import Path
import re,subprocess,sys
root=Path(__file__).resolve().parents[1]
files=subprocess.check_output(['git','ls-files','-z'],cwd=root).decode().split('\0')
issues=[];count=0
for name in filter(None,files):
 p=root/name;count+=1
 if p.is_symlink():issues.append((name,'tracked symlink'));continue
 if p.name.startswith('.env') and p.name!='.env.example':issues.append((name,'environment file'))
 if p.suffix.lower() in {'.p12','.pfx','.key','.mobileprovision'}:issues.append((name,'signing/credential material'))
 if name in {'Configuration/Signing.plist','Configuration/CredentialBroker.json'} or name.startswith(('local/','build/','Downloads/','.build/','Reference/native/','Reference/tool-windows/')):issues.append((name,'private/build artifact'))
 s=p.read_bytes().decode('utf-8','replace')
 for label,pattern in [('personal home path','/'+'Users/'+r'[^/\s"\x27]+/'),('private key',r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),('GitHub credential',r'(?:gh[pousr]_[A-Za-z0-9_]{30,}|github_pat_[A-Za-z0-9_]{40,})')]:
  if re.search(pattern,s):issues.append((name,label))
if issues:
 for name,reason in issues:print('FAIL',name,reason)
 sys.exit(1)
print('PASS public-source policy:',count,'tracked files; no forbidden data/material')
if (root/'scripts/check-feature-parity.py').is_file():subprocess.run([sys.executable,str(root/'scripts/check-feature-parity.py')],check=True)
