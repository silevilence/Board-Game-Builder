"""Build the Island Settlers module from the three original Kenney ZIPs (stdlib only)."""
import json, math, shutil, sys, hashlib
from pathlib import Path
from zipfile import ZipFile

root = Path(__file__).resolve().parents[1] / 'samples/island-settlers'
root.mkdir(parents=True, exist_ok=True)
(root/'assets').mkdir(exist_ok=True)
archives = {name: ZipFile(Path(sys.argv[1])/f'kenney_{name}-pack.zip') for name in ['playing-cards','boardgame','hexagon']}
resources, entities = {}, []
def asset(key, pack, path):
    dest = 'assets/'+key+'.png'
    (root/dest).write_bytes(archives[pack].read(path)); resources[key]=dest
for name, archive in archives.items():
    lic=next(n for n in archive.namelist() if n.lower()=='license.txt')
    (root/'assets'/f'{name}-License.txt').write_bytes(archive.read(lic))
for i, terrain in enumerate(['grass','dirt','grass','sand','stone','sand'],1):
    asset(f'terrain-{i}','hexagon',f'PNG/Tiles/Terrain/{terrain.title()}/{terrain}_05.png')
for key, name in [('forest','treePine_large'),('brick','rockBrown_large'),('wool','treeRound_small'),('grain','hay'),('ore','crystals1'),('village','medievalHouse'),('city','castle_small')]:
    candidates=[p for p in archives['hexagon'].namelist() if p==f'PNG/Objects/{name}.png']
    if not candidates:
        fallback={'forest':'tree','brick':'rock','wool':'bush','village':'house'}[key]
        candidates=[p for p in archives['hexagon'].namelist() if p.startswith('PNG/Objects/'+fallback) and p.endswith('.png')]
    asset(key,'hexagon',sorted(candidates)[0])
for i in range(1,7): asset(f'dice-{i}','boardgame',f'PNG/Dice/dieWhite{i}.png')
asset('robber','boardgame','PNG/Pieces (Black)/pieceBlack_single00.png')
for i, card in enumerate(['card_clubs_J','card_hearts_Q','card_spades_K'],1):
    asset(f'development-{i}','playing-cards',f'PNG/Cards (large)/{card}.png')
asset('card-back','playing-cards','PNG/Cards (large)/card_back.png')
def comp(t,d): return {'type':t,'data':d}
def entity(id,label,x,y,props=None,images=None,extra=None):
    e={'id':id,'components':[comp('position',{'x':x,'y':y}),comp('faces',{'names':label if isinstance(label,list) else [label or id],'resources':images or [],'up':True}),comp('properties',props or {})]+(extra or [])}
    entities.append(e); return e
def sprite(id,label,x,y,w,h,image,layer=0,**props):
    return entity(id,label,x,y,dict(viewStyle=1,viewWidth=w,viewHeight=h,viewLayer=layer,**props),[image] if image else [])
# Flat-top artwork is displayed on a pointy-top logical hex geometry using configured dimensions.
coords=[(q,r) for r in range(-2,3) for q in range(-2,3) if abs(q+r)<=2]
vertices, edges, tiles = [], [], []
index={}; edgeindex={}; radius=64
types=[1,2,3,4,5,1,4,3,2,6,5,1,4,3,2,5,3,4,1]
numbers=[5,2,6,3,8,10,9,12,11,0,4,8,10,9,4,5,6,3,11]
for i,(q,r) in enumerate(coords,1):
    x,y=470+radius*math.sqrt(3)*(q+r/2),340+radius*1.5*r
    vs=[]
    for n in range(6):
        a=math.radians(30+60*n); pt=(round(x+radius*math.cos(a),3),round(y+radius*math.sin(a),3))
        if pt not in index: index[pt]=len(vertices)+1;vertices.append({'x':pt[0],'y':pt[1],'tiles':[],'neighbors':[]})
        v=index[pt];vertices[v-1]['tiles'].append(i);vs.append(v)
    for a,b in zip(vs,vs[1:]+vs[:1]):
        key=tuple(sorted([a,b]))
        if key not in edgeindex:
            edgeindex[key]=len(edges)+1;edges.append(key)
            vertices[a-1]['neighbors'].append(b);vertices[b-1]['neighbors'].append(a)
    tiles.append({'x':x,'y':y,'vs':vs})
    sprite(f'tile-{i}',f'{["森林","丘陵","牧场","麦田","矿山","荒漠"][types[i-1]-1]} {numbers[i-1] or ""}',x,y,110,126,f'terrain-{types[i-1]}',0,tile=i,resource=types[i-1],number=numbers[i-1],viewLabel=1,viewLabelY=22,viewColor=0xF4E6C4)
    if types[i-1]!=6: sprite(f'decor-{i}','',x,y-8,38,40,['forest','brick','wool','grain','ore'][types[i-1]-1],1,viewPassive=1)
for i,(a,b) in enumerate(edges,1):
    va,vb=vertices[a-1],vertices[b-1]
    entity(f'edge-{i}',f'道路 {i}',(va['x']+vb['x'])/2,(va['y']+vb['y'])/2,
        {'a':a,'b':b,'player':0,'viewStyle':2,'viewLayer':2,'viewWidth':18,'viewHeight':18,'viewLineX':round(vb['x']-va['x']),'viewLineY':round(vb['y']-va['y']),'viewColor':0x537778,'viewLabel':0})
for i,v in enumerate(vertices,1):
    props={'vertex':i,'player':0,'level':0,'viewStyle':1,'viewWidth':16,'viewHeight':16,'viewLayer':3,'viewLabel':1,'viewLabelY':-7,'viewNoImage':1,'viewColor':0xD5C6A1}
    for j,t in enumerate(v['tiles'],1): props[f't{j}']=t
    for j,n in enumerate(v['neighbors'],1): props[f'n{j}']=n
    entity(f'vertex-{i}',[str(i),f'村庄{i}',f'城市{i}'],v['x'],v['y'],props,['','village','city'])
sprite('robber','强盗封锁',tiles[9]['x'],tiles[9]['y']+12,28,40,'robber',4,viewLabel=0)
colors=[0xDF785A,0x57AFD0,0xDAB357,0x85BA83]
for i in range(1,5):
    y=145+(i-1)*116
    entity(f'stock-p{i}',f'{["珊瑚","海蓝","金麦","松绿"][i-1]} · p{i}\n{{score}} 分 · 声望 {{prestige}}\n木{{wood}}  砖{{brick}}  羊{{wool}}\n麦{{grain}}  矿{{ore}}',100,y,
        dict(wood=2,brick=2,wool=2,grain=2,ore=2,prestige=0,score=0,roads=0,buildings=0,cities=0,played=0,viewStyle=3,viewWidth=170,viewHeight=102,viewLayer=5,viewColor=colors[i-1],viewLabel=1))
    entity(f'hand-p{i}',f'p{i} 发展牌',270+(i-1)*150,640,dict(viewWidth=140,viewHeight=144,viewCardWidth=60,viewCardHeight=88,viewFanY=10),extra=[comp('container',{'capacity':30,'ordered':True,'contents':[]}),comp('owner',{'seat':f'p{i}'})])
for i in range(1,25):
    kind=(i-1)%3+1
    entity(f'dev-{i}', ['骑士 · 声望 +1','丰收 · 每种资源 +1','工匠 · 木砖各 +2'][kind-1],0,0,{'cardType':kind},[f'development-{kind}'],[comp('visibility',{'mode':'hidden'})])
entity('deck','发展牌库',840,630,{'viewHidden':1},extra=[comp('container',{'capacity':24,'ordered':True,'contents':[f'dev-{i}' for i in range(1,25)]})])
entity('discard','已使用',0,0,{'viewHidden':1},extra=[comp('container',{'capacity':24,'ordered':True,'contents':[]})])
for i in range(1,3):
    entity(f'die-{i}',['骰子']*6,800+(i-1)*62,175,{'value':1,'viewStyle':1,'viewWidth':48,'viewHeight':48,'viewLayer':5,'viewLabel':0},[f'dice-{n}' for n in range(1,7)],[comp('dice',{'values':list(range(1,7))})])
entity('match','群岛开拓  /  ISLAND SETTLERS\n第 {round}/12 轮  ·  7 分获胜  ·  骰子 {lastRoll} 点  ·  简化规则 / M3 总体示例',470,42,dict(viewStyle=3,viewWidth=860,viewHeight=70,viewLayer=6,viewLabel=1,viewColor=0xE3C887,setup=0,lastVertex=0,round=1,turns=0,actions=0,auction=1,robber=10,lastRoll=0,auctionWinner=0,auctionPrice=0,targetScore=7,maxRounds=12))
entity('guide','道路 木1砖1  ·  村庄 木1砖1羊1麦1  ·  城市 麦2矿3',470,592,dict(viewStyle=1,viewWidth=700,viewHeight=28,viewLayer=5,viewLabel=1,viewColor=0xD7D8BC))
entity('trade-counter','港口成交\n{wasm.counter} 次',833,280,dict(viewStyle=3,viewWidth=140,viewHeight=74,viewLayer=5,viewLabel=1,viewColor=0xE3C887),extra=[comp('wasm.counter',{'value':0,'min':0,'max':1000})])
shutil.copyfile(root.parent/'wasm-counter/counter.wasm',root/'counter.wasm')
plugin={'id':'counter','version':'1','api':1,'kind':'wasm','file':'counter.wasm','sha256':hashlib.sha256((root/'counter.wasm').read_bytes()).hexdigest().upper(),'permissions':['component','command']}
module={'format':1,'id':'island-settlers','version':'1.0.0','rules':'rules.lua','cpu':'cpu.lua','resources':resources,'runtimeFiles':['README.md']+[f'assets/{p}-License.txt' for p in archives], 'plugins':[plugin], 'seats':[{'id':f'p{i}','team':f'p{i}','controller':'human' if i<3 else 'cpu'} for i in range(1,5)],'phases':['welcome','settle','road','roll','produce','build','robber','auction'],'layout':{'kind':'free','originX':0,'originY':0},'entities':entities}
# The empty face represents a bare intersection; omit empty resource references via a real sprite.
asset('empty','boardgame','PNG/Pieces (White)/pieceWhite_single00.png')
for e in entities:
    for c in e['components']:
        if c['type']=='faces': c['data']['resources']=['empty' if x=='' else x for x in c['data']['resources']]
(root/'module.json').write_text(json.dumps(module,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for archive in archives.values(): archive.close()
print(f'{len(tiles)} tiles, {len(vertices)} intersections, {len(edges)} edges; {len(resources)} resources')
