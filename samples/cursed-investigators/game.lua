local OBJECTS={{["name"]="通灵板",["score"]=2,["reward"]="die2",["challenges"]={{"same",3},{"same",4},{"run",5}}},{["name"]="恐怖娃娃",["score"]=4,["reward"]="luck",["challenges"]={{"value",4,4},{"value",4,5},{"value",5,6}}},{["name"]="悲剧镜子",["score"]=4,["reward"]="flip",["challenges"]={{"value",4,1},{"value",4,3},{"value",5,5}}},{["name"]="针扎人偶",["score"]=2,["reward"]="die5",["challenges"]={{"value",3,1},{"run",5},{"run",5}}},{["name"]="芭蕾舞音乐盒",["score"]=3,["reward"]="die1",["challenges"]={{"run",4},{"same",4},{"run",5}}},{["name"]="诡异面具",["score"]=3,["reward"]="shield",["challenges"]={{"run",4},{"groups",2,2},{"run",5}}},{["name"]="活肖像画",["score"]=3,["reward"]="health",["challenges"]={{"run",4},{"same",4},{"same",5}}},{["name"]="禁书",["score"]=3,["reward"]="knowledge",["challenges"]={{"run",4},{"min",4,4},{"run",5}}},{["name"]="玩偶匣",["score"]=2,["reward"]="ignore",["challenges"]={{"min",3,4},{"same",4},{"same",5}}},{["name"]="快乐猴子",["score"]=4,["reward"]="die4",["challenges"]={{"value",3,2},{"value",4,4},{"value",5,6}}},{["name"]="旧玩具屋",["score"]=3,["reward"]="die3",["challenges"]={{"groups",2,2},{"same",4},{"same",5}}},{["name"]="诅咒录像",["score"]=5,["reward"]="resolve",["challenges"]={{"compound",1,4,3,1},{"compound",1,1,3,2},{"compound",2,2,3,1}}},{["name"]="古怪护符",["score"]=3,["reward"]="die6",["challenges"]={{"value",4,6},{"same",5},{"run",5}}},{["name"]="命运机器",["score"]=4,["reward"]="health",["challenges"]={{"min",4,3},{"groups",2,2},{"run",6}}}}
local SPIRITS={{["id"]="poltergeist",["name"]="吵闹鬼",["level"]=1,["score"]=2,["caps"]={1,8,11},["challenges"]={{"groups",2,3}}},{["id"]="revenant",["name"]="还魂者",["level"]=1,["score"]=3,["caps"]={2,5,10},["challenges"]={{"min",5,4}}},{["id"]="phantasm",["name"]="幻象",["level"]=1,["score"]=3,["caps"]={3,6,9},["challenges"]={{"run",5}}},{["id"]="apparition",["name"]="幻影",["level"]=1,["score"]=2,["caps"]={4,7,12},["challenges"]={{"value",4,2}}},{["id"]="wraith",["name"]="幽灵",["level"]=2,["score"]=4,["caps"]={3,13,14},["challenges"]={{"run",5},{"run",6}}},{["id"]="banshee",["name"]="报丧女妖",["level"]=2,["score"]=3,["caps"]={1,8,15},["challenges"]={{"groups",3,3},{"run",5}}},{["id"]="specter",["name"]="幽魂",["level"]=2,["score"]=3,["caps"]={4,16,6},["challenges"]={{"run",5},{"value",5,3}}},{["id"]="drowned",["name"]="溺鬼",["level"]=2,["score"]=4,["caps"]={2,17,18},["challenges"]={{"run",4},{"min",6,5}}},{["id"]="ghost",["name"]="鬼魂",["level"]=3,["score"]=5,["caps"]={19,16,20},["challenges"]={{"value",5,2},{"value",5,3}}},{["id"]="shade",["name"]="阴魂",["level"]=3,["score"]=6,["caps"]={21,22,23},["challenges"]={{"run",5},{"min",6,5}}},{["id"]="ghoul",["name"]="食尸鬼",["level"]=3,["score"]=4,["caps"]={24,22,25},["challenges"]={{"run",6},{"value",6,6}}},{["id"]="evil-spirit",["name"]="恶魂",["level"]=3,["score"]=6,["caps"]={26,13,27},["challenges"]={{"run",6},{"run",6}}}}
-- Combined with immutable card data by build-project.py. All rules belong to this sample.
local TRACKS={{3,4,4,5,5,5,6},{3,4,4,4,5,5,6},{3,4,4,5,5,5,6}}
local START={{2,3,2},{3,2,3},{2,3,3}}
local BONUS={{2,2,1},{2,2,1},{0,2,2}}
local CHARACTER_HEALTH={5,4,4}
local PROFESSION_HEALTH={2,4,3}
local E,M,C
local function begin(x)
  E={};C={}
  local source=x.state and x.state.entities or x.view.entities
  for _,e in pairs(source) do
    local p={};for k,v in pairs(e.properties) do p[k]=v end
    E[e.id]={id=e.id,properties=p,contents=e.contents,container=e.container,x=e.x,y=e.y}
  end
  M=E.match.properties
end
local function emit(c) C[#C+1]=c end
local function set(id,k,v)
  assert(E[id], 'Missing entity '..tostring(id)..' for '..tostring(k))
  if E[id].properties[k]~=v then emit{kind='set',entity=id,property=k,value=v};E[id].properties[k]=v end
end
local function put(k,v) set('match',k,v) end
local function face(id,n)
  if E[id].properties.shownFace==n then return end
  set(id,'shownFace',n);emit{kind='flip',entity=id,value=n};emit{kind='reveal',entity=id,visibility='public'}
end
local function show(id,yes) set(id,'viewHidden',yes and 0 or 1) end
local function move(id,x,y)
  if E[id].x~=x or E[id].y~=y then emit{kind='move',entity=id,x=x,y=y};E[id].x=x;E[id].y=y end
end
local function stage(n) put('stage',n);face('hint',n-1) end
local function value(id) return E[id].properties.value end
local function knowledge() return TRACKS[M.profession][M.knowledge+1] end
local function cap(n)
  for i=1,3 do if M['cap'..i]==n and M.ignoreCap~=i then return true end end
  return false
end
local function obj(n) return M.object==n and M.objectDone~=7 and M.ignoreObject==0 end
local function bit(mask,n) return math.floor(mask/2^(n-1))%2==1 end
local function count(mask) local n=0;for i=1,6 do if bit(mask,i) then n=n+1 end end;return n end
local function pool(ordinary,selected)
  local a={}
  for i=1,ordinary and 6 or 20 do
    local id=i<=6 and 'die'..i or 'reward'..(i-6);local p=E[id].properties
    if p.status==1 and (not selected or p.selected==1) then a[#a+1]=id end
  end
  return a
end
local function health(n)
  if M.health<=0 then return end
  put('health',math.max(0,M.health-n));put('healthLost',1)
  if obj(4) and M.voodoo==0 then
    put('voodoo',1)
    if M.resolve>0 then put('resolve',M.resolve-1) else put('health',math.max(0,M.health-1)) end
  end
end
local function lower(k,n)
  for i=1,n do
    if M.health<=0 then return end
    if k=='health' then health(1)
    elseif M[k]>0 then put(k,M[k]-1) else health(1) end
  end
end
local function gain(k,n) if M.health>0 then put(k,math.min(k=='health' and M.maxHealth or 6,M[k]+n)) end end
local function pay(k,n)
  if k=='resolve' and M.paidResolve==0 then put('paidResolve',1);if cap(7) then lower('resolve',1) end end
  if k=='luck' and M.paidLuck==0 then put('paidLuck',1);if cap(16) then pay('resolve',1) end end
  lower(k,n)
end
local function selection()
  local a=pool(false,true);return #a>0 and a or pool(false,false)
end
local function clearSelection() for i=1,20 do set(i<=6 and 'die'..i or 'reward'..(i-6),'selected',0) end end
local function syncVisual()
  if M.stage==6 then
    face('hint',M.encounterWon==1 and 14 or M.challenged==1 and 11 or obj(14) and M.machine==0 and 12 or #pool(false,false)==0 and 13 or 5)
  end
  for _,k in ipairs({'game','score','health','luck','resolve'}) do set('status',k,M[k]) end
  set('status','knowledge',knowledge())
  for i=1,20 do
    local id=i<=6 and 'die'..i or 'reward'..(i-6);local p=E[id].properties
    show(id,p.status>0)
    if p.status>0 then
      face(id,p.value-1)
      if i<=6 then
        if p.status==2 or p.status==3 then
          move(id,68+(p.slot-1)%2*43,345+math.floor((p.slot-1)/2)*42);set(id,'viewWidth',24);set(id,'viewHeight',24)
        else move(id,330+i*65,742-(p.selected==1 and 14 or 0));set(id,'viewWidth',44);set(id,'viewHeight',44) end
      else move(id,360+(i-7)*36,795-(p.selected==1 and 12 or 0)) end
    end
  end
  for i=1,3 do show('mark'..i,bit(M.objectDone==7 and M.spiritDone or M.objectDone,i)) end
  for i,k in ipairs({'luck','resolve','knowledge','health'}) do
    local v=M[k];local x=({173,208,238,270})[i];local y=0
    if k=='health' then
      local professional=PROFESSION_HEALTH[M.profession]
      if v<=professional then y=292.5+(0.12+0.18*(professional-v))*145
      else y=147.5+(0.85-0.18*(v-professional-1))*145 end
    else y=v<=3 and 292.5+(0.65-0.18*v)*145 or 147.5+(0.85-0.18*(v-4))*145 end
    move('track-'..k,x,y)
  end
end
local function result(r,complete)
  if M.health<=0 and M.stage~=1 then stage(11);put('loss',1);r='dead';complete=true end
  syncVisual();return {commands=C,result=r or 'done',complete=complete~=false}
end
local function initialKnowledge()
  local v=START[M.character][3]+BONUS[M.profession][3]
  for i,n in ipairs(TRACKS[M.profession]) do if n>=v then return i-1 end end
  return 6
end
local function resetAttributes()
  face('character',M.character-1);face('profession',M.profession-1)
  put('luck',START[M.character][1]+BONUS[M.profession][1]);put('resolve',START[M.character][2]+BONUS[M.profession][2])
  put('initialKnowledge',initialKnowledge());put('knowledge',M.initialKnowledge)
  put('maxHealth',CHARACTER_HEALTH[M.character]+PROFESSION_HEALTH[M.profession]);put('health',M.maxHealth)
end
local function resetTurn()
  for _,k in ipairs({'challenged','free','paidLuck','paidResolve','healthLost','voodoo','portrait','machine','ignoreCap','ignoreObject','endIndex','pendingWarp'}) do put(k,0) end
  clearSelection();put('turns',M.turns+1)
end
local function disclose(index,ability)
  put('cap'..index,ability);face('ability'..index,ability);show('ability'..index,true)
  if ability==1 then lower('resolve',1) elseif ability==3 then lower('knowledge',1)
  elseif ability==4 then lower('luck',1) elseif ability==19 then lower('luck',2)
  elseif ability==24 then lower('resolve',2) elseif ability==26 then lower('knowledge',2) end
end
local function challenges() return M.objectDone~=7 and OBJECTS[M.object].challenges or SPIRITS[M.spirit].challenges end
local function doneMask() return M.objectDone~=7 and M.objectDone or M.spiritDone end
local function patterns(ch,extra)
  local t,n,v=ch[1],ch[2],ch[3];local a={};local add=extra and 1 or 0
  local function push(values,any) a[#a+1]={values=values,any=any or 0} end
  local function repeatv(v,n) local x={};for i=1,n do x[#x+1]=v end;return x end
  if t=='run' then n=math.min(6,n+add);for start=1,7-n do local x={};for i=start,start+n-1 do x[#x+1]=i end;push(x) end
  elseif t=='same' then for j=1,6 do push(repeatv(j,math.min(6,n+add))) end
  elseif t=='value' then push(repeatv(v,math.min(6,n+add)))
  elseif t=='min' then push(repeatv(-v,math.min(6,n+add)))
  elseif t=='groups' then
    for j=1,6 do for k=1,6 do if j~=k then local x=repeatv(j,n);for z=1,v do x[#x+1]=k end;push(x,math.min(add,6-n-v)) end end end
  elseif t=='compound' then local x=repeatv(v,n);for z=1,ch[4] do x[#x+1]=ch[5] end;push(x,math.min(add,6-#x)) end
  return a
end
local function match(ch,ids)
  local extra=cap(23);local fee=obj(11) and M.luck<=1 and 1 or 0
  for _,p in ipairs(patterns(ch,extra)) do
    local used,chosen={},{};local ok=true
    for _,v in ipairs(p.values) do
      local found=nil
      for _,id in ipairs(ids) do if not used[id] and (v>0 and value(id)==v or v<0 and value(id)>=-v) then found=id;break end end
      if not found then ok=false;break end
      used[found]=true;chosen[#chosen+1]=found
    end
    if ok then
      local remain=p.any+fee
      for _,id in ipairs(ids) do if remain>0 and not used[id] then chosen[#chosen+1]=id;remain=remain-1 end end
      if remain==0 then return chosen end
    end
  end
end
local function label(ch)
  local t,n,v=ch[1],ch[2],ch[3]
  if t=='run' then return n..' 连' elseif t=='same' then return n..' 同'
  elseif t=='value' then return n..' 颗 '..v elseif t=='min' then return n..' 颗 ≥'..v
  elseif t=='groups' then return n..' 同＋'..v..' 同（不同点数）'
  else return n..' 颗 '..v..'＋'..ch[4]..' 颗 '..ch[5] end
end
local function reserveOptions(slot)
  if slot==1 then return {'knowledge',M.profession==2 and 'resolve' or 'luck'}
  elseif slot==2 then return {'knowledge','resolve'}
  elseif slot==3 then return {M.profession==1 and 'wild' or M.profession==2 and 'plus2' or 'flip','luck'}
  else return {'luck','resolve'} end
end
local TEXT={knowledge='知识',resolve='决心',luck='幸运',health='健康',flip='翻到反面',plus2='点数 +2',wild='改为任意点数'}
local function actions(x)
  begin(x);local a={};local node=x.parameters and x.parameters.node
  local function add(kind,text,entity,v,property,target,gesture)
    local cmd={kind=kind,entity=entity,value=v or 0,property=property,target=target}
    a[#a+1]={id=kind..':'..(entity or '')..':'..(v or 0)..':'..(property or '')..':'..(target or ''),label=text,command=cmd,shortcuts=gesture=='click' and {'double-click'} or nil}
  end
  if node=='configure' or M.stage==1 then
    for i,n in ipairs({'塞莱斯特','埃莉诺','利兰'}) do add('character','角色：'..n,nil,i) end
    for i,n in ipairs({'不情愿的灵媒','前侦探','灵异调查员'}) do add('profession','职业：'..n,nil,i) end
    add('campaign','模式：标准单人',nil,0);add('campaign','模式：两局战役',nil,1)
    add('beginner',M.beginner==0 and '启用初学者：允许奖励健康' or '关闭初学者',nil,1-M.beginner)
    add('start','开始调查')
  elseif node=='target' or M.stage==2 then
    local level=4
    for i=1,M.game==1 and 6 or 7 do local s=E['slot'..i].properties;if s.done==0 then level=math.min(level,s.level) end end
    for i=1,M.game==1 and 6 or 7 do local s=E['slot'..i].properties;if s.done==0 and s.level==level then add('target','调查：'..OBJECTS[s.object].name,'slot'..i,i,nil,nil,'click') end end
  elseif node=='revealChoice' or node=='endChoice' or M.stage==3 or M.stage==8 then
    local first=M.stage==3
    add('choice',first and '失去 2 幸运' or '失去 1 知识',nil,0,first and 'luck' or 'knowledge')
    add('choice','失去 1 健康',nil,1,'health')
  elseif node=='initialChoice' or M.stage==4 then
    add('initial','重掷初始点数 3 的骰子',nil,0);add('initial','支付 1 决心，忽略模糊',nil,1)
  elseif node=='reserve' or M.stage==5 then
    for i=1,6 do local id='die'..i;local p=E[id].properties;if p.status==3 then add('select','保留骰 '..i..'：'..(p.selected==1 and '保持' or '选择重掷'),id,0,nil,nil,'click') end end
    add('ready','确认保留骰选择，进入骰子池')
  elseif node=='reward' or M.stage==7 then
    for _,k in ipairs({'knowledge','resolve','luck'}) do add('attribute','挑战奖励：+1 '..TEXT[k],nil,0,k) end
    if M.beginner==1 then add('attribute','挑战奖励：+1 健康',nil,0,'health') end
  elseif node=='campaign' or M.stage==9 then add('nextGame','继续战役：第二局七个遭遇')
  elseif node=='turn' or M.stage==6 then
    local selected=pool(true,true);local anySelected=pool(false,true)
    for _,id in ipairs(pool(false,false)) do add('select',(E[id].properties.selected==1 and '取消选择 ' or '选择 ')..id..' ['..value(id)..']',id,0,nil,nil,'click') end
    if #anySelected>0 then add('clear','取消全部选择') end
    if obj(14) and M.machine==0 and #pool(true,false)>0 then
      if #selected==1 then add('machine','命运机器：固定初始点数 '..value(selected[1]),selected[1],value(selected[1])) end
      return a
    end
    if #selected>0 then
      if M.free==0 then add('reroll','免费重掷所选骰子',nil,0) end
      add('reroll','花费 1 幸运重掷所选骰子',nil,1)
      if obj(13) then add('reroll','护符：失去 1 健康重掷所选骰子',nil,2) end
    end
    if #selected==1 then
      local id=selected[1];if value(id)<6 then add('adjust','花费 1 决心：所选骰 +1',id,1) end
      if value(id)>1 then add('adjust','花费 1 决心：所选骰 −1',id,-1) end
      for slot=1,4 do
        local occupied=false;for i=1,6 do local p=E['die'..i].properties;if p.status==2 and p.slot==slot then occupied=true end end
        if not occupied then for _,effect in ipairs(reserveOptions(slot)) do
          local cost=obj(7) and M.portrait==0 and (effect=='knowledge' or effect=='resolve' or effect=='luck')
          if not cost or #pool(true,false)>=2 then
            if cost then
              for _,discard in ipairs(pool(true,false)) do if discard~=id then
                add('store','能力格 '..slot..'：保留并获得 '..TEXT[effect]..'；先弃掉骰子 '..discard:sub(4),id,tonumber(discard:sub(4)),effect,tostring(slot))
              end end
            elseif effect=='wild' then for v=1,6 do add('store','能力格 '..slot..'：保留并改为 '..v,id,v,effect,tostring(slot)) end
            elseif effect~='plus2' or value(id)<=4 then add('store','能力格 '..slot..'：保留并 '..(TEXT[effect] or effect),id,0,effect,tostring(slot)) end
          end
        end end
      end
      if obj(14) and M.machine==0 then add('machine','命运机器：固定点数 '..value(id),id,value(id)) end
    end
    if M.challenged==0 and (not obj(14) or M.machine>0) then
      for i,ch in ipairs(challenges()) do
        if not bit(doneMask(),i) and not (obj(5) and ch[1]=='run' and M.resolve==0) then
          local ids=match(ch,selection())
          if ids then
            if cap(25) then add('challenge','挑战 '..i..'：'..label(ch)..'，付决心',nil,i,'resolve');add('challenge','挑战 '..i..'：'..label(ch)..'，付幸运',nil,i,'luck')
            else add('challenge','完成挑战 '..i..'：'..label(ch),nil,i) end
          end
        end
      end
    end
    for i,o in ipairs(OBJECTS) do local log=E['log'..i].properties
      if log.available==1 and log.used==0 then
        if o.reward=='flip' then for _,id in ipairs(pool(true,false)) do add('reward','镜子奖励：翻转 '..id,id,i) end
        elseif o.reward=='shield' then for c=1,3 do if M['cap'..c]>0 then add('reward','面具奖励：忽略第 '..c..' 个已揭露能力',nil,i,tostring(c)) end end
        else add('reward','使用 '..o.name..' 捕获奖励',nil,i) end
      end
    end
    if not obj(14) or M.machine>0 or #pool(true,false)==0 then add('end','结束回合（先安排保留骰子）') end
  end
  return a
end
local function choose(x)
  begin(x);local c=x.command;local k=c.kind;local r='done';local complete=false
  if k=='character' or k=='profession' or k=='campaign' or k=='beginner' then
    put(k,c.value);if k=='character' or k=='profession' then resetAttributes() end
  elseif k=='start' then resetAttributes();complete=true
  elseif k=='target' then put('current',c.value);complete=true
  elseif k=='choice' then lower(c.property,c.property=='luck' and 2 or 1);complete=true
  elseif k=='initial' then
    if c.value==1 then pay('resolve',1) else for i=1,6 do local id='die'..i;if E[id].properties.status==4 and value(id)==3 then emit{kind='roll',entity=id} end end end
    complete=true
  elseif k=='select' then set(c.entity,'selected',1-E[c.entity].properties.selected)
  elseif k=='clear' then clearSelection()
  elseif k=='ready' then
    for i=1,6 do local id='die'..i;local p=E[id].properties
      if p.status==3 or p.status==4 then if p.status==3 and p.selected==1 then emit{kind='roll',entity=id} end;set(id,'status',1);set(id,'slot',0) end
    end
    clearSelection();complete=true
  elseif k=='reroll' then
    if c.value==0 then put('free',1) elseif c.value==1 then pay('luck',1) else health(1) end
    if M.health>0 then for _,id in ipairs(pool(true,true)) do emit{kind='roll',entity=id} end end
    clearSelection();complete=true
  elseif k=='adjust' then pay('resolve',1);if M.health>0 then set(c.entity,'value',value(c.entity)+c.value) end
  elseif k=='store' then
    local effect=c.property
    if obj(7) and M.portrait==0 and (effect=='knowledge' or effect=='luck' or effect=='resolve') then
      set('die'..c.value,'status',0);put('portrait',1)
    end
    set(c.entity,'status',2);set(c.entity,'slot',tonumber(c.target));set(c.entity,'selected',0)
    if effect=='wild' then set(c.entity,'value',c.value) elseif effect=='flip' then set(c.entity,'value',7-value(c.entity))
    elseif effect=='plus2' then set(c.entity,'value',value(c.entity)+2) else gain(effect,1) end
  elseif k=='machine' then put('machine',c.value)
  elseif k=='challenge' then
    local ids=match(challenges()[c.value],selection());assert(ids)
    if cap(25) then pay(c.property,1) end
    if cap(15) then for _,id in ipairs(ids) do if value(id)==1 then pay('resolve',1);break end end end
    if M.health>0 then
      for _,id in ipairs(ids) do set(id,'status',0) end
      put('pendingWarp',cap(27) and 1 or 0);put('pendingChallenge',c.value)
      put(M.objectDone~=7 and 'objectDone' or 'spiritDone',doneMask()+2^(c.value-1));put('challenged',1)
      clearSelection();stage(7);r='reward';complete=true
    end
  elseif k=='attribute' then gain(c.property,1);complete=true
  elseif k=='reward' then
    set('log'..c.value,'used',1);local effect=OBJECTS[c.value].reward
    if effect:sub(1,3)=='die' then local id='reward'..c.value;set(id,'status',1);set(id,'value',tonumber(effect:sub(4)))
    elseif effect=='flip' then set(c.entity,'value',7-value(c.entity))
    elseif effect=='shield' then put('ignoreCap',tonumber(c.property))
    elseif effect=='ignore' then put('ignoreObject',1)
    else gain(effect,(effect=='luck' or effect=='resolve') and 2 or 1) end
  elseif k=='end' then put('endIndex',0);r='end';complete=true
  elseif k=='nextGame' then put('game',2);resetAttributes();complete=true end
  return result(r,complete)
end
local function setup(x)
  begin(x)
  -- Restore spirit decks each game; captured objects remain in discard.
  if M.game==2 then
    for i=1,12 do local id='spirit'..i;local target='spiritDeck'..math.ceil(i/4);if E[id].container~=target then emit{kind='transfer',entity=id,target=target} end end
  end
  emit{kind='shuffle',entity='objectDeck'}
  for level=1,3 do emit{kind='shuffle',entity='spiritDeck'..level} end
  local n=M.game==1 and 6 or 7
  for i=1,n do
    local level=i<=3 and 1 or i<n and 2 or 3
    emit{kind='draw',entity='objectDeck',target='pair'..i,visibility='hidden'}
    emit{kind='draw',entity='spiritDeck'..level,target='pair'..i,visibility='hidden'}
    set('slot'..i,'level',level);set('slot'..i,'done',0)
  end
  for i=1,20 do set(i<=6 and 'die'..i or 'reward'..(i-6),'status',0) end
  put('current',0);put('object',0);put('encounterWon',0)
  return result()
end
local function arrange(x)
  begin(x);local n=M.game==1 and 6 or 7
  for i=1,n do
    local pair=E['pair'..i];local object=nil
    for _,id in ipairs(pair.contents) do if id:sub(1,6)=='object' then object=E[id].properties.index end end
    set('slot'..i,'object',object);face('slot'..i,object);show('slot'..i,true)
    local level=E['slot'..i].properties.level
    local column=level==1 and i-1 or level==2 and i-4 or 1
    move('slot'..i,390+column*120,560-(level-1)*170)
  end
  show('slot7',n==7);put('dealSerial',M.dealSerial+1);stage(2)
  return result()
end
local function enter(x)
  begin(x);resetTurn();put('fresh',1);put('encounterWon',0);put('objectDone',0);put('spiritDone',0);put('spirit',0)
  put('object',E['slot'..M.current].properties.object)
  for i=1,3 do put('cap'..i,0);show('ability'..i,false) end
  face('encounter',M.object-1);show('encounter',true);show('manifestation',false)
  local spirit=nil
  for _,id in ipairs(E['pair'..M.current].contents) do if id:sub(1,6)=='spirit' then spirit=E[id].properties.index end end
  disclose(1,SPIRITS[spirit].caps[1])
  if cap(21) then stage(3);return result('choice') end
  return result()
end
local function roll(x)
  begin(x);if M.fresh==0 then resetTurn() else put('fresh',0) end;local reserved=0
  for i=1,6 do local id='die'..i;if E[id].properties.status==2 then reserved=reserved+1;set(id,'status',3) else set(id,'status',0) end end
  local need=math.max(0,knowledge()-reserved)
  for i=1,6 do local id='die'..i;if E[id].properties.status==0 and need>0 then set(id,'status',4);set(id,'slot',0);emit{kind='roll',entity=id};need=need-1 end end
  put('modifier',1);return result()
end
local function modify(x)
  begin(x);local step=M.modifier;put('modifier',step+1)
  if step<=3 then
    local a=M.ignoreCap==step and 0 or M['cap'..step];local ids={}
    for i=1,6 do if E['die'..i].properties.status==4 then ids[#ids+1]='die'..i end end
    if a==5 and #ids>0 then local best=ids[1];for _,id in ipairs(ids) do if value(id)>value(best) then best=id end end;set(best,'value',math.max(1,value(best)-1))
    elseif a==6 then for i=1,6 do local id='die'..i;if E[id].properties.status==3 then emit{kind='roll',entity=id};break end end
    elseif a==13 then for _,id in ipairs(ids) do set(id,'value',math.max(1,value(id)-1)) end
    elseif a==17 then
      local counts={};for _,id in ipairs(ids) do counts[value(id)]=(counts[value(id)] or 0)+1 end
      for _,id in ipairs(ids) do if counts[value(id)]>1 then emit{kind='roll',entity=id} end end
    elseif a==20 then local any=false;for _,id in ipairs(ids) do if value(id)==3 then any=true end end;if any then stage(4);return result('choice') end end
    return result('again')
  elseif step==4 then
    if obj(3) then
      local highest=0;for i=1,6 do local id='die'..i;if E[id].properties.status==4 and value(id)%2==1 then highest=math.max(highest,value(id)) end end
      for i=1,6 do local id='die'..i;if E[id].properties.status==4 and value(id)==highest then set(id,'value',7-highest) end end
    elseif obj(9) then
      local six=false;for i=1,6 do local id='die'..i;if E[id].properties.status==4 and value(id)==6 then six=true end end
      if six then for i=1,6 do local id='die'..i;if E[id].properties.status==3 then emit{kind='roll',entity=id} end end end
    end
    return result('again')
  end
  stage(5);return result()
end
local function sync(x) begin(x);stage(6);return result() end
local function afterReward(x)
  begin(x)
  if M.pendingWarp==1 then lower('knowledge',1) end
  if M.health<=0 then return result('dead') end
  local spirit=nil
  for _,id in ipairs(E['pair'..M.current].contents) do if id:sub(1,6)=='spirit' then spirit=E[id].properties.index end end
  local completed=count(M.objectDone)
  for i=1,math.min(3,completed+1) do if M['cap'..i]==0 then disclose(i,SPIRITS[spirit].caps[i]) end end
  if M.objectDone==7 then
    set('log'..M.object,'available',1);put('spirit',spirit)
    show('encounter',false);face('manifestation',spirit-1);show('manifestation',true)
    if M.spiritDone==2^#SPIRITS[spirit].challenges-1 then
      set('log'..M.object,'captured',1);set('observation'..spirit,'count',E['observation'..spirit].properties.count+1)
      put('score',M.score+OBJECTS[M.object].score+SPIRITS[spirit].score);put('captured',M.captured+1)
      put('knowledge',M.initialKnowledge);set('slot'..M.current,'done',1);show('slot'..M.current,false)
      put('encounterWon',1)
      show('captured'..M.object,true)
      for i=1,3 do put('cap'..i,0);show('ability'..i,false) end
      emit{kind='transfer',entity='object'..M.object,target='discard'}
    end
  end
  stage(6);return result()
end
local function endTurn(x)
  begin(x);local index=M.endIndex;put('endIndex',index+1)
  local function unused(test) for _,id in ipairs(pool(false,false)) do if test(value(id)) then return true end end;return false end
  if index==0 then
    if obj(1) and M.challenged==0 or obj(6) and M.resolve==0 then
      for i=1,6 do local id='die'..i;if E[id].properties.status==2 then set(id,'status',1);set(id,'slot',0) end end
    end
    if M.challenged==0 then health(1) end
  elseif index<=3 then
    local a=M.ignoreCap==index and 0 or M['cap'..index]
    if a==2 then local reserve=0;for i=1,6 do if E['die'..i].properties.status==2 then reserve=reserve+1 end end;if reserve==0 then health(1) end
    elseif a==8 and unused(function(v)return v==6 end) or a==9 and unused(function(v)return v==1 end) or a==10 and unused(function(v)return v%2==1 end) then health(1)
    elseif a==11 and M.challenged==0 then lower('resolve',1)
    elseif a==12 and M.challenged==0 then lower('luck',1)
    elseif a==14 and M.challenged==0 then stage(8);return result('choice')
    elseif a==18 and M.challenged==0 then lower('health',#challenges()-count(doneMask()))
    elseif a==22 and unused(function(v)return v==1 end) then lower('knowledge',1) end
  elseif index==4 then
    if obj(2) and M.healthLost==1 then lower('luck',1)
    elseif obj(8) and M.challenged==0 then if knowledge()==6 then health(1) else gain('knowledge',1) end
    elseif obj(10) and unused(function(v)return v%2==1 end) then health(1)
    elseif obj(12) then local total=0;for _,id in ipairs(pool(false,false)) do total=total+value(id) end;if total>=8 then health(1) end
    elseif obj(14) then for _,id in ipairs(pool(false,false)) do if value(id)==M.machine then lower('luck',1) end end end
  else
    for i=1,20 do local id=i<=6 and 'die'..i or 'reward'..(i-6);if E[id].properties.status~=2 then set(id,'status',0) end end
    clearSelection();emit{kind='next',phase='investigation'}
    if M.encounterWon==1 then
      local remaining=0;for i=1,M.game==1 and 6 or 7 do if E['slot'..i].properties.done==0 then remaining=remaining+1 end end
      if remaining==0 then
        if M.campaign==1 and M.game==1 then stage(9);return result('campaign') end
        stage(10);return result('win')
      end
      stage(2);return result('target')
    end
    return result('roll')
  end
  return result('again')
end

-- CPU sees only the same projection and offered actions as a human.
local function decide(x)
  begin(x);local offers=x.actions
  local function find(kind,property,entity,value)
    for _,a in ipairs(offers) do local c=a.command;if c.kind==kind and (not property or c.property==property) and (not entity or c.entity==entity) and (value==nil or c.value==value) then return c end end
  end
  local easy=find('start') or find('target') or find('nextGame') or find('ready') or find('challenge')
  if easy then return easy end
  if M.stage==7 then
    return (M.beginner==1 and (M.health<=2 or M.knowledge>=5 and M.health<M.maxHealth-1) and find('attribute','health')) or
      (M.knowledge<6 and find('attribute','knowledge')) or (M.resolve<=M.luck and find('attribute','resolve')) or find('attribute','luck')
  end
  if M.stage==3 or M.stage==8 then return M.stage==3 and M.luck>=2 and find('choice','luck') or M.stage==8 and M.knowledge>1 and find('choice','knowledge') or find('choice','health') end
  if M.stage==4 then return find('initial',nil,nil,0) end
  if M.stage==6 then
    -- Spend useful one-shot resources before risking health; dice rewards can fill a challenge.
    for _,a in ipairs(offers) do if a.command.kind=='reward' then
      local o=OBJECTS[a.command.value];local use=o.reward:sub(1,3)=='die' and M.challenged==0 or o.reward=='health' and M.health<M.maxHealth or o.reward=='knowledge' and M.knowledge<6 or o.reward=='luck' and M.luck<4 or o.reward=='resolve' and M.resolve<4 or o.reward=='ignore' or o.reward=='shield'
      if use then return a.command end
    end end
    local ids=pool(true,false);local selected=pool(true,true)
    if obj(14) and M.machine==0 and #ids>0 then
      if #selected~=1 then return find('clear') or find('select',nil,ids[1]) end
      return find('machine')
    end
    if M.challenged==0 then
      -- Match exact dice first, then assign nearest remaining die to each requirement.
      local best=nil
      local all=pool(false,false)
      for index,ch in ipairs(challenges()) do if not bit(doneMask(),index) and not (obj(5) and ch[1]=='run' and M.resolve==0) then
        for _,p in ipairs(patterns(ch,cap(23))) do
          if #p.values+p.any+(obj(11) and M.luck<=1 and 1 or 0)<=#all then
            local used,changes,cost={},{},0;local remaining={}
            for _,v in ipairs(p.values) do
              local found=nil
              for _,id in ipairs(all) do if not used[id] and (v>0 and value(id)==v or v<0 and value(id)>=-v) then found=id;break end end
              if found then used[found]=true else remaining[#remaining+1]=v end
            end
            for _,v in ipairs(remaining) do
              local near,delta=nil,nil
              for _,id in ipairs(ids) do if not used[id] then local d=(v>0 and v or -v)-value(id);if not delta or math.abs(d)<math.abs(delta) then near,delta=id,d end end end
              if near then used[near]=true;cost=cost+math.abs(delta);changes[#changes+1]={id=near,delta=delta} else cost=999 end
            end
            if not best or cost<best.cost then best={cost=cost,changes=changes} end
          end
        end
      end end
      if best and best.cost==0 and #pool(false,true)>0 then return find('clear') end
      if best and #best.changes>0 and best.cost<999 then
        local change=best.changes[1]
        if best.cost<=M.resolve then
          if #selected~=1 or selected[1]~=change.id then return find('clear') or find('select',nil,change.id) end
          return find('adjust',nil,change.id,change.delta>0 and 1 or -1)
        elseif M.free==0 or M.luck>0 then
          local want={};for _,v in ipairs(best.changes) do want[v.id]=true end
          for _,id in ipairs(ids) do if (E[id].properties.selected==1)~=(want[id]==true) then return find('select',nil,id) end end
          return find('reroll',nil,nil,M.free==0 and 0 or 1)
        end
      end
    end
    if #ids>0 then
      if #selected~=1 then return find('clear') or find('select',nil,ids[1]) end
      local store=(M.knowledge<6 and find('store','knowledge')) or (M.resolve<6 and find('store','resolve')) or (M.luck<6 and find('store','luck')) or find('store')
      if store then return store end
    end
    return find('end') or offers[1].command
  end
  return offers[1].command
end
return {actions=actions,choose=choose,decide=decide,setup=setup,arrange=arrange,enter=enter,roll=roll,modify=modify,sync=sync,afterReward=afterReward,endTurn=endTurn}




