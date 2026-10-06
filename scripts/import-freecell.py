"""Reproduce the FreeCell sample data from the user-supplied Kenney ZIP (stdlib only)."""
import json
from pathlib import Path
import sys
from zipfile import ZipFile

root = Path(__file__).resolve().parents[1] / 'samples' / 'freecell'
root.mkdir(parents=True, exist_ok=True)
(root / 'assets').mkdir(exist_ok=True)
entities, resources = [], {}

def component(kind, data):
    return {'type': kind, 'data': data}

def region(name, label, x, y, capacity, kind, number, fan=0):
    entities.append({'id': name, 'components': [
        component('container', {'capacity': capacity, 'ordered': True, 'contents': []}),
        component('position', {'x': x, 'y': y}),
        component('faces', {'names': [label], 'resources': [], 'up': True}),
        component('properties', {'zone': kind, 'number': number, 'viewFanY': fan,
                  'viewWidth': 84, 'viewHeight': 120 if kind != 1 else 410, 'viewTheme': 1,
                  'viewSuit': number-12 if kind == 3 else 0,
                  'viewCardWidth': 84, 'viewCardHeight': 120, 'viewHotkey': number if kind == 1 else 0, 'viewHidden': int(kind == 0)})]})

region('deck', '牌库', 0, 0, 52, 0, 0)
for i in range(1, 9):
    region(f'column-{i}', f'第 {i} 列', (i-1)*112, 190, 52, 1, i, 42)
for i in range(1, 5):
    region(f'cell-{i}', f'空当 {i}', (i-1)*96, 32, 1, 2, i+8)
suits = [('clubs', '梅花', 0), ('diamonds', '方块', 1), ('hearts', '红桃', 1), ('spades', '黑桃', 0)]
for i, (_, label, _) in enumerate(suits, 1):
    region(f'home-{i}', label+' A→K', 496+(i-1)*96, 32, 13, 3, i+12)

with ZipFile(sys.argv[1]) as archive:
    (root / 'assets' / 'License.txt').write_bytes(archive.read('License.txt'))
    for suit_id, (suit, label, color) in enumerate(suits, 1):
        for rank in range(1, 14):
            face = {1: 'A', 11: 'J', 12: 'Q', 13: 'K'}.get(rank, f'{rank:02}')
            filename = f'card_{suit}_{face}.png'
            (root / 'assets' / filename).write_bytes(archive.read('PNG/Cards (large)/'+filename))
            identity = f'card-{(suit_id-1)*13+rank:02}'
            resources[identity] = 'assets/'+filename
            entities[0]['components'][0]['data']['contents'].append(identity)
            entities.append({'id': identity, 'components': [
                component('faces', {'names': [label+face.lstrip('0')], 'resources': [identity], 'up': True}),
                component('properties', {'rank': rank, 'suit': suit_id, 'color': color, 'number': (suit_id-1)*13+rank})]})
entities.append({'id': 'match', 'components': [component('properties', {
    'moves': 0, 'home': 0, 'lastCard': 0, 'lastSource': 0, 'lastCount': 0, 'viewHidden': 1})]})
module = {'format': 1, 'id': 'freecell', 'version': '1.2.0', 'rules': 'rules.lua', 'cpu': 'cpu.lua',
          'resources': resources, 'runtimeFiles': ['assets/License.txt', 'README.md'],
          'seats': [{'id': 'p1', 'team': 'solo', 'controller': 'human'}],
          'phases': ['setup', 'deal', 'play'], 'layout': {'kind': 'free', 'originX': 20, 'originY': 12},
          'entities': entities}
(root / 'module.json').write_text(json.dumps(module, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
