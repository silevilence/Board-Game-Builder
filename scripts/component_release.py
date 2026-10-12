"""Immutable component publication; product drafts stay private until every reference verifies."""
import hashlib
import json
from pathlib import Path
import tempfile
import zipfile

import toolkit_manifest as toolkit

MAX_ASSETS = 1000
MAX_ASSET_BYTES = 2 * 1024**3  # GitHub requires each asset to be strictly below 2 GiB.


def check_limits(files):
    if len(files) > MAX_ASSETS or len({p.name for p in files}) != len(files):
        raise ValueError('Too many or duplicate release assets')
    for file in files:
        if not 0 < file.stat().st_size < MAX_ASSET_BYTES:
            raise ValueError('Release asset exceeds GitHub size limit: ' + file.name)


def verify_component(component, path):
    if path.stat().st_size != component['size'] or toolkit.digest(path) != component['sha256']:
        raise ValueError('Component archive integrity failure: ' + component['id'])
    with zipfile.ZipFile(path) as archive:
        expected = ['objects/' + h for h in component['objects']]
        if sorted(archive.namelist()) != sorted(expected):
            raise ValueError('Unexpected component archive entries')
        for entry in archive.infolist():
            if (entry.external_attr >> 16) & 0xf000 == 0xa000:
                raise ValueError('Linked component archive entry')
            with archive.open(entry) as stream:
                if hashlib.file_digest(stream, 'sha256').hexdigest() != entry.filename.removeprefix('objects/'):
                    raise ValueError('Component object integrity failure')


def prepare(output, version, commit, repository):
    """Read-only validation of both products and the complete component closure."""
    manifests, assets, components = [], [], {}
    for mode in ('standalone', 'slim'):
        sidecar = output / ('toolkit-' + mode + '.json')
        manifest = toolkit.read(sidecar)
        toolkit.validate(manifest)
        if (manifest['packageMode'], manifest['version'], manifest['commit'], manifest.get('layout')) != (mode, version, commit, 'hardlinks-v1'):
            raise ValueError('Artifact commit/version/mode mismatch')
        archive = output / f'Board-Game-Builder-V{version}-{mode}-win-x64.zip'
        checksum = archive.with_suffix('.zip.sha256')
        if checksum.read_text(encoding='utf-8').strip() != f'{toolkit.digest(archive)}  {archive.name}':
            raise ValueError('ZIP checksum mismatch')
        files = {f['path']: f for f in manifest['files']}
        stored = {f['storage'] for f in files.values()}
        with zipfile.ZipFile(archive) as zipped:
            if sorted(zipped.namelist()) != sorted(stored | {'toolkit.json'}):
                raise ValueError('Unexpected or duplicate product ZIP entries')
            if json.loads(zipped.read('toolkit.json')) != manifest:
                raise ValueError('Archive manifest differs from sidecar')
            for entry in zipped.infolist():
                if (entry.external_attr >> 16) & 0xf000 == 0xa000:
                    raise ValueError('Linked product ZIP entry')
                if entry.filename == 'toolkit.json':
                    continue
                record = files[entry.filename]
                with zipped.open(entry) as stream:
                    if entry.file_size != record['size'] or hashlib.file_digest(stream, 'sha256').hexdigest() != record['sha256']:
                        raise ValueError('Product ZIP content differs from manifest')
        for component in manifest['components'] + manifest.get('exportDependencies', {}).get('components', []):
            source = component['source']
            key = (source['repository'], source['tag'], source['asset'])
            previous = components.setdefault(key, component)
            if previous != component:
                raise ValueError('Conflicting immutable component references')
        manifests.append(manifest)
        assets.extend((archive, checksum, sidecar))
    # Both modes must come from the same published managed programs, plugins and docs.
    primary = {f['path']: f for f in manifests[0]['files']}
    slim = {f['path']: f for f in manifests[1]['files']}
    for path, file in primary.items():
        if file['libraries'] and all(library.startswith('runtimepack.') for library in file['libraries']):
            if path in slim:
                raise ValueError('Slim package contains a private runtime file')
            continue
        if path not in slim:
            raise ValueError('Slim package omits product content: ' + path)
        if path == '.payload/toolkit.json' or path.endswith(('.runtimeconfig.json', '.deps.json')):
            continue
        if (file['size'], file['sha256']) != (slim[path]['size'], slim[path]['sha256']):
            raise ValueError('Package modes contain different product code/content: ' + path)
    if set(slim) - set(primary) != {'.payload/player/export.dependencies.json'}:
        raise ValueError('Unexpected slim-only product content')
    check_limits(assets)
    for component in components.values():
        file = output / 'components' / component['source']['asset']
        check_limits([file])
        verify_component(component, file)
    return {'assets': assets, 'components': list(components.values()), 'manifests': manifests}


class Publisher:
    def __init__(self, gh, repository, commit, output):
        self.gh, self.repository, self.commit, self.output = gh, repository, commit, output
        self.releases = {}

    def list_releases(self, repository):
        if repository not in self.releases:
            pages = json.loads(self.gh('api', '--paginate', '--slurp', f'repos/{repository}/releases?per_page=100'))
            self.releases[repository] = [r for page in pages for r in page]
        return self.releases[repository]

    def find(self, repository, tag):
        matches = [r for r in self.list_releases(repository) if r['tag_name'].casefold() == tag.casefold()]
        if len(matches) > 1:
            raise ValueError('Duplicate equivalent releases: ' + tag)
        return matches[0] if matches else None

    def request(self, repository, endpoint, payload, method):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'request.json'
            path.write_bytes(toolkit.canonical(payload))
            return json.loads(self.gh('api', '--method', method, f'repos/{repository}/{endpoint}', '--input', str(path)))

    def verify_remote(self, repository, release, expected):
        """Always read bytes; API digest metadata alone does not prove availability."""
        with tempfile.TemporaryDirectory() as temporary:
            for name, (size, digest) in expected.items():
                asset = next((a for a in release['assets'] if a['name'] == name), None)
                if asset is None or asset['size'] != size or asset.get('state', 'uploaded') != 'uploaded':
                    raise ValueError('Missing/incomplete release asset: ' + name)
                self.gh('release', 'download', release['tag_name'], '--repo', repository,
                        '--pattern', name, '--dir', temporary)
                file = Path(temporary) / name
                if file.stat().st_size != size or toolkit.digest(file) != digest:
                    raise ValueError('Existing asset conflict; refusing overwrite: ' + name)

    def refresh(self, repository, release):
        return json.loads(self.gh('api', f"repos/{repository}/releases/{release['id']}"))

    def transfer(self, repository, release, files):
        expected = {p.name: (p.stat().st_size, toolkit.digest(p)) for p in files}
        names = [a['name'] for a in release['assets']]
        if len(names) != len(set(names)) or not set(names).issubset(expected):
            raise ValueError('Unexpected existing release attachment')
        # GitHub can leave a zero-byte starter after an interrupted upload. Only
        # unpublished placeholders owned by this publisher may be removed for retry.
        starters = [a for a in release['assets'] if a.get('state') == 'starter' and a['size'] == 0]
        if starters and release['draft'] and repository == self.repository:
            for asset in starters:
                self.gh('api', '--method', 'DELETE', f"repos/{repository}/releases/assets/{asset['id']}")
            release = self.refresh(repository, release)
            names = [a['name'] for a in release['assets']]
        self.verify_remote(repository, release, {name: expected[name] for name in names})
        for file in files:
            if file.name in names:
                continue
            if not release['draft']:
                raise ValueError('Published release is incomplete; refusing modification')
            self.gh('release', 'upload', release['tag_name'], str(file), '--repo', repository)
        release = self.refresh(repository, release)
        self.verify_remote(repository, release, expected)
        return release

    def publish_component(self, component):
        source = component['source']
        repository, tag = source['repository'], source['tag']
        file = self.output / 'components' / source['asset']
        release = self.find(repository, tag)
        body = ('Internal immutable component. Not an installable product.\n\n'
                f"Identity: {component['id']}\nSHA-256: {component['sha256']}\n"
                'Retain this release and its attachment while referenced by any product. Never replace or delete it.\n')
        if release is None:
            if repository != self.repository:
                raise ValueError('Referenced external component is unavailable: ' + tag)
            release = self.request(repository, 'releases', {'tag_name': tag, 'target_commitish': self.commit,
                'name': component['id'], 'body': body, 'draft': True, 'prerelease': True, 'make_latest': 'false'}, 'POST')
            self.list_releases(repository).append(release)
        elif release['body'].replace('\r\n', '\n').strip() != body.strip() or not release['prerelease']:
            raise ValueError('Existing component release metadata conflict: ' + tag)
        if repository != self.repository and release['draft']:
            raise ValueError('Referenced external component is not published: ' + tag)
        release = self.transfer(repository, release, [file])
        if release['draft']:
            release = self.request(repository, f"releases/{release['id']}", {'draft': False, 'make_latest': 'false'}, 'PATCH')
        if release['draft']:
            raise ValueError('Component is still private')
        self.verify_remote(repository, release, {file.name: (component['size'], component['sha256'])})

    def publish(self, tag, body, plan):
        release = self.find(self.repository, tag)
        if release is not None and (release['body'].replace('\r\n', '\n').strip() != body.strip() or
                release['target_commitish'] != self.commit or release['prerelease']):
            raise ValueError('Existing product release notes/commit conflict')
        if release is not None and not release['draft']:
            # Reject a rebuild conflict before publishing otherwise orphaned components.
            self.verify_remote(self.repository, release,
                {p.name: (p.stat().st_size, toolkit.digest(p)) for p in plan['assets']})
        for component in plan['components']:
            self.publish_component(component)
        if release is None:
            release = self.request(self.repository, 'releases', {'tag_name': tag, 'target_commitish': self.commit,
                'name': 'V' + plan['manifests'][0]['version'], 'body': body, 'draft': True, 'prerelease': False}, 'POST')
        release = self.transfer(self.repository, release, plan['assets'])
        # Recheck the complete cross-release closure immediately before product visibility.
        for component in plan['components']:
            source = component['source']
            current = self.refresh(source['repository'], self.find(source['repository'], source['tag']))
            if current['draft']:
                raise ValueError('Referenced component is no longer public')
            self.verify_remote(source['repository'], current, {source['asset']: (component['size'], component['sha256'])})
        if release['draft']:
            release = self.request(self.repository, f"releases/{release['id']}", {'draft': False, 'make_latest': 'true'}, 'PATCH')
        if release['draft']:
            raise ValueError('Product is still private')
        return release['html_url']
