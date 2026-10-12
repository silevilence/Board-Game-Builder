"""Native bootstrap integration tests. Build publish-toolkit-entries.ps1 first."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


BOOTSTRAP = Path(__file__).parents[2] / 'artifacts/toolkit-entries/cli/Bgb.Entry.cli.exe'


@unittest.skipUnless(os.name == 'nt' and BOOTSTRAP.is_file(), 'Native Windows bootstrap must be built first')
class NativeEntryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='工具包 native ')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        shutil.copyfile(BOOTSTRAP, self.root / 'bgb.exe')
        (self.root / '.payload').mkdir()
        shutil.copyfile(Path(os.environ['SystemRoot']) / 'System32/cmd.exe', self.root / '.payload/command.exe')
        (self.root / '.payload/shared.bin').write_bytes(b'one immutable product object')
        files = []
        for path in ('bgb.exe', '.payload/command.exe', '.payload/shared.bin'):
            data = (self.root / path).read_bytes()
            files.append({'path': path, 'storage': path, 'size': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
        files.append({**files[-1], 'path': '.payload/nested/shared.bin'})
        self.manifest = {'api': 2, 'format': 'board-game-builder.toolkit', 'platform': 'win-x64', 'layout': 'hardlinks-v1',
                         'entryTargets': {'bgb.exe': '.payload/command.exe'}, 'files': files}
        self.save()

    def save(self):
        (self.root / 'toolkit.json').write_text(json.dumps(self.manifest), encoding='utf-8')

    def invoke(self, *args):
        return subprocess.run([str(self.root / 'bgb.exe'), '/d', '/c', *args], cwd=os.environ['TEMP'],
                              capture_output=True, timeout=30)

    def test_exit_output_cwd_repeat_and_physical_identity(self):
        result = self.invoke('echo bootstrap-output & exit /b 37')
        self.assertEqual(result.returncode, 37, result.stderr)
        self.assertIn(b'bootstrap-output', result.stdout)
        self.assertEqual(os.stat(self.root / '.payload/shared.bin').st_ino,
                         os.stat(self.root / '.payload/nested/shared.bin').st_ino)
        self.assertEqual(self.invoke('exit /b 0').returncode, 0)
        self.assertIn(os.environ['TEMP'].encode(), self.invoke('cd').stdout)

    def test_standard_input_is_forwarded(self):
        result = subprocess.run([str(self.root / 'bgb.exe'), '/d', '/v:on', '/c', 'set /p userInput=& echo !userInput!'],
                                input=b'forwarded-input\r\n', capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(b'forwarded-input', result.stdout)

    def test_concurrent_first_launch(self):
        children = [subprocess.Popen([str(self.root / 'bgb.exe'), '/d', '/c', 'exit /b 0'],
                                     stdout=subprocess.PIPE, stderr=subprocess.PIPE) for _ in range(3)]
        for child in children:
            _, error = child.communicate(timeout=30)
            self.assertEqual(child.returncode, 0, error)

    def test_corruption_is_rejected_before_start(self):
        (self.root / '.payload/shared.bin').write_bytes(b'corrupt')
        result = self.invoke('exit /b 37')
        self.assertEqual(result.returncode, 1)
        self.assertIn(b'corrupt', result.stderr)
        self.assertFalse((self.root / '.payload/nested/shared.bin').exists())

    def test_existing_copied_alias_is_not_reported_as_shared(self):
        (self.root / '.payload/nested').mkdir()
        shutil.copyfile(self.root / '.payload/shared.bin', self.root / '.payload/nested/shared.bin')
        result = self.invoke('exit /b 37')
        self.assertEqual(result.returncode, 1)
        self.assertIn(b'not a shared product link', result.stderr)

    def test_path_escape_duplicate_and_unknown_layout_are_rejected(self):
        for invalid in ('../outside', 'C:/outside', 'a:stream', 'CON', 'bgb.exe'):
            self.manifest['files'][-1]['path'] = invalid
            self.save()
            self.assertEqual(self.invoke('exit /b 37').returncode, 1, invalid)
        self.manifest['layout'] = 'unknown'
        self.save()
        self.assertIn(b'Unsupported toolkit layout', self.invoke('exit /b 37').stderr)


if __name__ == '__main__':
    unittest.main()
