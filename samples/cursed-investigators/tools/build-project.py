"""Produce UTF-8 author edits; apply with released bgb call files.write.

The generator is sample content, not an engine extension. Original scans are untouched.
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
objects = [
 ('通灵板',2,'die2',[('same',3),('same',4),('run',5)]),
 ('恐怖娃娃',4,'luck',[('value',4,4),('value',4,5),('value',5,6)]),
 ('悲剧镜子',4,'flip',[('value',4,1),('value',4,3),('value',5,5)]),
 ('针扎人偶',2,'die5',[('value',3,1),('run',5),('run',5)]),
 ('芭蕾舞音乐盒',3,'die1',[('run',4),('same',4),('run',5)]),
 ('诡异面具',3,'shield',[('run',4),('groups',2,2),('run',5)]),
 ('活肖像画',3,'health',[('run',4),('same',4),('same',5)]),
 ('禁书',3,'knowledge',[('run',4),('min',4,4),('run',5)]),
 ('玩偶匣',2,'ignore',[('min',3,4),('same',4),('same',5)]),
 ('快乐猴子',4,'die4',[('value',3,2),('value',4,4),('value',5,6)]),
 ('旧玩具屋',3,'die3',[('groups',2,2),('same',4),('same',5)]),
 ('诅咒录像',5,'resolve',[('compound',1,4,3,1),('compound',1,1,3,2),('compound',2,2,3,1)]),
 ('古怪护符',3,'die6',[('value',4,6),('same',5),('run',5)]),
 ('命运机器',4,'health',[('min',4,3),('groups',2,2),('run',6)])]
# Abilities are listed in reveal order: bottom, middle, top.
spirits = [
 ('poltergeist','吵闹鬼',1,2,[1,8,11],[('groups',2,3)]),
 ('revenant','还魂者',1,3,[2,5,10],[('min',5,4)]),
 ('phantasm','幻象',1,3,[3,6,9],[('run',5)]),
 ('apparition','幻影',1,2,[4,7,12],[('value',4,2)]),
 ('wraith','幽灵',2,4,[3,13,14],[('run',5),('run',6)]),
 ('banshee','报丧女妖',2,3,[1,8,15],[('groups',3,3),('run',5)]),
 ('specter','幽魂',2,3,[4,16,6],[('run',5),('value',5,3)]),
 ('drowned','溺鬼',2,4,[2,17,18],[('run',4),('min',6,5)]),
 ('ghost','鬼魂',3,5,[19,16,20],[('value',5,2),('value',5,3)]),
 ('shade','阴魂',3,6,[21,22,23],[('run',5),('min',6,5)]),
 ('ghoul','食尸鬼',3,4,[24,22,25],[('run',6),('value',6,6)]),
 ('evil-spirit','恶魂',3,6,[26,13,27],[('run',6),('run',6)])]
chars=['celeste','eleanor','leland']
profs=['reluctant-medium','former-detective','paranormal-investigator']
def lua(v):
    if isinstance(v,str):return json.dumps(v,ensure_ascii=False)
    if isinstance(v,(int,float)):return str(v)
    if isinstance(v,dict):return '{'+','.join('['+lua(k)+']='+lua(x) for k,x in v.items())+'}'
    return '{'+','.join(lua(x) for x in v)+'}'
data='local OBJECTS='+lua([dict(name=n,score=s,reward=r,challenges=c) for n,s,r,c in objects])+'\n'
data+='local SPIRITS='+lua([dict(id=i,name=n,level=l,score=s,caps=a,challenges=c) for i,n,l,s,a,c in spirits])+'\n'
logic=(ROOT/'tools/game-logic.lua').read_text(encoding='utf-8')
entities={}
def entity(id,faces=None,resources=None,x=0,y=0,props=None,**kw):
    e=dict(id=id,properties=props or {},x=x,y=y,**kw)
    if faces is not None:
        e['faces']=faces
        e['properties']['shownFace']=0
    if resources is not None:e['faceResources']=resources
    entities[id]=e
    return e
def sprite(id,resources,faces,x,y,w,h,**props):
    return entity(id,faces,resources,x,y,dict(viewStyle=1,viewWidth=w,viewHeight=h,**props))
def container(id,contents):
    # Containers expose only their presentation settings. Every stored card remains hidden.
    entity(id,props={'viewHidden':1},isContainer=True,capacity=30,ordered=True,contents=contents,visibility='public')
meta=['fresh','stage','character','profession','campaign','beginner','game','health','luck','resolve','knowledge','initialKnowledge','maxHealth','score','captured','current','object','spirit','objectDone','spiritDone','cap1','cap2','cap3','ignoreCap','ignoreObject','challenged','free','paidLuck','paidResolve','healthLost','voodoo','portrait','machine','modifier','endIndex','encounterWon','pendingWarp','pendingChallenge','choice','dealSerial','turns','loss','autoSteps']
props={p:0 for p in meta};props.update(stage=1,character=3,profession=3,game=1,viewHidden=1,health=7,maxHealth=7,luck=2,resolve=5,knowledge=3,initialKnowledge=3)
entity('match',props=props)
sprite('character',['character-'+c for c in chars],['塞莱斯特','埃莉诺','利兰'],130,185,180,260)
sprite('profession',['profession-'+p for p in profs],['不情愿的灵媒','前侦探','灵异调查员'],130,445,180,260)
sprite('deck',['object-back'],['咒物牌库'],320,130,90,134)
for i in range(1,8):
    sprite('slot'+str(i),['object-back']+['object-%03d'%n for n in range(1,15)],['未分配']+[o[0] for o in objects],360+(i-1)%3*120,250+(2-(i-1)//3)*170,106,155,viewHidden=1,object=0,done=0,level=1)
    container('pair'+str(i),[])
sprite('encounter',['object-%03d'%n for n in range(1,15)], [o[0] for o in objects],820,260,218,320,viewHidden=1)
sprite('manifestation',['spirit-'+s[0] for s in spirits],[s[1] for s in spirits],820,260,218,320,viewHidden=1)
# Initially only the revealed effect text is exposed; full spirit face appears after the object.
for i in range(1,4):
    entity('ability'+str(i),['尚未揭露'],x=810,y=450+(i-1)*38,props={'viewStyle':1,'viewWidth':280,'viewHeight':30,'viewLabel':1,'viewLabelY':-12,'viewHidden':1})
ability_names=['尚未揭露','威胁 · 露出时 −1 决心','跟随 · 结束需保留骰子','惊吓 · 露出时 −1 知识','褪色 · 露出时 −1 幸运','扰动 · 初始最大骰 −1','动摇 · 初始重掷一个保留骰','弥散 · 首次决心额外 −1','啃咬 · 池中有 6 则 −1 健康','燃烧 · 池中有 1 则 −1 健康','劈砍 · 池中有奇数则 −1 健康','吵闹 · 无挑战 −1 决心','扰乱 · 无挑战 −1 幸运','恫吓 · 初始新骰全部 −1','恐吓 · 无挑战扣知识或健康','狡猾 · 使用 1 需付决心','咆哮 · 首次幸运额外 −1 决心','孤立 · 初始同点骰重掷','耗竭 · 无挑战按未完成项扣健康','消失 · 露出时 −2 幸运','模糊 · 初始 3 重掷／付决心','麻痹 · 露出扣幸运或健康','折磨 · 池中有 1 则 −1 知识','欺骗 · 挑战多一骰，最多六骰','粉碎 · 露出时 −2 决心','顽固 · 挑战另付决心或幸运','惊骇 · 露出时 −2 知识','变形 · 完成挑战 −1 知识']
for i in range(1,4):entities['ability'+str(i)]['faces']=ability_names
for i in range(1,7):
    sprite('die'+str(i),['die'+str(n) for n in range(1,7)],[str(n) for n in range(1,7)],290+i*65,700,48,48,status=0,slot=0,selected=0,value=1,viewHidden=1)
    entities['die'+str(i)]['diceFaces']=[1,2,3,4,5,6]
for i in range(1,15):
    sprite('reward'+str(i),['die'+str(n) for n in range(1,7)],[str(n) for n in range(1,7)],320+(i-1)*36,765,30,30,status=0,value=1,selected=0,viewHidden=1)
    entity('log'+str(i),props={'captured':0,'available':0,'used':0,'viewHidden':1})
for i in range(1,13):entity('observation'+str(i),props={'count':0,'viewHidden':1})
for i in range(1,4):
    sprite('mark'+str(i),['marker'],['完成'],750+(i-1)*50,415,16,16,viewHidden=1,viewLayer=10)
for name,x in [('luck',85),('resolve',115),('knowledge',145),('health',175)]:
    sprite('track-'+name,['marker'],[name],x,575,12,12,viewLayer=10)
entity('status',['第 {game} 局　声望 {score}　健康 {health}\n幸运 {luck}　决心 {resolve}　知识 {knowledge}'],x=490,y=55,props=dict(viewStyle=1,viewWidth=650,viewHeight=60,viewLabel=1,viewLabelY=-20,viewPassive=1,game=1,score=0,health=0,luck=0,resolve=0,knowledge=0))
hints=['选择角色、职业和玩法，然后开始调查。','处理完本层才能向上一层：选择一件咒物。','新能力已揭露：选择支付方式。','初始掷骰修正：选择是否抵抗。','保留骰子：双击选中要重掷的骰子，或全部保持。','双击骰子选择；可完成右侧列出的挑战，或调整、保留、使用奖励。','挑战完成：立即选择一项属性奖励。','回合结束：选择能力的扣除方式。','首局完成，进入第二局继续战役。','全部遭遇结算，调查成功！可翻阅日志，或从游戏菜单重新开始。','健康耗尽，调查结束。可翻阅日志，或从游戏菜单重新开始。','本回合挑战已完成：可以安排剩余骰子、使用奖励，然后结束回合。','命运机器：先双击选择一颗初始骰子，锁定本回合的惩罚点数。','骰池已空：可以使用尚未用的捕获奖励，或结束回合。','遭遇已结算：安排剩余骰子，结束回合后选择下一件咒物。']
entity('hint',hints,x=520,y=820,props=dict(viewStyle=1,viewWidth=900,viewHeight=35,viewLabel=1,viewLabelY=-12,viewPassive=1))
container('objectDeck',['object'+str(i) for i in range(1,15)])
container('discard',[])
for i in range(1,15):entity('object'+str(i),props={'index':i},container='objectDeck',visibility='hidden')
for level in range(1,4):
    ids=['spirit'+str(i) for i in range((level-1)*4+1,level*4+1)]
    container('spiritDeck'+str(level),ids)
    for i in range((level-1)*4+1,level*4+1):entity('spirit'+str(i),props={'index':i},container='spiritDeck'+str(level),visibility='hidden')
# Table composition uses original aspect ratios. Character/profession cards are landscape.
entity('tablecloth',['调查桌面'],['tabletop'],props={'viewTablecloth':1,'viewWidth':1100,'viewHeight':870})
for id,y in [('character',220),('profession',365)]:
    entities[id].update(x=165,y=y,face=2);entities[id]['properties'].update(viewWidth=250,viewHeight=145,shownFace=2)
for name,x,y in [('luck',173,334.55),('resolve',208,244.65),('knowledge',238,308.45),('health',270,192.45)]:
    entities['track-'+name].update(x=x,y=y)
entities['status']['properties'].update(health=7,luck=2,resolve=5,knowledge=5)
entities['deck'].update(x=670,y=195);entities['deck']['properties'].update(viewWidth=72,viewHeight=124)
for id in ['encounter','manifestation']:
    entities[id].update(x=902,y=300);entities[id]['properties'].update(viewWidth=187,viewHeight=320)
for i in range(1,4):
    e=entities['ability'+str(i)];e.update(x=902,y=500+(i-1)*64,faceResources=['ability1']+['ability'+str(n) for n in range(1,28)])
    e['properties'].update(viewLabel=0,viewWidth=270,viewHeight=60)
    entities['mark'+str(i)].update(x=850+(i-1)*52,y=465)
    entities['mark'+str(i)]['properties'].update(viewWidth=8,viewHeight=8)
for i in range(1,15):sprite('captured'+str(i),['object-%03d'%i],[objects[i-1][0]],65+(i-1)%4*65,555+(i-1)//4*76,36,62,viewHidden=1)
for id,text,x,y,w in [('heading','咒物调查员  /  THE INVESTIGATOR’S DESK',60,48,970),('personal','调查员档案',50,130,240),('tiers','遭遇目录  ·  由下至上调查',340,130,370),('active','当前遭遇',760,130,290),('captures','封存物件  /  已结算',50,510,240),('pool','骰子池  ·  双击选择，右侧执行动作',340,698,690)]:
    entity('label-'+id,[text],x=x+w/2,y=y,props=dict(viewStyle=1,viewWidth=w,viewHeight=25,viewLabel=1,viewLabelY=-10,viewPassive=1,viewColor=0xCDBB92))
entities['status'].update(x=550,y=83)
entities['hint'].update(x=550,y=840)
for i in range(1,8):entities['slot'+str(i)]['properties'].update(viewWidth=85,viewHeight=145)
state={'format':1,'state':{'seats':[{'id':'p1','team':'investigator','controller':'human'}],'phases':['investigation'],'phase':'investigation','layout':{'kind':'free','originX':0,'originY':0},'entities':entities},'resources':{}}
def plugin(id,config=None):return dict(id=id,version='1',api=1,configuration=config or {})
images=[{'path':a['path'].replace('runtime/','assets/runtime/'),'name':a['id']} for a in json.loads((ROOT/'assets/runtime/manifest.json').read_text())['assets']]
images += [dict(path='assets/icons/kenney-board-game-icons/png/dice_'+str(i)+'.png',name='die'+str(i)) for i in range(1,7)]
images += [dict(path='assets/icons/kenney-board-game-icons/png/d6.png',name='marker')]
images += [dict(path='assets/table/tabletop.png',name='tabletop')]
images += [dict(path=a['path'],name=a['id']) for a in json.loads((ROOT/'assets/table/manifest.json').read_text())['abilities']]
startup=dict(format=1,id='cursed-investigators',version='1.0.0',plugins=[plugin('kernel'),plugin('definition.state',{'path':'position.json'}),plugin('flow',{'path':'flow.json','maxAutomaticSteps':64}),plugin('lua.cpu',{'file':'game.lua'}),plugin('session.local'),plugin('recording'),plugin('resources',{'sources':[{'path':'presentation.json','name':'player.presentation'},{'path':'assets/icons/kenney-board-game-icons/LICENSE.txt','name':'kenney.license'}]}),plugin('resources.images',{'sources':images}),plugin('resources.font',{'path':'assets/fonts/noto-sans-cjk-sc/NotoSansCJKsc-Regular.otf','license':'assets/fonts/noto-sans-cjk-sc/LICENSE.txt'}),plugin('resources.animations',{'path':'animations.json'})])
startup['plugins'].append(plugin('resources.documents',{'path':'documents.json'}))
nodes=[dict(id='start',kind='start'),dict(id='win',kind='end',winners=['p1']),dict(id='loss',kind='end',winners=[])]
edges=[dict(id='begin',**{'from':'start','to':'configure'})]
routes={
 'configure':('player',{'done':'setup'}),
 'setup':('system',{'done':'arrange'}),
 'arrange':('system',{'done':'target'}),
 'target':('player',{'done':'enter'}),
 'enter':('system',{'done':'roll','choice':'revealChoice','dead':'loss'}),
 'revealChoice':('player',{'done':'roll','dead':'loss'}),
 'roll':('system',{'done':'modify'}),
 'modify':('system',{'again':'modify','done':'reserve','choice':'initialChoice','dead':'loss'}),
 'initialChoice':('player',{'done':'modify','dead':'loss'}),
 'reserve':('player',{'done':'sync'}),
 'sync':('system',{'done':'turn','dead':'loss'}),
 'turn':('player',{'done':'sync','reward':'reward','end':'endTurn','dead':'loss'}),
 'reward':('player',{'done':'afterReward'}),
 'afterReward':('system',{'done':'turn','dead':'loss'}),
 'endTurn':('system',{'again':'endTurn','choice':'endChoice','roll':'roll','target':'target','campaign':'campaign','win':'win','dead':'loss'}),
 'endChoice':('player',{'done':'endTurn','dead':'loss'}),
 'campaign':('player',{'done':'setup'})}
for name,(kind,branches) in routes.items():
    n=dict(id=name,kind=kind,results=list(branches))
    if kind=='player':n.update(seat={'fixed':'p1'},actionsScript={'path':'game.lua','entry':'actions'},decideScript={'path':'game.lua','entry':'choose'},parameters={'node':name})
    else:n['script']={'path':'game.lua','entry':name}
    nodes.append(n)
    for result,to in branches.items():edges.append(dict(id=name+'-'+result,**{'from':name,'to':to},result=result))
flow=dict(format=1,nodes=nodes,edges=edges)
tracks=[]
for i in range(1,8):tracks.append(dict(entity='slot'+str(i),source='deck',backResource='object-back',keys=[dict(timeMs=300+i*70,x=0,y=0,rotation=-8),dict(timeMs=400+i*70,x=(-1)**i*12,y=-10,rotation=8),dict(timeMs=800+i*90,x=0,y=0,rotation=0)]))
tracks.append(dict(entity='deck',keys=[dict(timeMs=0,x=0,y=0,rotation=0),dict(timeMs=100,x=-12,y=0,rotation=-7),dict(timeMs=200,x=12,y=0,rotation=7),dict(timeMs=300,x=0,y=0,rotation=0)]))
animations={'format':1,'clips':[dict(id='deal',trigger={'entity':'match','property':'dealSerial'},tracks=tracks)]}
files={name:json.dumps(value,ensure_ascii=False,indent=2)+'\n' for name,value in {'position.json':state,'game.start.json':startup,'flow.json':flow,'animations.json':animations,'presentation.json':dict(title='咒物调查员 · 调查档案',help='标准单人：从下层开始，捕获全部六个咒物并击败对应咒灵。\n战役：首局后保留日志和未用奖励，第二局七个遭遇。\n每回合至多完成一项挑战，立即获得幸运／决心／知识之一（初学者可选健康）。\n掷骰数量＝知识－保留骰子。每回合一次免费重掷；幸运重掷，决心改点。\n职业格每格一骰，立即获得二选一效果，留到下回合。\n未完成挑战的回合结束失去健康，另结算已揭露能力。\n咒物三项完成后奖励可用、咒物效果停止；击败咒灵才记录捕获与声望。\n属性按格移动，知识有重复数值。健康到骷髅即失败。\n点骰选中后调整／花费；未选择骰子时挑战自动选一组合法骰子。\n所有缺失边界采用本 sample 的 RULINGS.md，不声称官方补充规则。',showActions=True)}.items()}
files.update({'game.lua':data+logic,'rules.lua':None,'cpu.lua':None})
presentation=json.loads(files['presentation.json']);presentation.update(width=1820,height=1000,statusEntity='hint')
files['presentation.json']=json.dumps(presentation,ensure_ascii=False,indent=2)
def field(x,y,entity,prop,**opts):return dict(x=x,y=y,size=30,binding=dict(entity=entity,property=prop,**opts))
captures=[dict(x=175,y=128,size=28,text='单人调查档案'),field(600,128,'match','character',values={'1':'塞莱斯特','2':'埃莉诺','3':'利兰'}),field(175,178,'match','profession',values={'1':'不情愿的灵媒','2':'前侦探','3':'灵异调查员'}),field(600,178,'match','score')]
rows=[305,497,689,881,1069,1261,1450]
for i in range(1,15):
    x=237 if i%2 else 689;y=rows[(i-1)//2]
    captures += [field(x,y,'log'+str(i),'captured',hideZero=True,nonzeroText='✓'),field(x,y+37,'log'+str(i),'used',hideZero=True,nonzeroText='✓')]
observations=[]
for x,ids in [(280,[4,7,9,1,6,11]),(743,[3,5,12,2,8,10])]:
    for y,i in zip([251,462,676,947,1159,1373],ids):
        f=field(x,y,'observation'+str(i),'count',hideZero=True);f.update(size=25,text='驱散 ×{value}');observations.append(f)
sizes={a['id']:a['size'] for a in json.loads((ROOT/'assets/runtime/manifest.json').read_text())['assets']}
def page(title,image,fields):
    w,h=sizes[image];return dict(title=title,image=image,width=w,height=h,fields=fields)
documents=dict(format=1,panels=[dict(id='journal',title='调查日志',permanent=True,width=330,pages=[page('捕获目录','journal-captures',captures),page('灵异观察','journal-observations',observations)]),dict(id='reference',title='简要说明',pages=[page('回合流程','reference-turn',[])])])
files['documents.json']=json.dumps(documents,ensure_ascii=False,indent=2)
import sys
out=Path(sys.argv[1]);out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(json.dumps({'files':files},ensure_ascii=False),encoding='utf-8')
print('Author changes:',out)



