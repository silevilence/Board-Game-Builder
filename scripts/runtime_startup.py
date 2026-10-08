"""Write generated examples using the current runtime entry protocol."""
import json


def write_game(root, module):
    definition = dict(module)
    startup = {'format': 1, 'id': definition.pop('id'), 'version': definition.pop('version'), 'plugins': []}

    def select(plugin, configuration=None):
        startup['plugins'].append({'id': plugin, 'version': '1', 'api': 1, 'configuration': configuration or {}})

    for plugin in ('kernel', 'session.local', 'recording'):
        select(plugin)
    select('definition.module', {'path': 'module.json'})
    select('lua.rules', {'file': definition.pop('rules')})
    select('lua.cpu', {'file': definition.pop('cpu')})
    extensions = definition.pop('plugins', [])
    if extensions:
        select('wasm', {'extensions': extensions})
    for name, data in (('module.json', definition), ('game.start.json', startup)):
        (root / name).write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
