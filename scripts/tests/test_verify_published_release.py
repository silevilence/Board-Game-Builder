import hashlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
import sys
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).parents[1]))

spec = importlib.util.spec_from_file_location('published', Path(__file__).parents[1] / 'verify-published-release.py')
published = importlib.util.module_from_spec(spec)
spec.loader.exec_module(published)


class PublishedDownloadTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.path = Path(self.temporary.name) / 'component.zip'
        self.content = b'fixed release content'
        self.asset = {'name': self.path.name, 'size': len(self.content),
                      'digest': 'sha256:' + hashlib.sha256(self.content).hexdigest(),
                      'browser_download_url': 'https://github.com/test/repo/releases/download/components-fixed/component.zip'}

    def test_anonymous_download_verified_and_reused(self):
        with patch.object(published.urllib.request, 'urlopen', return_value=io.BytesIO(self.content)) as request:
            self.assertTrue(published.download(self.asset, self.path))
            self.assertNotIn('Authorization', request.call_args.args[0].headers)
        with patch.object(published.urllib.request, 'urlopen') as request:
            self.assertFalse(published.download(self.asset, self.path)); request.assert_not_called()

    def test_interruption_retries_without_exposing_partial_content(self):
        with patch.object(published.urllib.request, 'urlopen', side_effect=[OSError('interrupted'), io.BytesIO(self.content)]), patch.object(published.time, 'sleep'):
            self.assertTrue(published.download(self.asset, self.path))
        self.assertEqual(self.path.read_bytes(), self.content)
        self.assertFalse(self.path.with_suffix('.zip.partial').exists())

    def test_corrupt_download_does_not_replace_existing_file(self):
        self.path.write_bytes(b'existing local evidence')
        with patch.object(published.urllib.request, 'urlopen', side_effect=lambda *a, **k: io.BytesIO(b'bad')), patch.object(published.time, 'sleep'):
            with self.assertRaises(ValueError): published.download(self.asset, self.path)
        self.assertEqual(self.path.read_bytes(), b'existing local evidence')
        self.assertFalse(self.path.with_suffix('.zip.partial').exists())
