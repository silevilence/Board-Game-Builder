"""Version checks, deterministic archive and draft-first GitHub publication (stdlib only)."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


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


ENTRIES = {'editor': 'editor/Editor.exe', 'cli': 'bgb.exe', 'player': 'player/Player.exe',
           'debugPlayer': 'debug-player/Player.exe', 'documentation': 'docs/author/entry.md'}


def verify(root, tag):
    manifest = json.loads((root / 'toolkit.json').read_text(encoding='utf-8-sig'))
    if (manifest['api'], manifest['version'], manifest['platform'], manifest['entries']) != (1, version(tag), 'win-x64', ENTRIES):
        raise ValueError('Toolkit identity/entries mismatch')
    for relative in list(ENTRIES.values()) + ['templates/bgb-author/SKILL.md.template']:
        if not (root / relative).is_file():
            raise ValueError(f'Missing toolkit content: {relative}')
    for folder in ['', 'editor/', 'player/', 'debug-player/', 'worker/', 'editor/worker/', 'player/worker/', 'debug-player/worker/']:
        for native in ['coreclr.dll', 'hostfxr.dll']:
            if not (root / folder / native).is_file():
                raise ValueError(f'Missing self-contained runtime: {folder}{native}')
    player = json.loads((root / 'player/release.json').read_text(encoding='utf-8-sig'))
    if player['version'] != version(tag) or player['editorDebug'] or not player['guiEntry']:
        raise ValueError('Formal Player identity mismatch')
    for relative, digest in player['files'].items():
        if sha(root / 'player' / relative).lower() != digest.lower():
            raise ValueError(f'Formal Player hash mismatch: {relative}')
    return manifest


def archive(repo, root, tag, output):
    body = check(repo, tag)
    manifest = verify(root, tag)
    # PowerShell hashtable iteration order varies between processes. Canonicalize generated
    # metadata so an interrupted upload can be resumed from a rebuild of the same commit.
    for relative in ['toolkit.json', 'player/release.json']:
        path = root / relative
        content = json.loads(path.read_text(encoding='utf-8-sig'))
        path.write_text(json.dumps(content, sort_keys=True, indent=2) + '\n', encoding='utf-8', newline='\n')
    output.mkdir(parents=True, exist_ok=True)
    name = f'Board-Game-Builder-V{version(tag)}-win-x64.zip'
    target = output / name
    if target.exists():
        raise ValueError(f'Archive already exists: {target}')
    staging = target.with_suffix('.zip.partial')
    with zipfile.ZipFile(staging, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for file in sorted(root.rglob('*')):
            if file.is_symlink():
                raise ValueError(f'Linked release content: {file}')
            if file.is_file():
                info = zipfile.ZipInfo(file.relative_to(root).as_posix(), (2020, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                with file.open('rb') as source, z.open(info, 'w', force_zip64=True) as destination:
                    import shutil
                    shutil.copyfileobj(source, destination)
    staging.rename(target)
    (output / (name + '.sha256')).write_text(f'{sha(target)}  {name}\n', encoding='utf-8')
    (output / 'toolkit.json').write_text(json.dumps(manifest, sort_keys=True, indent=2) + '\n', encoding='utf-8', newline='\n')
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
    releases = json.loads(gh('api', '--paginate', '--slurp', f'repos/{repository}/releases?per_page=100'))
    existing = [r for page in releases for r in page if r['tag_name'].lower() == tag.lower()]
    if len(existing) > 1:
        raise ValueError('Duplicate equivalent releases')
    name = f'Board-Game-Builder-V{version(tag)}-win-x64.zip'
    files = [output / name, output / (name + '.sha256'), output / 'toolkit.json']
    manifest = json.loads(files[2].read_text(encoding='utf-8'))
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
    parser.add_argument('command', choices=['check', 'archive', 'publish'])
    parser.add_argument('--tag', required=True)
    parser.add_argument('--root', type=Path)
    parser.add_argument('--output', type=Path, default=Path('artifacts/release'))
    args = parser.parse_args()
    repository = Path(__file__).resolve().parent.parent
    if args.command == 'check':
        print(check(repository, args.tag))
    elif args.command == 'archive':
        archive(repository, args.root, args.tag, args.output)
    else:
        publish(repository, args.tag, args.output)
