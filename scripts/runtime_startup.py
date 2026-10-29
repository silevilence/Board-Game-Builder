"""Write generated examples using the current runtime entry protocol."""
import json
from pathlib import Path


def write_game(root, module):
    definition = dict(module)
    startup = {'format': 1, 'id': definition.pop('id'), 'version': definition.pop('version'), 'plugins': []}

    def select(plugin, configuration=None):
        startup['plugins'].append({'id': plugin, 'version': '1', 'api': 1, 'configuration': configuration or {}})

    for plugin in ('kernel', 'session.local', 'recording', 'board.render'):
        select(plugin)
    select('definition.module', {'path': 'module.json'})
    select('lua.rules', {'file': definition.pop('rules')})
    select('lua.cpu', {'file': definition.pop('cpu')})
    extensions = definition.pop('plugins', [])
    if extensions:
        select('wasm', {'extensions': extensions})
    resources = definition.pop('resources', {})
    images = [{'path': path, 'name': name} for name, path in resources.items()
              if Path(path).suffix.lower() in ('.png', '.jpg', '.jpeg', '.gif', '.bmp')]
    files = [{'path': path, 'name': name} for name, path in resources.items()
             if Path(path).suffix.lower() not in ('.png', '.jpg', '.jpeg', '.gif', '.bmp')]
    if resources:
        select('resources', {'sources': files})
    if images:
        select('resources.images', {'sources': images})
    for name, data in (('module.json', definition), ('game.start.json', startup)):
        (root / name).write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
