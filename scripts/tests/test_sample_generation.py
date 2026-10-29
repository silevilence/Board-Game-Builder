import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class SampleGenerationTests(unittest.TestCase):
    def test_investigator_generator_declares_rendering(self):
        root = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'changes.json'
            subprocess.run([sys.executable, str(root / 'samples/cursed-investigators/tools/build-project.py'), str(output)], check=True, capture_output=True)
            changes = json.loads(output.read_text(encoding='utf-8'))
            startup = json.loads(changes['files']['game.start.json'])
            renderers = [plugin for plugin in startup['plugins'] if plugin['id'] == 'board.render']
            self.assertEqual(len(renderers), 1)
            self.assertEqual(renderers[0]['api'], 1)


if __name__ == '__main__':
    unittest.main()
