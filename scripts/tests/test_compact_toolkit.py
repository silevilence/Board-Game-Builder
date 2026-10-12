from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).parents[1]))
import compact_toolkit
import toolkit_manifest
import test_toolkit_manifest as fixtures


class CompactToolkitTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.ManifestTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.root.parent / 'compact'
        self.entries = self.fixture.root.parent / 'entries'
        self.fixture.put('README.md', 'offline documentation')
        # Include README in the complete source inventory before transforming it.
        toolkit_manifest.generate(self.fixture.root, self.fixture.components, 'test/repo')
        for role in toolkit_manifest.ENTRIES:
            if role == 'documentation':
                continue
            folder = self.entries / role
            folder.mkdir(parents=True)
            (folder / ('Bgb.Entry.' + role + '.exe')).write_bytes(('native fixture:' + role).encode())

    def test_compacted_package_has_all_dependencies_with_one_copy(self):
        compact_toolkit.prepare(self.fixture.root, self.root, self.entries)
        manifest = toolkit_manifest.generate(self.root, self.root.parent / 'compact-components', 'test/repo')
        report = compact_toolkit.compact(self.root)
        self.assertLess(report['storedFiles'], report['logicalFiles'])
        self.assertEqual(toolkit_manifest.validate(manifest, self.root), 'components')
        actual = toolkit_manifest.inventory(self.root)
        self.assertEqual(len(actual), len({f['sha256'] for f in actual.values()}))
        self.assertTrue((self.root / 'bgb.exe').is_file())
        self.assertTrue((self.root / toolkit_manifest.ENTRIES['documentation']).is_file())
        self.assertFalse((self.root / '.payload/worker/Common.dll').exists())
        self.assertTrue((self.fixture.root / 'worker/Common.dll').is_file())

    def test_invalid_canonical_reference_and_unknown_layout(self):
        compact_toolkit.prepare(self.fixture.root, self.root, self.entries)
        manifest = toolkit_manifest.generate(self.root, self.root.parent / 'compact-components', 'test/repo')
        manifest['files'][0]['storage'] = '../external'
        with self.assertRaises(ValueError):
            toolkit_manifest.validate(manifest)
        manifest['layout'] = 'unknown'
        with self.assertRaises(ValueError):
            toolkit_manifest.validate(manifest)


if __name__ == '__main__':
    unittest.main()
