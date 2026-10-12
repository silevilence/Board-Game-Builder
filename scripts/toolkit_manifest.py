"""Complete, content-addressed toolkit manifest and deterministic component archives.

This module has no network side effects. Publication must verify the fixed sources
before making a product release public. toolkit.json is the manifest envelope;
its own integrity is provided by the containing ZIP / release checksum.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import zipfile


FORMAT = 'board-game-builder.toolkit'
ENTRIES = {'editor': 'editor/Editor.exe', 'cli': 'bgb.exe', 'player': 'player/Player.exe',
           'debugPlayer': 'debug-player/Player.exe', 'documentation': 'docs/author/entry.md'}
VERSION = r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)'
HASH = r'[0-9a-f]{64}'


def digest(file):
    with file.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def canonical(value):
    return (json.dumps(value, sort_keys=True, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def read(file):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f'Duplicate JSON key: {key}')
            result[key] = value
        return result
    return json.loads(file.read_text(encoding='utf-8-sig'), object_pairs_hook=unique)


def relative(value):
    if not isinstance(value, str) or not value or '\\' in value:
        raise ValueError(f'Invalid relative path: {value!r}')
    for part in value.split('/'):
        if (part in ('', '.', '..') or part[-1:] in (' ', '.') or
                re.search(r'[\x00-\x1f<>:"|?*]', part) or
                re.fullmatch(r'(?i)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?', part)):
            raise ValueError(f'Unsafe Windows path: {value!r}')
    return value


def identity(manifest):
    if not re.fullmatch(VERSION, manifest.get('version', '')) or manifest.get('platform') != 'win-x64':
        raise ValueError('Unsupported product version/platform')
    if manifest.get('entries') != ENTRIES:
        raise ValueError('Missing or unknown toolkit entries')
    if manifest.get('api') == 1:
        if manifest['version'] != '0.1.0' or 'format' in manifest or 'components' in manifest:
            raise ValueError('Only the original V0.1.0 API 1 layout is supported')
        return 'legacy'
    if manifest.get('api') != 2 or manifest.get('format') != FORMAT:
        raise ValueError('Unknown toolkit manifest format')
    if manifest.get('architecture') != 'x64' or manifest.get('packageMode') not in ('standalone', 'slim'):
        raise ValueError('Unsupported architecture/package mode')
    return 'components'


def inventory(root):
    result = {}
    folded = set()
    for path in sorted(root.rglob('*')):
        if path.is_symlink() or path.is_junction():
            raise ValueError(f'Linked release content: {path}')
        if not path.is_file():
            continue
        name = relative(path.relative_to(root).as_posix())
        if name.casefold() in folded:
            raise ValueError(f'Conflicting Windows path: {name}')
        folded.add(name.casefold())
        if name == 'toolkit.json':
            continue
        result[name] = {'path': name, 'size': path.stat().st_size, 'sha256': digest(path)}
    return result


def program_metadata(root, files):
    programs, ownership, libraries = [], {}, {}
    for name in sorted(files):
        if not name.endswith('.runtimeconfig.json'):
            continue
        config = read(root / name)['runtimeOptions']
        stem = name.removesuffix('.runtimeconfig.json')
        deps_path = stem + '.deps.json'
        if deps_path not in files or stem + '.exe' not in files:
            raise ValueError(f'Missing host/dependency manifest for {name}')
        deps = read(root / deps_path)
        frameworks = config.get('includedFrameworks', config.get('frameworks', []))
        if 'framework' in config:
            frameworks = [config['framework']]
        if not frameworks:
            raise ValueError(f'Missing runtime requirement: {name}')
        requirements = []
        for framework in frameworks:
            if (framework['name'] not in ('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App') or
                    not re.fullmatch(VERSION, framework['version'])):
                raise ValueError(f'Unsupported runtime requirement: {framework}')
            requirements.append({'framework': framework['name'], 'privateVersion': framework['version'],
                                 'minimumVersion': framework['version'], 'rollForward': 'LatestPatch'})
        parent = str(Path(name).parent).replace('\\', '/')
        prefix = '' if parent == '.' else parent + '/'
        used = {name, deps_path, stem + '.exe', stem + '.dll'}
        program_libraries = []
        for library, assets in deps['targets'][deps['runtimeTarget']['name']].items():
            lib_name, lib_version = library.rsplit('/', 1)
            lib_type = deps.get('libraries', {}).get(library, {}).get('type', 'runtimepack')
            libraries[library] = {'id': library, 'name': lib_name, 'version': lib_version, 'type': lib_type}
            program_libraries.append(library)
            for section in ('runtime', 'native', 'resources', 'runtimeTargets'):
                for asset in assets.get(section, {}):
                    # Runtime packages can list optional native debug symbols removed by publish.
                    if asset.lower().endswith('.pdb'):
                        continue
                    # SDK publish flattens runtime/native assets but preserves locale resources.
                    asset = relative(asset)
                    suffix = '/'.join(asset.split('/')[-2:]) if section == 'resources' else Path(asset).name
                    candidates = [prefix + asset, prefix + suffix]
                    found = next((p for p in candidates if p in files), None)
                    if found:
                        ownership.setdefault(found, set()).add(library)
                        used.add(found)
                    elif section != 'runtimeTargets':
                        raise ValueError(f'Missing published dependency: {name}: {asset}')
        if not used.issubset(files):
            raise ValueError(f'Missing managed entry: {stem}')
        programs.append({'entry': stem + '.exe', 'runtimeConfig': name, 'dependencies': deps_path,
                         'frameworks': requirements, 'libraries': sorted(program_libraries), 'files': sorted(used)})
    for role in ('editor', 'cli', 'player', 'debugPlayer'):
        if ENTRIES[role] not in {p['entry'] for p in programs}:
            raise ValueError(f'Missing program: {role}')
    return programs, ownership, sorted(libraries.values(), key=lambda x: x['id'])


def write_component(target, objects, root):
    partial = target.with_suffix('.partial')
    try:
        with zipfile.ZipFile(partial, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            for sha, path in sorted(objects.items()):
                info = zipfile.ZipInfo('objects/' + sha, (2020, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                with (root / path).open('rb') as source, archive.open(info, 'w', force_zip64=True) as destination:
                    shutil.copyfileobj(source, destination)
        if target.exists():
            if digest(target) != digest(partial):
                raise ValueError(f'Immutable component conflict: {target.name}')
        else:
            partial.rename(target)
    finally:
        partial.unlink(missing_ok=True)


def generate(root, output, repository):
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
        raise ValueError('Expected owner/repository')
    manifest = read(root / 'toolkit.json')
    files = inventory(root)
    programs, ownership, libraries = program_metadata(root, files)
    # Assign each byte identity exactly once, even when several programs use it.
    objects = {}
    for name, file in files.items():
        obj = objects.setdefault(file['sha256'], {'path': name, 'owners': set()})
        obj['owners'].update(ownership.get(name, []))
    groups = {}
    for sha, obj in objects.items():
        owners = sorted(obj['owners'])
        runtime = [o for o in owners if o.startswith('runtimepack.')]
        packages = [o for o in owners if next(l['type'] for l in libraries if l['id'] == o) == 'package']
        group = ('runtime:' + '|'.join(runtime) if runtime else
                 'dependency:' + '|'.join(packages) if packages else 'product')
        groups.setdefault(group, {})[sha] = obj['path']
    output.mkdir(parents=True, exist_ok=True)
    components, owners_by_hash = [], {}
    for group, content in sorted(groups.items()):
        content_id = hashlib.sha256(canonical({'group': group, 'objects': sorted(content)})).hexdigest()
        component_id = group.split(':')[0] + '-' + content_id
        filename = component_id + '.zip'
        target = output / filename
        write_component(target, content, root)
        components.append({'id': component_id, 'kind': group.split(':')[0], 'identity': group,
                           'objects': sorted(content), 'size': target.stat().st_size, 'sha256': digest(target),
                           'source': {'repository': repository, 'tag': 'components-' + content_id,
                                      'asset': filename}})
        for sha in content:
            owners_by_hash[sha] = component_id
    for name, file in files.items():
        file['component'] = owners_by_hash[file['sha256']]
        file['libraries'] = sorted(ownership.get(name, []))
    manifest.update({'api': 2, 'format': FORMAT, 'architecture': 'x64', 'packageMode': 'standalone',
                     'files': list(files.values()), 'components': components, 'programs': programs,
                     'libraries': libraries, 'runtimePlugins': read(root / 'player/release.json')['runtimePlugins']})
    validate(manifest, root, output)
    (root / 'toolkit.json').write_bytes(canonical(manifest))
    return manifest


def validate(manifest, root=None, archives=None):
    mode = identity(manifest)
    if mode == 'legacy':
        if root:
            for path in ENTRIES.values():
                if not (root / path).is_file():
                    raise ValueError(f'Missing legacy entry: {path}')
        return mode
    files, paths, all_objects = {}, set(), {}
    for file in manifest['files']:
        path = relative(file['path'])
        if path.casefold() == 'toolkit.json' or path.casefold() in paths:
            raise ValueError(f'Duplicate/conflicting file: {path}')
        paths.add(path.casefold())
        if type(file['size']) is not int or file['size'] < 0 or not re.fullmatch(HASH, file['sha256']):
            raise ValueError(f'Invalid content identity: {path}')
        if file['sha256'] in all_objects and all_objects[file['sha256']] != file['size']:
            raise ValueError('Conflicting size for the same content')
        all_objects[file['sha256']] = file['size']
        files[path] = file
    for path in files:
        parts = path.casefold().split('/')
        if any('/'.join(parts[:i]) in paths for i in range(1, len(parts))):
            raise ValueError(f'File/directory collision: {path}')
    components, assigned = {}, {}
    for component in manifest['components']:
        cid = component['id']
        if cid in components or component['kind'] not in ('runtime', 'dependency', 'product'):
            raise ValueError(f'Duplicate/invalid component: {cid}')
        content = component['objects']
        expected = hashlib.sha256(canonical({'group': component['identity'], 'objects': sorted(content)})).hexdigest()
        if (not content or len(content) != len(set(content)) or cid != component['kind'] + '-' + expected or
                not component['identity'].startswith(component['kind']) or
                type(component['size']) is not int or component['size'] <= 0 or
                not re.fullmatch(HASH, component['sha256'])):
            raise ValueError(f'Invalid component identity: {cid}')
        source = component['source']
        if (not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', source['repository']) or
                source['tag'] != 'components-' + expected or source['asset'] != cid + '.zip'):
            raise ValueError('Component source must be immutable and content-addressed')
        for sha in content:
            if sha not in all_objects or sha in assigned:
                raise ValueError('Missing/duplicate component object')
            assigned[sha] = cid
        components[cid] = component
        if archives:
            archive_path = archives / source['asset']
            if archive_path.stat().st_size != component['size'] or digest(archive_path) != component['sha256']:
                raise ValueError(f'Component archive integrity failure: {cid}')
            with zipfile.ZipFile(archive_path) as archive:
                if sorted(archive.namelist()) != sorted('objects/' + h for h in content):
                    raise ValueError('Component archive contents differ from manifest')
                for sha in content:
                    with archive.open('objects/' + sha) as stream:
                        if (archive.getinfo('objects/' + sha).file_size != all_objects[sha] or
                                hashlib.file_digest(stream, 'sha256').hexdigest() != sha):
                            raise ValueError('Component object integrity failure')
    for file in files.values():
        if assigned.get(file['sha256']) != file['component']:
            raise ValueError(f'Unresolved component for {file["path"]}')
    libraries = {lib['id']: lib for lib in manifest['libraries']}
    if len(libraries) != len(manifest['libraries']):
        raise ValueError('Duplicate library identity')
    for lib in libraries.values():
        if lib['id'] != lib['name'] + '/' + lib['version'] or not lib['version']:
            raise ValueError('Invalid library identity')
    for file in files.values():
        if not set(file['libraries']).issubset(libraries):
            raise ValueError('Unknown file library')
    entries = set()
    for program in manifest['programs']:
        if program['entry'] in entries:
            raise ValueError('Duplicate program entry')
        entries.add(program['entry'])
        required = {program['entry'], program['runtimeConfig'], program['dependencies']}
        if (not required.issubset(program['files']) or not set(program['files']).issubset(files) or
                not set(program['libraries']).issubset(libraries) or not program['frameworks']):
            raise ValueError('Unresolved program dependencies')
        frameworks = set()
        for requirement in program['frameworks']:
            framework = requirement['framework']
            if (framework in frameworks or framework not in ('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App') or
                    not re.fullmatch(VERSION, requirement['privateVersion']) or
                    requirement['minimumVersion'] != requirement['privateVersion'] or
                    requirement['rollForward'] != 'LatestPatch'):
                raise ValueError('Invalid runtime policy')
            frameworks.add(framework)
        if 'Microsoft.NETCore.App' not in frameworks:
            raise ValueError('Missing core framework')
    if not set(ENTRIES.values()).issubset(files) or not {ENTRIES[r] for r in ENTRIES if r != 'documentation'}.issubset(entries):
        raise ValueError('Missing required entries')
    if root:
        actual = inventory(root)
        if actual.keys() != files.keys():
            raise ValueError('File inventory has missing or unlisted paths')
        for name, file in actual.items():
            if (file['size'], file['sha256']) != (files[name]['size'], files[name]['sha256']):
                raise ValueError(f'File integrity failure: {name}')
        programs, ownership, actual_libraries = program_metadata(root, actual)
        if programs != manifest['programs'] or actual_libraries != manifest['libraries']:
            raise ValueError('Program metadata differs from published files')
        if any(sorted(ownership.get(name, [])) != files[name]['libraries'] for name in files):
            raise ValueError('File library ownership differs from dependency metadata')
        if manifest['runtimePlugins'] != read(root / 'player/release.json')['runtimePlugins']:
            raise ValueError('Runtime plugin identities differ from Player catalog')
    return mode


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['generate', 'verify'])
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--components', type=Path)
    parser.add_argument('--repository', default='silevilence/Board-Game-Builder')
    args = parser.parse_args()
    if args.command == 'generate':
        if args.components is None:
            parser.error('--components is required for generation')
        generate(args.root, args.components, args.repository)
    else:
        validate(read(args.root / 'toolkit.json'), args.root, args.components)
    print('Toolkit manifest verified.')
