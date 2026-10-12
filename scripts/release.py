"""Version checks, deterministic archive and draft-first GitHub publication (stdlib only)."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
import xml.etree.ElementTree as ET
import zipfile
import toolkit_manifest
import component_release


def version(tag):
    if not re.fullmatch(r"[Vv](0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", tag):
        raise ValueError("Expected a formal Vmajor.minor.patch tag without leading zeros")
    return tag[1:]


def notes(text, tag):
    wanted = version(tag)
    headings = list(re.finditer(r"^##[ \t]+([^\r\n]+)", text, re.M))
    matches = [i for i, h in enumerate(headings) if h[1].strip().lower() == 'v' + wanted]
    if len(matches) != 1:
        raise ValueError("changelog must contain exactly one matching version heading")
    i = matches[0]
    body = text[headings[i].end():headings[i+1].start() if i+1 < len(headings) else len(text)].strip()
    if not re.sub(r"(?m)^\s*#+.*$", "", body).strip():
        raise ValueError("changelog version content is empty")
    return body + '\n'


def check(repo, tag):
    product = ET.parse(repo / 'Directory.Build.props').findtext('.//Version')
    if product != version(tag):
        raise ValueError(f"Product version {product} does not match {tag}")
    return notes((repo / 'changelog.md').read_text(encoding='utf-8-sig'), tag)


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def rename_archive(staging, target):
    # Windows scanners/readers can briefly retain the just-closed ZIP. Never
    # overwrite a target or retry an unrelated filesystem error.
    for attempt in range(10):
        try:
            staging.rename(target)
            return
        except PermissionError as error:
            if getattr(error, 'winerror', None) not in (32, 33) or attempt == 9:
                raise
            time.sleep(0.2)


ENTRIES = {'editor': 'editor/Editor.exe', 'cli': 'bgb.exe', 'player': 'player/Player.exe',
           'debugPlayer': 'debug-player/Player.exe', 'documentation': 'docs/author/entry.md'}


def verify(root, tag):
    manifest = json.loads((root / 'toolkit.json').read_text(encoding='utf-8-sig'))
    toolkit_manifest.validate(manifest, root)
    if (manifest['version'], manifest['platform'], manifest['entries']) != (version(tag), 'win-x64', ENTRIES):
        raise ValueError('Toolkit identity/entries mismatch')
    prefix = '.payload/' if manifest.get('layout') == 'hardlinks-v1' else ''
    files = {f['path']: f for f in manifest.get('files', [])}
    def stored(path):
        name = prefix + path
        return root / files.get(name, {}).get('storage', name)
    for relative in list(ENTRIES.values()) + ['templates/bgb-author/SKILL.md.template']:
        if not stored(relative).is_file():
            raise ValueError(f'Missing toolkit content: {relative}')
    for folder in ([] if manifest.get('packageMode') == 'slim' else ['', 'editor/', 'player/', 'debug-player/', 'worker/', 'editor/worker/', 'player/worker/', 'debug-player/worker/']):
        for native in ['coreclr.dll', 'hostfxr.dll']:
            if not stored(folder + native).is_file():
                raise ValueError(f'Missing self-contained runtime: {folder}{native}')
    player = json.loads(stored('player/release.json').read_text(encoding='utf-8-sig'))
    if player['version'] != version(tag) or player['editorDebug'] or not player['guiEntry']:
        raise ValueError('Formal Player identity mismatch')
    export_files = {f['path']: f for f in manifest.get('exportDependencies', {}).get('files', [])}
    for relative, digest in player['files'].items():
        actual = export_files.get(relative, {}).get('sha256') if manifest.get('packageMode') == 'slim' else sha(stored('player/' + relative))
        if actual is None or actual.lower() != digest.lower():
            raise ValueError(f'Formal Player hash mismatch: {relative}')
    return manifest


def archive(repo, root, tag, output, component_format=False):
    body = check(repo, tag)
    # PowerShell hashtable iteration order varies between processes. Canonicalize generated
    # metadata so an interrupted upload can be resumed from a rebuild of the same commit.
    current = toolkit_manifest.read(root / 'toolkit.json')
    metadata = ['toolkit.json'] if current.get('api') == 2 else ['toolkit.json', 'player/release.json']
    for relative in metadata:
        path = root / relative
        content = json.loads(path.read_text(encoding='utf-8-sig'))
        path.write_text(json.dumps(content, sort_keys=True, indent=2) + '\n', encoding='utf-8', newline='\n')
    if component_format and not current.get('layout'):
        toolkit_manifest.generate(root, output / 'components', os.environ.get('GH_REPO', 'silevilence/Board-Game-Builder'))
    manifest = verify(root, tag)
    output.mkdir(parents=True, exist_ok=True)
    suffix = '-' + manifest['packageMode'] if manifest.get('api') == 2 else ''
    name = f'Board-Game-Builder-V{version(tag)}{suffix}-win-x64.zip'
    target = output / name
    if target.exists():
        raise ValueError(f'Archive already exists: {target}')
    staging = target.with_suffix('.zip.partial')
    archived_files = ([root / p for p in sorted({'toolkit.json'} | {f['storage'] for f in manifest['files']})]
                      if manifest.get('layout') == 'hardlinks-v1' else sorted(root.rglob('*')))
    with zipfile.ZipFile(staging, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for file in archived_files:
            if file.is_symlink():
                raise ValueError(f'Linked release content: {file}')
            if file.is_file():
                info = zipfile.ZipInfo(file.relative_to(root).as_posix(), (2020, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                with file.open('rb') as source, z.open(info, 'w', force_zip64=True) as destination:
                    import shutil
                    shutil.copyfileobj(source, destination)
    rename_archive(staging, target)
    (output / (name + '.sha256')).write_text(f'{sha(target)}  {name}\n', encoding='utf-8')
    (output / ('toolkit' + suffix + '.json')).write_text(json.dumps(manifest, sort_keys=True, indent=2) + '\n', encoding='utf-8', newline='\n')
    (output / 'release-notes.md').write_text(body, encoding='utf-8')


def gh(*args):
    return subprocess.check_output(['gh', *args], text=True, encoding='utf-8').strip()


def publish(repo, tag, output):
    body = check(repo, tag)
    repository = os.environ['GH_REPO']
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repo, text=True).strip()
    # Resolve every equivalent tag, including annotated tags, before any mutation.
    refs = json.loads(gh('api', f'repos/{repository}/git/matching-refs/tags/'))
    for ref in refs:
        other = ref['ref'].removeprefix('refs/tags/')
        if other.lower() != tag.lower():
            continue
        obj = ref['object']
        while obj['type'] == 'tag':
            obj = json.loads(gh('api', f"repos/{repository}/git/tags/{obj['sha']}"))['object']
        if obj['sha'] != commit:
            raise ValueError('Equivalent version tag points to another commit')
    if not any(r['ref'] == 'refs/tags/' + tag for r in refs):
        raise ValueError('Remote tag is missing')
    if any((output / ('toolkit-' + mode + '.json')).exists() for mode in ('standalone', 'slim')):
        plan = component_release.prepare(output, version(tag), commit, repository)
        print(component_release.Publisher(gh, repository, commit, output).publish(tag, body, plan))
        return
    releases = json.loads(gh('api', '--paginate', '--slurp', f'repos/{repository}/releases?per_page=100'))
    existing = [r for page in releases for r in page if r['tag_name'].lower() == tag.lower()]
    if len(existing) > 1:
        raise ValueError('Duplicate equivalent releases')
    name = f'Board-Game-Builder-V{version(tag)}-win-x64.zip'
    files = [output / name, output / (name + '.sha256'), output / 'toolkit.json']
    manifest = json.loads(files[2].read_text(encoding='utf-8'))
    if manifest.get('api', 1) != 1:
        raise ValueError('Component publication must verify all referenced assets before publishing API 2')
    if manifest['commit'] != commit or manifest['version'] != version(tag):
        raise ValueError('Artifact commit/version mismatch')
    if files[1].read_text().strip() != f'{sha(files[0])}  {name}':
        raise ValueError('ZIP checksum mismatch')
    with zipfile.ZipFile(files[0]) as z:
        if json.loads(z.read('toolkit.json').decode('utf-8-sig')) != manifest:
            raise ValueError('Archive manifest differs from sidecar')
    if existing:
        release = existing[0]
        if release['body'].replace('\r\n', '\n').strip() != body.strip() or release['target_commitish'] != commit:
            raise ValueError('Existing release notes/commit conflict')
        tag = release['tag_name']
    else:
        # Use the creation response, not an eventually consistent list immediately after creation.
        request = output / 'draft-request.json'
        request.write_text(json.dumps({'tag_name': tag, 'target_commitish': commit,
                                      'name': 'V' + version(tag), 'body': body, 'draft': True}), encoding='utf-8')
        release = json.loads(gh('api', '--method', 'POST', f'repos/{repository}/releases', '--input', str(request)))
    expected = {f.name: f for f in files}
    if any(a['name'] not in expected for a in release['assets']):
        raise ValueError('Unexpected existing release attachment')
    for file in files:
        asset = next((a for a in release['assets'] if a['name'] == file.name), None)
        if asset:
            with tempfile.TemporaryDirectory() as temp:
                gh('release', 'download', tag, '--repo', repository, '--pattern', file.name, '--dir', temp)
                if sha(Path(temp) / file.name) != sha(file):
                    raise ValueError(f'Existing asset conflict: {file.name}; refusing overwrite')
        elif release['draft']:
            gh('release', 'upload', tag, str(file), '--repo', repository)
        else:
            raise ValueError('Published release is incomplete; refusing modification')
    # Read back bytes for all attachments before publishing, including newly uploaded ones.
    with tempfile.TemporaryDirectory() as temp:
        gh('release', 'download', tag, '--repo', repository, '--dir', temp)
        if any(sha(Path(temp) / f.name) != sha(f) for f in files):
            raise ValueError('Uploaded attachment verification failed')
    if release['draft']:
        gh('release', 'edit', tag, '--repo', repository, '--draft=false', '--latest')
    print(gh('release', 'view', tag, '--repo', repository, '--json', 'url', '--jq', '.url'))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('command', choices=['check', 'archive', 'plan', 'publish'])
    parser.add_argument('--tag', required=True)
    parser.add_argument('--root', type=Path)
    parser.add_argument('--output', type=Path, default=Path('artifacts/release'))
    parser.add_argument('--component-format', action='store_true')
    args = parser.parse_args()
    repository = Path(__file__).resolve().parent.parent
    if args.command == 'check':
        print(check(repository, args.tag))
    elif args.command == 'archive':
        archive(repository, args.root, args.tag, args.output, args.component_format)
    elif args.command == 'plan':
        check(repository, args.tag)
        commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repository, text=True).strip()
        plan = component_release.prepare(args.output, version(args.tag), commit, os.environ.get('GH_REPO', 'silevilence/Board-Game-Builder'))
        print(json.dumps({'version': version(args.tag), 'commit': commit, 'assets': [p.name for p in plan['assets']],
                          'components': [{'id': c['id'], 'source': c['source'], 'size': c['size'], 'sha256': c['sha256']} for c in plan['components']]}, indent=2))
    else:
        publish(repository, args.tag, args.output)
