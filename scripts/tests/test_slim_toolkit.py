import copy
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).parents[1]))
import slim_toolkit
import compact_toolkit
import toolkit_manifest as toolkit
import test_compact_toolkit as fixtures


class SlimToolkitTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.CompactToolkitTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.source = self.fixture.fixture.root
        player = toolkit.read(self.source / 'player/release.json')
        player['files'] = {p.relative_to(self.source / 'player').as_posix(): toolkit.digest(p)
                           for p in (self.source / 'player').rglob('*') if p.is_file() and p.name != 'release.json'}
        (self.source / 'player/release.json').write_bytes(toolkit.canonical(player))
        self.original = toolkit.generate(self.source, self.fixture.fixture.components, 'test/repo')
        self.slim = self.source.parent / 'slim'

    def test_same_build_bytes_without_private_runtime_and_locked_export(self):
        descriptor = slim_toolkit.create(self.source, self.slim)
        manifest = toolkit.generate(self.slim, self.fixture.fixture.components, 'test/repo')
        self.assertEqual(toolkit.validate(manifest, self.slim), 'components')
        self.assertEqual(manifest['packageMode'], 'slim')
        self.assertEqual(manifest['exportDependencies'], descriptor)
        self.assertFalse(list(self.slim.rglob('coreclr.dll')))
        for file in self.original['files']:
            if file['path'].endswith(('.dll', '.md')) and not file['path'].endswith('coreclr.dll'):
                self.assertEqual((self.source / file['path']).read_bytes(), (self.slim / file['path']).read_bytes())
        for program in manifest['programs']:
            options = toolkit.read(self.slim / program['runtimeConfig'])['runtimeOptions']
            self.assertEqual(options['rollForward'], 'LatestPatch')
            self.assertNotIn('includedFrameworks', options)
        for file in descriptor['files']:
            self.assertEqual(toolkit.digest(self.source / 'player' / file['path']), file['sha256'])
        compact = self.slim.parent / 'compact-slim'
        compact_toolkit.prepare(self.slim, compact, self.fixture.entries)
        packed = toolkit.generate(compact, self.fixture.fixture.components, 'test/repo')
        compact_toolkit.compact(compact)
        self.assertEqual(toolkit.validate(packed, compact), 'components')
        self.assertEqual(packed['packageMode'], 'slim')
        self.assertEqual(packed['exportDependencies'], descriptor)

    def test_incomplete_export_and_mismatched_descriptor_rejected(self):
        descriptor = slim_toolkit.create(self.source, self.slim)
        manifest = toolkit.generate(self.slim, self.fixture.fixture.components, 'test/repo')
        changed = copy.deepcopy(manifest)
        changed['exportDependencies']['files'][0]['component'] = 'missing'
        with self.assertRaises(ValueError): toolkit.validate(changed)
        descriptor['version'] = '2.0.0'
        (self.slim / 'player/export.dependencies.json').write_bytes(toolkit.canonical(descriptor))
        with self.assertRaises(ValueError): toolkit.validate(manifest, self.slim)
        with self.assertRaises(ValueError): slim_toolkit.create(self.source, self.source / 'nested')


if __name__ == '__main__':
    unittest.main()
