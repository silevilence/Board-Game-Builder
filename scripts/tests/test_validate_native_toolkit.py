import copy
import hashlib
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).parents[1]))
import compact_toolkit
import slim_toolkit
import toolkit_manifest as toolkit
import validate_native_toolkit as native
import test_compact_toolkit as fixtures


class NativeToolkitTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.CompactToolkitTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.fixture.root
        for entry in ('player', 'debug-player', 'editor'):
            self.package(self.root / entry / 'backends/desktopgl', 'render.desktopgl', license=True)
        for entry in ('', 'editor'):
            sdk = self.root / entry / 'native-sdk'
            licenses = [self.file(sdk, 'licenses/' + name, 'license:' + name, 'content')
                        for name in ('MonoGame.LICENSE.txt', 'CordiSharp.LICENSE.txt')]
            hosts = []
            for host in ('application', 'board'):
                hosts.append({'id': host + '.desktop', 'entryType': 'Native.' + host,
                              'entryMethod': 'Run', 'payloads': [], 'requiredCapabilities': [],
                              'files': [self.file(sdk, 'hosts/' + host + '/Common.dll',
                                                  'shared dependency bytes'), *licenses]})
            packages = []
            for plugin in ('render.desktopgl', 'definition.board-state', 'wasm', 'motion.definition'):
                relative = 'plugins/' + plugin
                self.package(sdk / relative, plugin)
                packages.append(relative)
            self.write(sdk / 'native.sdk.json', {'format': 1, 'target': 'win-x64',
                                               'hosts': hosts, 'packages': packages})
        player = toolkit.read(self.root / 'player/release.json')
        player['files'] = {p.relative_to(self.root / 'player').as_posix(): toolkit.digest(p)
                           for p in (self.root / 'player').rglob('*')
                           if p.is_file() and p.name != 'release.json'}
        self.write(self.root / 'player/release.json', player)
        toolkit.generate(self.root, self.fixture.fixture.components, 'test/repo')

    @staticmethod
    def write(path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(toolkit.canonical(value))

    @staticmethod
    def file(root, relative, text, role='managed'):
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        data = text.encode()
        path.write_bytes(data)
        return {'path': relative, 'sha256': hashlib.sha256(data).hexdigest(), 'role': role}

    def package(self, root, identity, license=False):
        files = [self.file(root, 'Implementation.dll', identity, 'implementation')]
        if license:
            files.append(self.file(root, 'MonoGame.LICENSE.txt', 'MonoGame license', 'content'))
        self.write(root / 'runtime.plugin.json', {
            'format': 1, 'kind': 'runtime', 'id': identity, 'version': '1', 'api': 1,
            'entry': {'assembly': 'Implementation.dll', 'type': 'Factory', 'method': 'Create'},
            'targets': ['win-x64'], 'aot': {'compatible': True, 'targets': ['win-x64'], 'reason': ''},
            'provides': [], 'requires': [], 'execution': {'scope': 'Instance', 'modes': 'All'},
            'files': files})

    def test_native_sdk_paths_match_host_and_package_roots(self):
        self.assertEqual(native.verify(self.root), {
            'success': True, 'graphicsEntries': 3, 'nativeAuthorEntries': 2})

    def test_corrupt_or_missing_inputs_rejected(self):
        for relative in ('native-sdk/hosts/board/Common.dll',
                         'editor/native-sdk/plugins/wasm/Implementation.dll',
                         'debug-player/backends/desktopgl/MonoGame.LICENSE.txt'):
            path = self.root / relative
            original = path.read_bytes()
            with self.subTest(relative=relative):
                path.write_bytes(b'changed')
                with self.assertRaises(ValueError):
                    native.verify(self.root)
                path.unlink()
                with self.assertRaises(ValueError):
                    native.verify(self.root)
                path.write_bytes(original)

    def test_invalid_paths_and_duplicate_or_empty_file_lists_rejected(self):
        package = self.root / 'player/backends/desktopgl'
        manifest = toolkit.read(package / 'runtime.plugin.json')
        file = manifest['files'][0]
        for relative in ('../Implementation.dll', '/Implementation.dll', 'C:/Implementation.dll',
                         'a/../Implementation.dll', 'Implementation.dll:stream', 'a\\b.dll'):
            with self.subTest(path=relative), self.assertRaises(ValueError):
                native.verify_files(package, [{**file, 'path': relative}])
        for files in ([], [file, file], [file, {**file, 'path': file['path'].upper()}]):
            with self.subTest(files=files), self.assertRaises(ValueError):
                native.verify_files(package, files)

    def test_missing_duplicate_hosts_packages_and_aot_rejected(self):
        path = self.root / 'native-sdk/native.sdk.json'
        original = toolkit.read(path)
        mutations = [lambda m: m['hosts'].append(copy.deepcopy(m['hosts'][0])),
                     lambda m: m['hosts'].pop(), lambda m: m['packages'].pop(),
                     lambda m: m['packages'].append(m['packages'][0]),
                     lambda m: m['packages'].append('../native-sdk/plugins/wasm')]
        for mutate in mutations:
            changed = copy.deepcopy(original)
            mutate(changed)
            self.write(path, changed)
            with self.assertRaises(ValueError):
                native.verify(self.root)
        self.write(path, original)
        package = self.root / 'native-sdk/plugins/wasm/runtime.plugin.json'
        plugin = toolkit.read(package)
        plugin['aot']['compatible'] = False
        self.write(package, plugin)
        with self.assertRaises(ValueError):
            native.verify(self.root)

    def test_slim_and_compact_preserve_native_capabilities(self):
        slim = self.root.parent / 'native-slim'
        slim_toolkit.create(self.root, slim)
        toolkit.generate(slim, self.fixture.fixture.components, 'test/repo')
        self.assertTrue(native.verify(slim)['success'])
        compact = self.root.parent / 'native-compact'
        compact_toolkit.prepare(slim, compact, self.fixture.entries)
        manifest = toolkit.generate(compact, self.fixture.fixture.components, 'test/repo')
        compact_toolkit.compact(compact)
        # The native bootstrap materializes every verified alias before CLI validation.
        # Recreate that layout here without launching test fixture executables.
        for file in manifest['files']:
            destination = compact / file['path']
            if not destination.exists():
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.hardlink_to(compact / file['storage'])
        self.assertTrue(native.verify(compact / '.payload')['success'])


if __name__ == '__main__':
    unittest.main()
