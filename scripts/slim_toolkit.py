"""Derive the system-runtime variant from the same self-contained publish bytes."""
import argparse
import copy
from pathlib import Path
import shutil

import toolkit_manifest as toolkit


def create(source, target):
    original = toolkit.read(source / 'toolkit.json')
    toolkit.validate(original, source)
    if original.get('layout') or original.get('packageMode') != 'standalone':
        raise ValueError('Slim input must be the complete, unpacked standalone publish output')
    if target.exists() or target.resolve().is_relative_to(source.resolve()):
        raise ValueError('Choose a new output outside the original distribution')
    target.mkdir(parents=True)
    for file in original['files']:
        owners = file['libraries']
        if owners and all(owner.startswith('runtimepack.') for owner in owners):
            continue
        destination = target / file['path']
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / file['path'], destination)
    for program in original['programs']:
        config = toolkit.read(target / program['runtimeConfig'])
        options = config['runtimeOptions']
        frameworks = options.pop('includedFrameworks')
        options['frameworks'] = frameworks
        options['rollForward'] = 'LatestPatch'
        (target / program['runtimeConfig']).write_bytes(toolkit.canonical(config))
        deps = toolkit.read(target / program['dependencies'])
        for libraries in deps['targets'].values():
            for name in list(libraries):
                if name.startswith('runtimepack.'):
                    del libraries[name]
                    continue
                dependencies = libraries[name].get('dependencies', {})
                for dependency in list(dependencies):
                    if dependency.startswith('runtimepack.'):
                        del dependencies[dependency]
        for name in list(deps.get('libraries', {})):
            if name.startswith('runtimepack.'):
                del deps['libraries'][name]
        (target / program['dependencies']).write_bytes(toolkit.canonical(deps))
    player = toolkit.read(source / 'player/release.json')
    expected = {'player/' + name for name in player['files']} | {'player/release.json'}
    export_files = [{'path': f['path'].removeprefix('player/'), 'size': f['size'], 'sha256': f['sha256'], 'component': f['component']}
                    for f in original['files'] if f['path'] in expected]
    if len(export_files) != len(expected):
        raise ValueError('Formal Player export inventory is incomplete')
    component_ids = {f['component'] for f in export_files}
    descriptor = {'api': 1, 'format': 'board-game-builder.player-components', 'version': original['version'],
                  'platform': original['platform'], 'files': export_files,
                  'components': [copy.deepcopy(c) for c in original['components'] if c['id'] in component_ids]}
    (target / 'player/export.dependencies.json').write_bytes(toolkit.canonical(descriptor))
    manifest = {key: original[key] for key in ('api', 'format', 'version', 'platform', 'architecture', 'commit', 'entries')}
    manifest['packageMode'] = 'slim'
    (target / 'toolkit.json').write_bytes(toolkit.canonical(manifest))
    return descriptor


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    create(args.source, args.output)
