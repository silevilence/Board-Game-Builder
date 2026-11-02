"""Check that every distributed author/player entry includes its native capabilities."""
import argparse
import hashlib
import json
from pathlib import Path
import re

import toolkit_manifest as toolkit


def read(path):
    return toolkit.read(path)


def inside(root, relative):
    path = root / toolkit.relative(relative)
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError(f'Escaped native distribution path: {path}')
    return path


def verify_files(root, files):
    if not isinstance(files, list) or not files:
        raise ValueError(f'Empty native distribution inventory: {root}')
    paths = set()
    for item in files:
        path = inside(root, item['path'])
        name = item['path'].casefold()
        if name in paths or not re.fullmatch(toolkit.HASH, item['sha256']):
            raise ValueError(f'Duplicate path or invalid hash: {path}')
        paths.add(name)
        if not path.is_file():
            raise ValueError(f'Missing or escaped native distribution file: {path}')
        with path.open('rb') as stream:
            actual = hashlib.file_digest(stream, 'sha256').hexdigest()
        if actual != item['sha256']:
            raise ValueError(f'Native distribution hash mismatch: {path}')


def verify(root):
    for entry in ('player', 'debug-player', 'editor'):
        package = root / entry / 'backends/desktopgl'
        manifest = read(package / 'runtime.plugin.json')
        if (manifest['format'] != 1 or manifest['kind'] != 'runtime' or
                manifest['id'] != 'render.desktopgl' or 'win-x64' not in manifest['targets']):
            raise ValueError(f'Wrong graphics package: {package}')
        verify_files(package, manifest['files'])
        if not (package / 'MonoGame.LICENSE.txt').is_file():
            raise ValueError(f'Missing MonoGame license: {package}')
    for entry in ('', 'editor'):
        sdk = root / entry / 'native-sdk'
        manifest = read(sdk / 'native.sdk.json')
        if manifest['format'] != 1 or manifest['target'] != 'win-x64':
            raise ValueError(f'Wrong native SDK target: {sdk}')
        if (len(manifest['hosts']) != 2 or
                {host['id'] for host in manifest['hosts']} != {'board.desktop', 'application.desktop'}):
            raise ValueError(f'Missing native host: {sdk}')
        for host in manifest['hosts']:
            verify_files(sdk, host['files'])
        ids = set()
        packages = set()
        for relative in manifest['packages']:
            package = inside(sdk, relative)
            if relative.casefold() in packages:
                raise ValueError(f'Duplicate native SDK package: {relative}')
            packages.add(relative.casefold())
            plugin = read(package / 'runtime.plugin.json')
            if (plugin['format'] != 1 or plugin['kind'] != 'runtime' or plugin['id'] in ids or
                    'win-x64' not in plugin['targets'] or plugin.get('aot', {}).get('compatible') is not True or
                    'win-x64' not in plugin['aot']['targets']):
                raise ValueError(f'Invalid native SDK package identity or target: {package}')
            ids.add(plugin['id'])
            verify_files(package, plugin['files'])
        if not {'render.desktopgl', 'definition.board-state', 'wasm', 'motion.definition'}.issubset(ids):
            raise ValueError(f'Missing native SDK capabilities: {sdk}')
        for license_name in ('MonoGame.LICENSE.txt', 'CordiSharp.LICENSE.txt'):
            if not (sdk / 'licenses' / license_name).is_file():
                raise ValueError(f'Missing native SDK license: {license_name}')
    return {'success': True, 'graphicsEntries': 3, 'nativeAuthorEntries': 2}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.root)))
