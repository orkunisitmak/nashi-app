#!/usr/bin/env python3
"""Fail if the Windows embedded catalog differs from the native Swift contract."""
import json, pathlib, re
root = pathlib.Path(__file__).resolve().parents[1]
source = (root / 'Sources/NashiCore/FeatureCatalog.swift').read_text(encoding='utf-8')
expected = {}
for name in ['conversionMatrix', 'singleTools', 'multipleTools']:
    block = source.split('let '+name+':',1)[1].split('= [',1)[1].split('\n    ]',1)[0]
    expected[name] = {key:(re.findall(r'"([^"]+)"',value) if name=='conversionMatrix' else re.findall(r'\.([A-Za-z]+)',value)) for key,value in re.findall(r'"([a-z0-9]+)": \[([^\]]*)\]',block)}
block = source.split('let toolNames:',1)[1].split('= [',1)[1].split('\n    ]',1)[0]
expected['toolNames'] = dict(re.findall(r'\.([A-Za-z]+): "([^"]+)"',block))
actual = json.loads((root/'Resources/FeatureCatalog.json').read_text(encoding='utf-8'))
for name, value in expected.items():
    if actual.get(name) != value:
        raise SystemExit(f'{name}: Windows and macOS catalogs diverged. Update the shared contract and corresponding engines/tests together.')
print(f'PASS matching catalogs: {len(actual["conversionMatrix"])} input formats, {len(actual["toolNames"])} tools')
# Runtime preview/metadata routing must agree too; matching menu catalogs alone
# would miss aliases such as M4V and AAC.
models = (root/'Sources/NashiCore/Models.swift').read_text(encoding='utf-8')
windows = (root/'windows/Nashi.Core/FeatureCatalog.cs').read_text(encoding='utf-8')
alias_line = source.split('public static func normalizedExtension',1)[1].split('public static func formats',1)[0]
aliases = dict(re.findall(r'"([a-z0-9]+)":"([a-z0-9]+)"', alias_line))
for category in ['image', 'video', 'audio', 'archive']:
    mac = re.search(r'if \[([^\]]+)\]\.contains\(ext\) \{ return \.'+category+r' \}', models)
    win = re.search(r'bool Is'+category.capitalize()+r'\(string path\)=>new\[\]\{([^}]+)\}', windows)
    if not mac or not win:
        raise SystemExit(f'{category}: category declaration could not be verified')
    extensions = lambda text: set(re.findall(r'"([a-z0-9]+)"',text))
    expected_category = {aliases.get(ext,ext) for ext in extensions(mac[1])}
    if expected_category != extensions(win[1]):
        raise SystemExit(f'{category}: Windows runtime routing differs from Mac: {expected_category ^ extensions(win[1])}')
print('PASS matching image/video/audio/archive preview categories and aliases')
