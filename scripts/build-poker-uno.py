"""Generate the three authored presets. Run build-poker-uno.ps1 to rebuild the atlas too."""
import json
import sys
from pathlib import Path

root = Path(sys.argv[1])
frames = json.loads((root / 'assets/cards.json').read_text(encoding='utf-8-sig'))['frames']
images = [f'card_{suit}_{rank}' for suit in ('clubs', 'diamonds', 'hearts', 'spades')
          for rank in ('A', '02', '03', '04', '05', '06', '07', '08', '09', '10', 'J', 'Q', 'K')] + ['card_back']
if any(name not in frames for name in images):
    raise ValueError('The atlas must contain all 52 named cards and card_back')


def write(name, value):
    (root / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def entity(identity, **parts):
    return {'id': identity, 'components': [{'type': k, 'data': v} for k, v in parts.items()]}


def region(identity, title, x, y, contents=None, **props):
    return entity(identity, container={'capacity': 52, 'ordered': True, 'contents': contents or []},
                  faces={'names': [title], 'resources': [images[52]], 'up': True},
                  position={'x': x, 'y': y},
                  properties={'viewWidth': 440, 'viewHeight': 156, 'viewFanX': 34,
                              'viewCardWidth': 76, 'viewCardHeight': 104, **props})


for count in (4, 5, 6):
    cards = []
    for suit, title in enumerate(('梅花', '方块', '红桃', '黑桃')):
        for rank, face in enumerate(('A', '2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K'), 1):
            index = suit * 13 + rank - 1
            cards.append(entity(f'card-{index + 1:02}',
                                faces={'names': [title + face, '牌背'], 'resources': [images[index], images[52]], 'up': False},
                                properties={'suit': suit + 1, 'rank': rank}, visibility={'mode': 'hidden'}))
    deck = region('deck', '摸牌堆', 270, 200, [c['id'] for c in cards], viewWidth=180)
    entities = [deck, region('table', '当前弃牌', 480, 200, viewWidth=180, viewTopOnly=1),
                region('spent', '已用弃牌', 0, 0, viewHidden=1),
                entity('match', properties={'stalled': 0, 'viewHidden': 1})]
    for i in range(count):
        hand = region(f'hand-p{i+1}', f'席位 {i+1} · 手牌', (i % 3) * 465, 20 if i < 3 else 400)
        hand['components'].append({'type': 'owner', 'data': {'seat': f'p{i+1}'}})
        entities.append(hand)
    entities += cards
    write(f'module-{count}.json', {'format': 1, 'layout': {'kind': 'free', 'size': 70, 'originX': 15, 'originY': 20},
          'seats': [{'id': f'p{i+1}', 'team': f'p{i+1}', 'controller': 'human' if i == 0 else 'cpu'} for i in range(count)],
          'phases': ['setup', 'opening', 'play', 'drawn'],
          'resources': {'cards': 'assets/cards.json', 'player.presentation': 'player.json'},
          'runtimeFiles': ['assets/License.txt', 'README.md'], 'entities': entities})

plugins = [{'id': key, 'version': '1', 'api': 1, 'configuration': config} for key, config in (
    ('kernel', {}), ('session.local', {}), ('recording', {}),
    ('definition.module', {'path': 'module-4.json', 'presets': {f'{n} 人': f'module-{n}.json' for n in (4, 5, 6)}}),
    ('lua.rules', {'file': 'rules.lua'}), ('lua.cpu', {'file': 'cpu.lua'}))]
write('game.start.json', {'format': 1, 'id': 'poker-uno', 'version': '1.0.0', 'plugins': plugins})
write('player.json', {'title': '同花同点 · 扑克牌 UNO', 'showActions': True,
                    'help': '4～6 人，每人 7 张。打出一张与中央弃牌同花色或同点数的牌；A/J/Q/K 都是普通牌。不能出时摸 1 张，能出可打出，也可结束回合。先出完获胜。没有功能牌或 UNO 惩罚。\n摸牌堆用尽后回收旧弃牌，保留当前顶牌。无牌可摸且整轮无人出牌则平局。\nF3 选择人数、本地模式及真人席位；任意席位可用 CPU。文件菜单可连接服务器或进入观战。点牌再点弃牌区，或拖动出牌；右侧动作栏可摸牌及结束回合。'})
