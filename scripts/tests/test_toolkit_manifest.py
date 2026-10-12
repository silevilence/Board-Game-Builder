import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).parents[1]))
import toolkit_manifest as toolkit


class ManifestTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'toolkit'
        self.components = Path(self.temp.name) / 'components'
        self.root.mkdir()
        self.put('toolkit.json', {'api': 1, 'version': '0.1.0', 'platform': 'win-x64',
                                  'entries': toolkit.ENTRIES, 'commit': 'abc'})
        self.put('player/release.json', {'runtimePlugins': {'core': {'api': 1, 'version': '1.0.0'}}})
        self.put('docs/author/entry.md', 'Author documentation')
        for name in [p for r, p in toolkit.ENTRIES.items() if r != 'documentation'] + ['worker/Lua.exe']:
            stem = name[:-4]
            parent = Path(name).parent.as_posix()
            prefix = '' if parent == '.' else parent + '/'
            self.put(name, 'native host:' + name)
            self.put(stem + '.dll', 'managed entry:' + name)
            self.put(prefix + 'Common.dll', 'shared dependency bytes')
            self.put(prefix + 'coreclr.dll', 'runtime bytes')
            self.put(stem + '.runtimeconfig.json', {'runtimeOptions': {'includedFrameworks': [
                {'name': 'Microsoft.NETCore.App', 'version': '10.0.12'}]}})
            self.put(stem + '.deps.json', {
                'runtimeTarget': {'name': 'target'}, 'libraries': {'Common/3.2.1': {'type': 'package'}},
                'targets': {'target': {'Common/3.2.1': {'runtime': {'lib/net10.0/Common.dll': {}}},
                    'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.12': {'native': {'coreclr.dll': {}}}}}})
        self.manifest = toolkit.generate(self.root, self.components, 'test/repo')

    def put(self, path, value):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(toolkit.canonical(value) if isinstance(value, dict) else value.encode())

    def test_complete_inventory_and_offline_dependency_resolution(self):
        self.assertEqual(toolkit.validate(self.manifest, self.root, self.components), 'components')
        self.assertEqual(len(self.manifest['programs']), 5)
        common = [f for f in self.manifest['files'] if f['path'].endswith('Common.dll')]
        self.assertEqual(len(common), 5)
        self.assertEqual(len({f['sha256'] for f in common}), 1)
        self.assertEqual(len({f['component'] for f in common}), 1)
        self.assertIn('Common/3.2.1', [l['id'] for l in self.manifest['libraries']])

    def test_rebuild_stable_and_only_changed_component_replaced(self):
        rebuilt = toolkit.generate(self.root, self.components, 'test/repo')
        self.assertEqual(self.manifest, rebuilt)
        self.put('bgb.dll', 'new CLI implementation')
        updated = toolkit.generate(self.root, self.components, 'test/repo')
        stable = lambda m: [c for c in m['components'] if c['kind'] != 'product']
        self.assertEqual(stable(self.manifest), stable(updated))
        self.assertNotEqual(self.manifest['files'], updated['files'])

    def test_missing_extra_changed_and_same_name_different_content(self):
        for action in ('missing', 'extra', 'changed'):
            with self.subTest(action=action):
                path = self.root / ('unknown.dll' if action == 'extra' else 'bgb.dll')
                old = path.read_bytes() if path.exists() else None
                if action == 'missing':
                    path.unlink()
                else:
                    path.write_bytes(b'corrupt')
                with self.assertRaises(ValueError):
                    toolkit.validate(self.manifest, self.root)
                if old is None:
                    path.unlink()
                else:
                    path.write_bytes(old)
        self.put('editor/Common.dll', 'different content with the same basename')
        updated = toolkit.generate(self.root, self.components, 'test/repo')
        common = [f for f in updated['files'] if f['path'].endswith('Common.dll')]
        self.assertEqual(len({f['sha256'] for f in common}), 2)

    def test_invalid_or_conflicting_paths(self):
        for path in ('../escape', '/rooted', 'C:/file', 'a\\b', 'a/../b', 'CON.txt', 'a:stream', 'a.',
                     'a//b', 'a/ ', 'BGB.EXE', 'editor'):
            with self.subTest(path=path):
                changed = copy.deepcopy(self.manifest)
                changed['files'].append({**changed['files'][0], 'path': path})
                with self.assertRaises(ValueError):
                    toolkit.validate(changed)

    def test_illegal_dependency_and_runtime_policy(self):
        mutations = [lambda m: m['programs'][0]['files'].append('missing.dll'),
                     lambda m: m['files'][0].update(component='missing'),
                     lambda m: m['programs'][0]['libraries'].append('Unknown/1.0.0'),
                     lambda m: m['programs'][0]['frameworks'][0].update(rollForward='Major'),
                     lambda m: m['components'][0]['source'].update(tag='latest'),
                     lambda m: m['components'].append(copy.deepcopy(m['components'][0])),
                     lambda m: m['components'][0]['objects'].append('0' * 64),
                     lambda m: m['files'][0].update(size=-1),
                     lambda m: m['files'][0].update(sha256='wrong'),
                     lambda m: m.update(api=99)]
        for mutate in mutations:
            changed = copy.deepcopy(self.manifest)
            mutate(changed)
            with self.assertRaises(ValueError):
                toolkit.validate(changed)

    def test_corrupt_component_and_duplicate_json_keys(self):
        archive = self.components / self.manifest['components'][0]['source']['asset']
        archive.write_bytes(b'broken')
        with self.assertRaises(ValueError):
            toolkit.validate(self.manifest, self.root, self.components)
        self.put('duplicate.json', '{"api":1,"api":2}')
        with self.assertRaises(ValueError):
            toolkit.read(self.root / 'duplicate.json')

    def test_legacy_is_explicit_and_never_inferred_as_shared(self):
        legacy = {'api': 1, 'version': '0.1.0', 'platform': 'win-x64', 'entries': toolkit.ENTRIES}
        self.assertEqual(toolkit.validate(legacy, self.root), 'legacy')
        for change in ({'version': '0.2.0'}, {'api': 3}, {'components': []}, {'format': toolkit.FORMAT}):
            with self.assertRaises(ValueError):
                toolkit.validate({**legacy, **change})


if __name__ == '__main__':
    unittest.main()
