import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location('release', Path(__file__).parents[1] / 'release.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class MetadataTests(unittest.TestCase):
    def test_all_positions_and_casing(self):
        text = '# Change Log\n\n## V2.0.0\n### A\n- first\n## v1.0.0\n### B\n- middle\n## V0.1.0\n### C\n- last\n'
        for tag, expected in [('v2.0.0', '### A\n- first\n'), ('V1.0.0', '### B\n- middle\n'), ('V0.1.0', '### C\n- last\n')]:
            self.assertEqual(release.notes(text, tag), expected)

    def test_invalid_versions(self):
        for tag in ['main', 'V1.0', 'V01.0.0', 'V1.0.0-rc1', 'V1.0.0+build', 'V1.0.0\n']:
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                release.version(tag)

    def test_missing_duplicate_empty(self):
        for text in ['', '## V1.0.0\n- other', '## V0.1.0\n', '## V0.1.0\n### Category\n', '## V0.1.0\n- one\n## v0.1.0\n- two']:
            with self.subTest(text=text), self.assertRaises(ValueError):
                release.notes(text, 'V0.1.0')

    def test_product_mismatch_and_missing_file(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'Directory.Build.props').write_text('<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>')
            with self.assertRaises(ValueError):
                release.check(root, 'V0.1.0')
            with self.assertRaises(FileNotFoundError):
                release.check(root, 'V1.0.0')


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.manifest = {'commit': 'abc', 'version': '0.1.0'}
        self.name = 'Board-Game-Builder-V0.1.0-win-x64.zip'
        with zipfile.ZipFile(self.root / self.name, 'w') as z:
            z.writestr('toolkit.json', json.dumps(self.manifest))
        (self.root / 'toolkit.json').write_text(json.dumps(self.manifest))
        (self.root / (self.name + '.sha256')).write_text(f'{release.sha(self.root / self.name)}  {self.name}\n')
        self.remote = None
        self.assets = {}
        self.calls = []
        self.fail_upload = False
        self.refs = [{'ref': 'refs/tags/V0.1.0', 'object': {'type': 'commit', 'sha': 'abc'}}]
        for mock in [patch.object(release, 'check', return_value='### Features\n- release\n'),
                     patch.object(release.subprocess, 'check_output', return_value='abc\n'),
                     patch.dict(os.environ, GH_REPO='test/repository'), patch.object(release, 'gh', side_effect=self.gh)]:
            mock.start()
            self.addCleanup(mock.stop)

    def gh(self, *args):
        self.calls.append(args)
        if args[0] == 'api':
            if 'matching-refs' in args[-1]:
                return json.dumps(self.refs)
            if args[-1].endswith('releases?per_page=100'):
                return json.dumps([[self.remote] if self.remote else []])
            return json.dumps(self.remote)
        action = args[1]
        if action == 'create':
            self.remote = {'tag_name': args[2], 'draft': True, 'body': '### Features\n- release\n', 'target_commitish': 'abc', 'assets': []}
        elif action == 'upload':
            if self.fail_upload and self.assets:
                raise RuntimeError('Injected upload interruption')
            file = Path(args[3])
            self.assets[file.name] = file.read_bytes()
            self.remote['assets'].append({'name': file.name})
        elif action == 'download':
            directory = Path(args[args.index('--dir') + 1])
            pattern = args[args.index('--pattern') + 1] if '--pattern' in args else None
            for name, data in self.assets.items():
                if pattern is None or name == pattern:
                    (directory / name).write_bytes(data)
        elif action == 'edit':
            self.assertEqual(len(self.assets), 3)
            self.remote['draft'] = False
        return ''

    def publish(self):
        release.publish(self.root, 'V0.1.0', self.root)

    def test_success_and_idempotent_rerun(self):
        self.publish()
        self.assertFalse(self.remote['draft'])
        self.publish()
        self.assertEqual(sum(c[:2] == ('release', 'create') for c in self.calls), 1)
        self.assertEqual(sum(c[:2] == ('release', 'upload') for c in self.calls), 3)

    def test_interrupted_upload_resumes_draft(self):
        self.fail_upload = True
        with self.assertRaises(RuntimeError):
            self.publish()
        self.assertTrue(self.remote['draft'])
        self.fail_upload = False
        self.publish()
        self.assertFalse(self.remote['draft'])

    def test_alias_reuses_existing_release(self):
        self.publish()
        self.remote['tag_name'] = 'v0.1.0'
        self.publish()
        self.assertEqual(sum(c[:2] == ('release', 'create') for c in self.calls), 1)

    def test_conflicting_alias_rejected_before_mutation(self):
        self.refs.append({'ref': 'refs/tags/v0.1.0', 'object': {'type': 'commit', 'sha': 'other'}})
        with self.assertRaises(ValueError):
            self.publish()
        self.assertIsNone(self.remote)

    def test_corrupt_existing_asset_never_overwritten(self):
        self.publish()
        self.assets[self.name] = b'corrupt'
        with self.assertRaises(ValueError):
            self.publish()
        self.assertEqual(sum(c[:2] == ('release', 'upload') for c in self.calls), 3)

    def test_bad_local_checksum_creates_no_draft(self):
        (self.root / (self.name + '.sha256')).write_text('wrong')
        with self.assertRaises(ValueError):
            self.publish()
        self.assertIsNone(self.remote)

    def test_build_failure_creates_no_draft(self):
        (self.root / self.name).unlink()
        with self.assertRaises(FileNotFoundError):
            self.publish()
        self.assertIsNone(self.remote)


if __name__ == '__main__':
    unittest.main()
