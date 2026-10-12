"""Prepare a portable hard-link toolkit without altering the input distribution."""
import argparse
import json
import os
from pathlib import Path
import shutil

import toolkit_manifest as toolkit


def prepare(source, target, entries):
    if target.exists():
        raise ValueError('Output already exists; select a new directory')
    if target.resolve().is_relative_to(source.resolve()):
        raise ValueError('Output must not be inside the input distribution')
    # Verify the source before making a candidate. Original package stays untouched.
    original = toolkit.read(source / 'toolkit.json')
    toolkit.validate(original, source)
    if original.get('layout'):
        raise ValueError('Input is already compact; use the original publish output')
    target.mkdir(parents=True)
    shutil.copytree(source, target / '.payload', ignore=shutil.ignore_patterns('.state'))
    for symbol in (target / '.payload').rglob('*.pdb'):
        symbol.unlink()
    for role, path in toolkit.ENTRIES.items():
        if role == 'documentation':
            continue
        entry = entries / role / ('Bgb.Entry.' + role + '.exe')
        if not entry.is_file():
            raise ValueError(f'Missing native bootstrap: {entry}')
        destination = target / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(entry, destination)
    shutil.copytree(source / 'docs', target / 'docs')
    shutil.copyfile(source / 'README.md', target / 'README.md')
    manifest = {k: original[k] for k in ('version', 'platform', 'commit', 'entries')}
    manifest.update({'api': 2, 'format': toolkit.FORMAT, 'architecture': 'x64', 'packageMode': 'standalone',
                     'layout': 'hardlinks-v1',
                     'entryTargets': {p: '.payload/' + p for r, p in toolkit.ENTRIES.items() if r != 'documentation'}})
    (target / 'toolkit.json').write_bytes(toolkit.canonical(manifest))


def compact(root):
    manifest = toolkit.read(root / 'toolkit.json')
    toolkit.validate(manifest, root)
    if manifest.get('layout') != 'hardlinks-v1':
        raise ValueError('Expected native bootstrap layout')
    # Validate every removal against the full root, and remove only enumerated aliases.
    resolved_root = root.resolve()
    for file in manifest['files']:
        if file['storage'] == file['path']:
            continue
        alias = root / file['path']
        alias.resolve().relative_to(resolved_root)
        if alias.exists():
            if alias.is_symlink() or toolkit.digest(alias) != file['sha256']:
                raise ValueError(f'Alias changed during compaction: {alias}')
            alias.unlink()
    toolkit.validate(manifest, root)
    report = {'logicalBytes': sum(f['size'] for f in manifest['files']),
              'storedBytes': sum(f['size'] for f in manifest['files'] if f['storage'] == f['path']),
              'logicalFiles': len(manifest['files']),
              'storedFiles': len({f['storage'] for f in manifest['files']}),
              'runtimeCoreIdentities': sorted({f['sha256'] for f in manifest['files'] if f['path'].endswith('/coreclr.dll')})}
    print(json.dumps(report, indent=2))
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['prepare', 'compact'])
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--source', type=Path)
    parser.add_argument('--entries', type=Path)
    args = parser.parse_args()
    if args.command == 'prepare':
        if args.source is None or args.entries is None:
            parser.error('--source and --entries are required')
        prepare(args.source, args.root, args.entries)
    else:
        compact(args.root)
