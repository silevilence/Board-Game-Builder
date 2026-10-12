import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).parents[1]))
import component_release as publication
import toolkit_manifest as toolkit
import compact_toolkit
import slim_toolkit
import test_slim_toolkit as fixtures


class PreparationTests(unittest.TestCase):
    def test_both_packages_and_export_closure_verified_before_writes(self):
        fixture = fixtures.SlimToolkitTests(); fixture.setUp(); self.addCleanup(fixture.doCleanups)
        output = fixture.source.parent / 'output'; output.mkdir()
        slim_toolkit.create(fixture.source, fixture.slim)
        toolkit.generate(fixture.slim, fixture.fixture.fixture.components, 'test/repo')
        import shutil
        shutil.copytree(fixture.fixture.fixture.components, output / 'components')
        for mode, source in [('standalone', fixture.source), ('slim', fixture.slim)]:
            root = output / mode
            compact_toolkit.prepare(source, root, fixture.fixture.entries)
            manifest = toolkit.generate(root, output / 'components', 'test/repo')
            compact_toolkit.compact(root)
            sidecar = output / ('toolkit-' + mode + '.json'); sidecar.write_bytes(toolkit.canonical(manifest))
            target = output / f'Board-Game-Builder-V0.1.0-{mode}-win-x64.zip'
            with zipfile.ZipFile(target, 'w') as archive:
                for path in {'toolkit.json'} | {f['storage'] for f in manifest['files']}:
                    archive.write(root / path, path)
            target.with_suffix('.zip.sha256').write_text(toolkit.digest(target) + '  ' + target.name + '\n')
        plan = publication.prepare(output, '0.1.0', 'abc', 'test/repo')
        self.assertEqual(len(plan['assets']), 6)
        self.assertTrue(any(c['kind'] == 'runtime' for c in plan['components']))
        self.assertGreater(len(plan['components']), len(plan['manifests'][1]['components']))
        missing = output / 'components' / plan['components'][0]['source']['asset']
        missing.unlink()
        with self.assertRaises(FileNotFoundError): publication.prepare(output, '0.1.0', 'abc', 'test/repo')

    def test_github_asset_limits(self):
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / 'asset'; file.write_bytes(b'ok')
            publication.check_limits([file])
            with self.assertRaises(ValueError): publication.check_limits([file, file])
            with patch.object(publication, 'MAX_ASSETS', 0), self.assertRaises(ValueError): publication.check_limits([file])
            with patch.object(publication, 'MAX_ASSET_BYTES', 2), self.assertRaises(ValueError): publication.check_limits([file])


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name); (self.output / 'components').mkdir()
        self.component = {'id': 'dependency-' + 'a' * 64, 'sha256': '', 'size': 1,
                          'source': {'repository': 'test/repo', 'tag': 'components-' + 'a' * 64, 'asset': 'component.zip'}}
        component = self.output / 'components/component.zip'; component.write_bytes(b'component fixture')
        self.component.update(sha256=toolkit.digest(component), size=component.stat().st_size)
        self.remotes = {}; self.uploads = []; self.failed_upload = None; self.corrupt = None; self.hide = None

    def plan(self, version='1.0.0'):
        files = []
        for suffix in ['standalone.zip', 'slim.zip', 'manifest.json']:
            file = self.output / (version + '-' + suffix); file.write_text(version + suffix); files.append(file)
        return {'components': [self.component], 'assets': files, 'manifests': [{'version': version}]}

    def publisher(self): return publication.Publisher(self.gh, 'test/repo', 'commit', self.output)

    def gh(self, *args):
        if args[0] == 'api':
            if '--method' in args:
                if args[2] == 'DELETE':
                    identity = int(args[3].rsplit('/',1)[-1])
                    for release in self.remotes.values():
                        release['assets'] = [a for a in release['assets'] if a.get('id') != identity]
                    return ''
                payload = json.loads(Path(args[-1]).read_text())
                method = args[args.index('--method') + 1]
                if method == 'POST':
                    key = len(self.remotes) + 1
                    remote = {**payload, 'id': key, 'assets': [], 'html_url': f'https://example.test/{key}', 'bytes': {}}
                    self.remotes[key] = remote
                else:
                    remote = self.remotes[int(args[3].rsplit('/', 1)[-1])]; remote.update(payload)
                return json.dumps({k: v for k, v in remote.items() if k != 'bytes'})
            if '--paginate' in args:
                releases = [{k: v for k, v in r.items() if k != 'bytes'} for r in self.remotes.values()]
                return json.dumps([releases[:1], releases[1:]])
            remote = self.remotes[int(args[1].rsplit('/', 1)[-1])]
            result = {k: v for k, v in remote.items() if k != 'bytes'}
            if self.hide and remote['tag_name'].startswith('components-') and any(not r['tag_name'].startswith('components-') for r in self.remotes.values()):
                result = {**result, 'assets': []}
            return json.dumps(result)
        action, tag = args[1:3]
        remote = next(r for r in self.remotes.values() if r['tag_name'] == tag)
        if action == 'upload':
            file = Path(args[3])
            if self.failed_upload == file.name:
                self.failed_upload = None; raise OSError('Interrupted upload')
            self.uploads.append((tag, file.name)); remote['bytes'][file.name] = file.read_bytes()
            remote['assets'].append({'name': file.name, 'size': file.stat().st_size, 'state': 'uploaded'})
            return ''
        if action == 'download':
            name = args[args.index('--pattern') + 1]; data = remote['bytes'][name]
            if self.corrupt == name: data = b'wrong'
            (Path(args[args.index('--dir') + 1]) / name).write_bytes(data); return ''
        raise AssertionError(args)

    def test_two_versions_reuse_one_component_and_rerun_is_idempotent(self):
        first = self.plan(); self.publisher().publish('V1.0.0', 'first', first)
        uploaded = len(self.uploads)
        self.publisher().publish('V1.0.0', 'first', first)
        self.assertEqual(len(self.uploads), uploaded)
        self.publisher().publish('V1.0.1', 'second', self.plan('1.0.1'))
        self.assertEqual(sum(tag.startswith('components-') for tag, _ in self.uploads), 1)
        self.assertEqual(len(self.uploads), uploaded + 3)
        self.assertTrue(all(not r['draft'] for r in self.remotes.values()))
        self.assertTrue(self.remotes[1]['prerelease'])
        self.assertEqual(self.remotes[1]['make_latest'], 'false')

    def test_interruption_resumes_without_replacing_uploaded_assets(self):
        plan = self.plan(); self.failed_upload = plan['assets'][1].name
        with self.assertRaises(OSError): self.publisher().publish('V1.0.0', 'notes', plan)
        self.assertTrue(self.remotes[2]['draft'])
        self.publisher().publish('V1.0.0', 'notes', plan)
        self.assertFalse(self.remotes[2]['draft'])
        self.assertEqual(len(set(self.uploads)), len(self.uploads))

    def test_rebuild_conflict_is_rejected_before_new_component_publication(self):
        plan = self.plan(); self.publisher().publish('V1.0.0', 'notes', plan)
        uploaded = len(self.uploads)
        plan['assets'][0].write_bytes(b'different rebuilt product')
        plan['components'][0]['source']['tag'] = 'components-unpublished-candidate'
        with self.assertRaisesRegex(ValueError, 'Missing/incomplete'):
            self.publisher().publish('V1.0.0', 'notes', plan)
        self.assertEqual(len(self.remotes), 2)
        self.assertEqual(len(self.uploads), uploaded)

    def test_corrupt_component_or_conflict_never_overwrites_public_assets(self):
        plan = self.plan(); self.publisher().publish('V1.0.0', 'notes', plan)
        count = len(self.uploads); self.corrupt = 'component.zip'
        with self.assertRaisesRegex(ValueError, 'refusing overwrite'): self.publisher().publish('V1.0.1', 'next', self.plan('1.0.1'))
        self.assertEqual(len(self.uploads), count)
        self.assertEqual(len(self.remotes), 2)
        self.corrupt = None
        with self.assertRaisesRegex(ValueError, 'conflict'): self.publisher().publish('V1.0.0', 'changed notes', plan)

    def test_empty_draft_upload_placeholder_is_retryable(self):
        plan = self.plan(); self.failed_upload = plan['assets'][0].name
        with self.assertRaises(OSError): self.publisher().publish('V1.0.0', 'notes', plan)
        self.remotes[2]['assets'].append({'id': 100, 'name': plan['assets'][0].name, 'size': 0, 'state': 'starter'})
        self.publisher().publish('V1.0.0', 'notes', plan)
        self.assertFalse(self.remotes[2]['draft'])
        self.assertTrue(all(a['state'] == 'uploaded' for a in self.remotes[2]['assets']))

    def test_dependency_disappearing_before_visibility_keeps_product_draft(self):
        self.hide = True
        with self.assertRaisesRegex(ValueError, 'Missing/incomplete'): self.publisher().publish('V1.0.0', 'notes', self.plan())
        self.assertTrue(self.remotes[2]['draft'])

    def test_missing_external_component_does_not_write_another_repository(self):
        self.component['source']['repository'] = 'other/repo'
        with self.assertRaisesRegex(ValueError, 'external component'): self.publisher().publish('V1.0.0', 'notes', self.plan())
        self.assertFalse(self.remotes)


if __name__ == '__main__': unittest.main()
