"""Deterministic sample artwork and cropped, already-public ability bands.

Scans are never overwritten. No OCR, generative repair or retypesetting of card content.
"""
import hashlib,json,random
from pathlib import Path
from PIL import Image,ImageDraw,ImageFilter
ROOT=Path(__file__).resolve().parents[1]
out=ROOT/'assets/table';out.mkdir(exist_ok=True)
random.seed(20261017)
w,h=1100,870
image=Image.new('RGB',(w,h));p=image.load()
for y in range(h):
    for x in range(w):
        grain=random.randrange(-3,4)
        light=max(0,1-((x-530)/800)**2-((y-360)/750)**2)
        p[x,y]=(int(20+light*13)+grain,int(29+light*17)+grain,int(27+light*12)+grain)
draw=ImageDraw.Draw(image)
draw.rounded_rectangle((8,8,w-9,h-9),radius=24,outline='#6d654d',width=2)
draw.rounded_rectangle((17,17,w-18,h-18),radius=18,outline='#343e34',width=1)
for box in [(30,115,300,470),(320,115,715,660),(735,115,1070,660),(30,490,300,820),(320,680,1070,820)]:
    draw.rounded_rectangle(box,radius=12,fill='#192420',outline='#4c5545',width=1)
for x,y in [(28,28),(1072,28),(28,842),(1072,842)]:
    draw.ellipse((x-3,y-3,x+3,y+3),fill='#938363')
image.save(out/'tabletop.png',optimize=True)
mapping=[('poltergeist',[1,8,11]),('revenant',[2,5,10]),('phantasm',[3,6,9]),('apparition',[4,7,12]),('wraith',[3,13,14]),('banshee',[1,8,15]),('specter',[4,16,6]),('drowned',[2,17,18]),('ghost',[19,16,20]),('shade',[21,22,23]),('ghoul',[24,22,25]),('evil-spirit',[26,13,27])]
entries=[];seen=set()
for spirit,caps in mapping:
    path=ROOT/'assets/runtime'/('spirit-'+spirit+'.png')
    with Image.open(path) as card:
        for i,cap in enumerate(caps):
            if cap in seen:continue
            seen.add(cap)
            top,bottom=[(.845,.975),(.720,.845),(.598,.720)][i]
            box=(round(card.width*.035),round(card.height*top),round(card.width*.945),round(card.height*bottom))
            target=out/('ability-%02d.png'%cap);card.crop(box).save(target,optimize=True)
            entries.append(dict(id='ability'+str(cap),path='assets/table/'+target.name,source=str(path.relative_to(ROOT)).replace('\\','/'),sourceSha256=hashlib.sha256(path.read_bytes()).hexdigest(),box=box,sha256=hashlib.sha256(target.read_bytes()).hexdigest()))
(out/'manifest.json').write_text(json.dumps(dict(format=1,method='Canonical repeated ability text cropped from scanned cards; procedural felt background, seed 20261017',abilities=entries),ensure_ascii=False,indent=2),encoding='utf-8')
print('Prepared tabletop and',len(entries),'canonical public ability bands')
