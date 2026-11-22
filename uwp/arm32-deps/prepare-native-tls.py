#!/usr/bin/env python3
"""Stage the pinned ARM32 online TLS graph in a private assembly."""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parents[2]
EXPECTED = ('crates/cargo-horizon-mod', 'crates/horizon-client', 'crates/horizon-client-lib',
    'crates/horizon-client-render', 'crates/horizon-cosmetics', 'crates/horizon-mod-registry',
    'crates/horizon-perf-share', 'crates/horizon-port-pack', 'crates/horizon-server',
    'crates/horizon-server-files', 'crates/horizon-telemetry', 'launcher')
spec = importlib.util.spec_from_file_location('arm_deps', ROOT / 'uwp/arm32-deps/prepare.py')
deps = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deps)
deps.fetch_missing(True)
subprocess.run([sys.executable, str(ROOT / 'uwp/arm32-deps/prepare.py')], check=True)
archives = sorted((deps.CARGO_HOME / 'registry/cache').glob('*/ureq-2.12.1.crate'))
archives = [p for p in archives if hashlib.sha256(p.read_bytes()).hexdigest() == '02d1a66277ed75f640d608235660df48c8e3c19f3b4edb6a263315626cc3c01d']
if not archives: raise RuntimeError('Expected authenticated ureq 2.12.1 crate archive')
target = deps.OUTPUT / 'ureq-2.12.1-native'
with tempfile.TemporaryDirectory(prefix='ureq-native-', dir=deps.OUTPUT) as temporary:
    staged = Path(temporary) / 'crate'
    staged.mkdir()
    with tarfile.open(archives[0], 'r:gz') as archive:
        for member in archive:
            path = Path(member.name)
            if not path.parts or path.parts[0] != 'ureq-2.12.1' or '..' in path.parts or not (member.isfile() or member.isdir()):
                raise RuntimeError('Unexpected ureq archive entry')
            out = staged.joinpath(*path.parts[1:])
            if member.isdir(): out.mkdir(parents=True, exist_ok=True)
            else:
                out.parent.mkdir(parents=True, exist_ok=True)
                out.write_bytes(archive.extractfile(member).read())
    lib = staged / 'src/lib.rs'
    s = lib.read_text(encoding='utf-8')
    marker = '// Without rustls compiled, we just fail on https when using the shortcut'
    old = '#[cfg(not(feature = "tls"))]'
    if s.count(marker) != 1 or s.count(old) != 1: raise RuntimeError('Unexpected ureq connector implementation')
    s = s.replace(old, '#[cfg(all(not(feature = "tls"), not(feature = "native-tls")))]')
    s = s.replace(marker, '#[cfg(all(not(feature = "tls"), feature = "native-tls"))]\npub(crate) fn default_tls_config() -> std::sync::Arc<dyn TlsConnector> {\n    ntls::default_tls_config()\n}\n\n' + marker)
    lib.write_text(s, encoding='utf-8', newline='\n')
    if target.exists():
        def contents(root):
            return {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).digest() for p in root.rglob('*') if p.is_file()}
        if contents(target) != contents(staged): raise RuntimeError('Existing private ureq overlay differs from the audited version')
    else: shutil.copytree(staged, target)
# Validate all intended manifests before making any changes; retries accept the exact staged form.
updates = []
for relative in EXPECTED:
    manifest = ROOT / relative / 'Cargo.toml'
    s = manifest.read_text(encoding='utf-8')
    matches = list(re.finditer(r'(?m)^ureq\s*=.*$', s))
    if len(matches) != 1: raise RuntimeError(f'Unexpected ureq declaration: {manifest}')
    line = matches[0].group()
    if line.count('"tls"') + line.count('"native-tls"') != 1: raise RuntimeError(f'Unexpected TLS features: {manifest}')
    updates.append((manifest, s.replace(line, line.replace('"tls"', '"native-tls"'))))
for manifest, data in updates: manifest.write_text(data, encoding='utf-8', newline='\n')
config = deps.OUTPUT / 'patches-native-tls.toml'
config.write_text((deps.OUTPUT / 'patches.toml').read_text(encoding='utf-8') + f'ureq = {{ path = {json.dumps(target.as_posix())} }}\n' + deps.prepare_arm32_xxhash() + '\n', encoding='utf-8')
print(f'Prepared secure ARM32 online TLS config: {config}')
