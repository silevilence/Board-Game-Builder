"""Rebuild the two teaching samples' JSON and geometric PNG assets (stdlib only).

Rules, CPU scripts and READMEs are hand maintained and are never overwritten.
"""
import json
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]


def write(folder, name, value):
    path = ROOT / "samples" / folder / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def component(kind, **data):
    return {"type": kind, "data": data}


def entity(identity, x, y, label, properties, **extra):
    return {"id": identity, "components": [component("position", x=x, y=y),
        component("faces", names=[label], resources=[], up=True),
        component("properties", **properties)], **extra}


def plugin(identity, **configuration):
    return {"id": identity, "version": "1", "api": 1, "configuration": configuration}


def startup(folder, extras, images=False):
    sources = [{"path": "player.json", "name": "player.presentation"},
               {"path": "board.json", "name": "board.presentation"}]
    plugins = [plugin("definition.module", path="module.json"), plugin("kernel"),
               *extras, plugin("session.local"), plugin("recording"),
               plugin("resources", sources=sources), plugin("board.render")]
    if images:
        plugins.append(plugin("resources.images", sources=[{"path": "assets", "pattern": "*.png"}]))
    write(folder, "game.start.json", {"format": 1, "id": folder, "version": "1.0.0", "plugins": plugins})
    write(folder, "author.profile.json", {"defaultProfile": "sample", "profiles": {"sample": {
        "format": 1, "plugins": [{"id": name} for name in ["edit.disk", "author.files", "author.module",
            "author.configuration", "author.resources", "author.validation", "author.run",
            "author.export.package", "author.export.player"]]}}})


def tile(name, color, label=""):
    """Flat colored square with a contrasting border; no downloaded assets."""
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    rgb = bytes.fromhex(color)
    glyphs = {"I": ["111", "010", "010", "010", "111"],
              "O": ["111", "101", "101", "101", "111"],
              "A": ["010", "101", "111", "101", "101"],
              "B": ["110", "101", "110", "101", "110"],
              "*": ["000", "101", "010", "101", "000"]}
    ink = set()
    for index, char in enumerate(label):
        for y, row in enumerate(glyphs[char]):
            for x, value in enumerate(row):
                if value == "1":
                    for sy in range(3):
                        for sx in range(3):
                            ink.add((10+index*15+x*3+sx, 23+y*3+sy))
    rows = b"".join(b"\0" + b"".join(
        bytes((20, 35, 44)) if (x, y) in ink or x < 2 or y < 2 or x >= 62 or y >= 62 else rgb
        for x in range(64)) for y in range(64))
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 64, 64, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")
    path = ROOT / "samples/polyomino/assets" / (name + ".png")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def polyomino():
    folder = "polyomino"
    prototype = {"id": "cell", "components": [component("owner", seat="p1"),
        component("visibility", mode="public"), component("properties", viewStyle=1, viewWidth=58,
            viewHeight=58, viewLayer=2, viewLabel=0, viewLabelY=-8, viewColor=0xFFFFFF, material=1),
        component("faces", names=["Cell"], resources=["I"], up=True)]}
    entities = []
    for y in range(4):
        for x in range(4):
            e = entity(f"slot-{x}-{y}", x, y, "Slot", dict(viewStyle=1, viewWidth=62, viewHeight=62, viewPassive=1))
            e["components"][1]["data"]["resources"] = ["slot"]
            entities.append(e)
    shapes = [("I", [(0, 0), (1, 0), (2, 0), (3, 0)], (5, 0), (0, 0, 1), "58B8CF"),
              ("O", [(0, 0), (1, 0), (0, 1), (1, 1)], (5, 5), (1, 0, 0), "E5B95B"),
              ("A", [(1, 0), (1, 1), (1, 2), (0, 2)], (9, 0), (2, 0, 0), "DA8275"),
              ("B", [(1, 0), (1, 1), (1, 2), (0, 2)], (9, 5), (1, 2, 1), "93B880")]
    for group, (name, cells, tray, goal, color) in enumerate(shapes, 1):
        tile(name, color, name)
        tile(name+"-handle", color, name+"*")
        for i, (dx, dy) in enumerate(cells):
            props = dict(piece=group, cell=i, dx=dx, dy=dy)
            if i == 0:
                props.update(rotation=0, placed=0, trayX=tray[0], trayY=tray[1],
                             goalX=goal[0], goalY=goal[1], goalR=goal[2], anchorX=tray[0], anchorY=tray[1])
            # One deliberate field override demonstrates that other fields still inherit.
            if name == "B":
                props["material"] = 2
            entities.append({"id": f"{name}-{i}", "prototype": "cell", "components": [
                component("position", x=tray[0]+dx, y=tray[1]+dy), component("properties", **props),
                component("faces", names=[name + ("*" if i == 0 else "")], resources=[name+"-handle" if i == 0 else name])]})
    tile("slot", "344C59")
    entities.append(entity("puzzle", 0, 0, "当前骨牌 {active}（1=I 2=O 3=A 4=B） · 已放 {placed}/4 · 操作 {moves}",
                           dict(viewHidden=1, active=1, placed=0, moves=0, width=4, height=4)))
    for identity, x, y, width, height, label in [
        ("board-title", 1.5, -1, 252, 42, "POLYOMINO / 4 x 4"),
        ("tray-title", 8.5, -1, 508, 42, "PIECES / * = drag handle"),
        ("instructions", 1.5, 5, 252, 110, "Select > Rotate > Place\nReturn a piece to try again\nBoard coordinates: 0 - 3")]:
        entities.append(entity(identity, x, y, label, dict(viewStyle=3, viewLabel=1,
            viewPassive=1, viewWidth=width, viewHeight=height, viewLayer=1, viewColor=0xE5B95B)))
    write(folder, "module.json", {"format": 1, "resources": {}, "seats": [{"id": "p1", "team": "solo", "controller": "human"}],
        "phases": ["puzzle"], "layout": {"kind": "square", "size": 64, "originX": 80, "originY": 100},
        "prototypes": [prototype], "entities": entities})
    startup(folder, [plugin("lua.rules", file="rules.lua"), plugin("lua.cpu", file="cpu.lua")], images=True)
    write(folder, "player.json", {"title": "Polyomino · 方格拼板", "width": 1200, "height": 850,
        "showActions": True, "statusEntity": "puzzle", "help": "将 I、O、A、B 四块骨牌铺满左侧 4×4 棋盘。A、B 是两块相同的 L 形骨牌。\n先在动作栏选择骨牌，可旋转、选择落点或收回。坐标从 (0,0) 开始，落点指形状外接框左上角。\n也可拖动当前骨牌标 * 的单元格到棋盘；整块会在提交后移动。拖动预览只跟随所拖的单元格。\n不能越界或重叠；卡住时收回再摆。F3 可改为全 CPU，按公开配置中的示范解完成，不是通用解算器。"})
    write(folder, "board.json", {"format": 1, "captions": []})


def supply():
    folder = "flow-supply"
    def set_value(entity, key, value):
        return {"kind": "set", "entity": entity, "property": key, "value": value}
    def status(index):
        return [{"kind": "flip", "entity": "status", "value": index},
                {"kind": "reveal", "entity": "status", "visibility": "public"}]
    def action(identity, label, commands=None, complete=True, result="done"):
        return dict(id=identity, label=label, commands=commands or [], complete=complete, result=result)
    def compare(entity, key, op, value):
        return {"op": op, "left": {"path": f"/state/entities/{entity}/properties/{key}"}, "right": {"literal": value}}
    nodes = [dict(id="start", kind="start", commands=status(0)),
        dict(id="pack", kind="player", seat={"fixed": "p1"}, actions=[
            action("depart", "确认装备，出发"),
            action("rope", "携带绳索（适合山路）", [set_value("kit", "choice", 1)], False),
            action("lamp", "携带提灯（适合海路）", [set_value("kit", "choice", 2)], False),
            action("empty", "放下装备，轻装出发", [set_value("kit", "choice", 0)], False)]),
        dict(id="weather", kind="system", commands=[{"kind": "roll", "entity": "weather"},
            {"kind": "flip", "entity": "weather", "value": 1},
            {"kind": "reveal", "entity": "weather", "visibility": "public"}, *status(1)]),
        dict(id="route", kind="player", seat={"current": True}, results=["done", "coast", "ridge", "home"], actions=[
            action("coast", "走海路：提灯可应对坏天气", result="coast"),
            action("ridge", "走山路：绳索可应对坏天气", result="ridge"),
            action("home", "返回营地，本局结束", result="home")]),
        dict(id="judge-coast", kind="system"), dict(id="judge-ridge", kind="system")]
    for identity, index, winners in [("bright", 2, ["p1"]), ("delivered", 3, ["p1"]), ("failed", 4, []), ("retreat", 5, [])]:
        nodes.append(dict(id=identity, kind="end", commands=status(index), winners=winners))
    edges = [dict(id="begin", **{"from": "start", "to": "pack"}),
             dict(id="packed", **{"from": "pack", "to": "weather"}),
             dict(id="forecast", **{"from": "weather", "to": "route"})]
    # The default 'done' result is declared for node-entry validation; actual actions use named results.
    for result, target in [("coast", "judge-coast"), ("ridge", "judge-ridge"), ("home", "retreat"), ("done", "retreat")]:
        edges.append(dict(id="route-"+result, **{"from": "route", "to": target}, result=result))
    for route, kit in [("coast", 2), ("ridge", 1)]:
        clear = compare("weather", "value", "ge", 4)
        equipped = compare("kit", "choice", "eq", kit)
        edges.extend([
            dict(id=route+"-bonus", **{"from": "judge-"+route, "to": "bright"}, priority=20,
                 condition={"op": "all", "items": [clear, equipped]}),
            dict(id=route+"-safe", **{"from": "judge-"+route, "to": "delivered"}, priority=10,
                 condition={"op": "any", "items": [clear, equipped]}),
            dict(id=route+"-blocked", **{"from": "judge-"+route, "to": "failed"}, default=True)])
    write(folder, "flow.json", {"format": 1, "nodes": nodes, "edges": edges})
    panel = dict(viewStyle=3, viewLabel=1, viewWidth=500, viewHeight=70, viewColor=0xE5B95B)
    kit = entity("kit", 300, 145, "装备 {choice}：0=无 / 1=绳索 / 2=提灯", {**panel, "choice": 0})
    weather = entity("weather", 300, 240, "天气 {value}：1~3 风雨 / 4~6 晴朗", {**panel, "value": 1})
    weather["components"][1]["data"]["names"].insert(0, "天气：确认装备、出发后才掷骰")
    weather["components"].append(component("dice", values=[1, 2, 3, 4, 5, 6]))
    state = entity("status", 300, 355, "", {**panel, "viewHeight": 110})
    state["components"][1]["data"]["names"] = ["营地：选一件装备，可反复更换。\n确认后才会掷骰决定天气。", "查看天气，选择海路或山路。\n装备匹配时可应对风雨，也可返回。",
        "额外嘉奖！晴天且装备匹配。\n两条条件同时成立，优先级 20 获选。", "补给送达，灯塔重新亮起。\n天气或装备满足通行条件。",
        "风雨阻路，补给未能送达。\n条件均不成立，进入默认出口。", "已返回营地，本局结束。\n下次可以准备不同装备再出发。"]
    write(folder, "module.json", {"format": 1, "resources": {}, "seats": [{"id": "p1", "team": "courier", "controller": "human"}],
        "phases": ["delivery"], "layout": {"kind": "free", "size": 64, "originX": 0, "originY": 0}, "entities": [kit, weather, state]})
    startup(folder, [plugin("flow", path="flow.json", maxAutomaticSteps=32)])
    write(folder, "player.json", {"title": "灯塔补给 · 纯配置流程", "width": 1000, "height": 720, "showActions": True,
        "statusEntity": "status", "help": "单人短流程：在营地选择绳索、提灯或不带装备，然后确认出发。天气由种子掷骰决定：1~3 风雨，4~6 晴朗。\n山路配绳索、海路配提灯。晴天或装备匹配即可送达；两者同时满足获得嘉奖。坏天气且装备不匹配则失败。也可主动返回营地。\n所有节点、动作、掷骰与条件均在 flow.json，无 Lua、WASM 或 CPU 脚本。请保留真人席位，重新开局可尝试其他选择。"})
    write(folder, "board.json", {"format": 1, "captions": [
        {"text": "LIGHTHOUSE / SUPPLY RUN", "x": 50, "y": 35, "size": 24},
        {"text": "PACK  >  WEATHER  >  ROUTE  >  DELIVERY", "x": 50, "y": 80},
        {"text": "A configuration-only adventure", "x": 50, "y": 450, "size": 16}]})


if __name__ == "__main__":
    polyomino()
    supply()
