"""Download an immutable published release, verify its closure and replay publication read-only."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import urllib.request

import component_release
import release
import toolkit_manifest as toolkit


def download(asset, target):
    if target.exists() and target.stat().st_size == asset['size'] and 'sha256:' + toolkit.digest(target) == asset.get('digest'):
        return False
    partial = target.with_suffix(target.suffix + '.partial')
    for attempt in range(3):
        try:
            request = urllib.request.Request(asset['browser_download_url'], headers={'User-Agent': 'BoardGameBuilder-release-verification'})
            with urllib.request.urlopen(request, timeout=120) as stream, partial.open('wb') as output:
                shutil.copyfileobj(stream, output)
            if partial.stat().st_size != asset['size'] or 'sha256:' + toolkit.digest(partial) != asset.get('digest'):
                raise ValueError('Downloaded asset integrity mismatch: ' + asset['name'])
            partial.replace(target)
            return True
        except (OSError, ValueError):
            partial.unlink(missing_ok=True)
            if attempt == 2:
                raise
            time.sleep(2 ** attempt)


def verify(source, tag, output, cache):
    repository = os.environ['GH_REPO']
    release.check(source, tag)
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=source, text=True).strip()
    remote = json.loads(release.gh('api', f'repos/{repository}/releases/tags/{tag}'))
    if remote['draft'] or remote['prerelease'] or remote['target_commitish'] != commit:
        raise ValueError('Published release does not match the checked-out source')
    output.mkdir(parents=True, exist_ok=True)
    cache.mkdir(parents=True, exist_ok=True)
    (output / 'components').mkdir(exist_ok=True)
    expected = {f'Board-Game-Builder-V{release.version(tag)}-{mode}-win-x64.zip{suffix}'
                for mode in ('standalone', 'slim') for suffix in ('', '.sha256')}
    expected.update(('toolkit-standalone.json', 'toolkit-slim.json'))
    if sorted(a['name'] for a in remote['assets']) != sorted(expected):
        raise ValueError('Published product attachment set is incomplete')
    for asset in remote['assets']:
        download(asset, output / asset['name'])
    components = {}
    for mode in ('standalone', 'slim'):
        manifest = toolkit.read(output / f'toolkit-{mode}.json')
        toolkit.validate(manifest)
        for component in manifest['components'] + manifest.get('exportDependencies', {}).get('components', []):
            old = components.setdefault(component['id'], component)
            if old != component:
                raise ValueError('Conflicting fixed component identity')
    records = []
    for component in components.values():
        origin = component['source']
        current = json.loads(release.gh('api', f"repos/{origin['repository']}/releases/tags/{origin['tag']}"))
        if current['draft'] or not current['prerelease'] or len(current['assets']) != 1:
            raise ValueError('Component is unavailable or is exposed as a product')
        asset = current['assets'][0]
        if asset['name'] != origin['asset'] or asset['size'] != component['size'] or asset.get('digest') != 'sha256:' + component['sha256']:
            raise ValueError('Fixed component attachment mismatch')
        target = cache / origin['asset']
        transferred = download(asset, target)
        component_release.verify_component(component, target)
        local = output / 'components' / target.name
        if not local.exists(): os.link(target, local)
        records.append({'id': component['id'], 'kind': component['kind'], 'identity': component['identity'],
                        'source': origin, 'assetId': asset['id'], 'sha256': component['sha256'],
                        'size': component['size'], 'downloaded': transferred})
    component_release.prepare(output, release.version(tag), commit, repository)
    # The workflow only grants contents:read. An unintended write cannot succeed.
    release.publish(source, tag, output)
    result = {'success': True, 'tag': tag, 'commit': commit, 'releaseId': remote['id'],
              'anonymousDownloads': True, 'publicationReplay': True,
              'productAssets': [{k:a[k] for k in ('id','name','size','digest')} for a in remote['assets']],
              'components': records, 'componentDownloadBytes': sum(c['size'] for c in records if c['downloaded'])}
    (output / 'verified.json').write_bytes(toolkit.canonical(result))
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--tag', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--cache', type=Path, required=True)
    args = parser.parse_args()
    result = verify(args.source.resolve(), args.tag, args.output.resolve(), args.cache.resolve())
    print(json.dumps({k:v for k,v in result.items() if k != 'components'}, indent=2))
