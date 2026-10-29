"""Explicit, narrow migration of an existing board entry; never invoked during load."""
import argparse
import json
from pathlib import Path


def migrate(project: Path, check: bool = False) -> bool:
    entry = project.resolve() / 'game.start.json'
    original = entry.read_text(encoding='utf-8-sig')
    startup = json.loads(original)
    plugins = startup.get('plugins', [])
    if startup.get('format') != 1 or not any(p.get('id') in ('definition.module', 'definition.state') for p in plugins):
        raise ValueError(f'{entry}: expected an explicitly declared board definition')
    selected = [p for p in plugins if p.get('id') == 'board.render']
    if selected:
        if len(selected) != 1 or selected[0] != {'id': 'board.render', 'version': '1', 'api': 1, 'configuration': {}}:
            raise ValueError(f'{entry}: existing renderer declaration needs manual review')
        return False
    if check:
        raise ValueError(f'{entry}: missing board.render; run explicit migration')
    plugins.append({'id': 'board.render', 'version': '1', 'api': 1, 'configuration': {}})
    entry.write_text(json.dumps(startup, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return True


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    arguments = parser.parse_args()
    print('Migrated' if migrate(arguments.project, arguments.check) else 'Already declared')
