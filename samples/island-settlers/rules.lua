-- All Island Settlers rules live in this sandboxed module. Only projected data is read.
local resources={'wood','brick','wool','grain','ore'}
local names={'木','砖','羊','麦','矿'}
local colors={14645338,5746640,14332759,8764035}
local costs={road={1,1,0,0,0},settlement={1,1,1,1,0},city={0,0,0,2,3},develop={0,0,1,1,1}}
local function model(v)
  local by,vs,es,ts={},{},{},{}
  for _,e in ipairs(v.entities) do
    by[e.id]=e
    if e.properties.vertex then vs[#vs+1]=e end
    if e.properties.a then es[#es+1]=e end
    if e.properties.tile then ts[#ts+1]=e end
  end
  return by,vs,es,ts
end
local function affordable(p,cost)
  for i,k in ipairs(resources) do if p[k]<cost[i] then return false end end
  return true
end
local function empty(by,e)
  if e.properties.player~=0 then return false end
  for j=1,3 do local n=e.properties['n'..j]; if n and by['vertex-'..n].properties.player~=0 then return false end end
  return true
end
local function connected(by,links,n,player)
  local p=by['vertex-'..n].properties.player
  if p==player then return true end
  if p~=0 then return false end
  return links[n]==true
end
local function offers(v)
  local by,vs,es,ts=model(v)
  local m=by.match.properties; local player=tonumber(string.sub(v.seat,2));local stock=by['stock-'..v.seat].properties
  local links={};for _,e in ipairs(es) do local p=e.properties;if p.player==player then links[p.a]=true;links[p.b]=true end end
  local result={}
  local function add(id,label,c,shortcut)
    result[#result+1]={id=id,label=label,command=c,shortcuts=shortcut and {'double-click'} or nil}
  end
  if v.phase=='welcome' then
    add('start-auction','启航 · 开启每轮秘密竞拍',{kind='start',value=1})
    add('start-classic','启航 · 关闭秘密竞拍',{kind='start',value=0})
  elseif v.phase=='settle' then
    for _,e in ipairs(vs) do if empty(by,e) then add('settle-'..e.id,'免费村庄 · 交点 '..e.properties.vertex,{kind='settlement',entity=e.id},true) end end
  elseif v.phase=='road' then
    for _,e in ipairs(es) do local p=e.properties
      if p.player==0 and (p.a==m.lastVertex or p.b==m.lastVertex) then add('road-'..e.id,'免费道路 · '..p.a..'—'..p.b,{kind='road',entity=e.id},true) end
    end
  elseif v.phase=='roll' then add('roll','掷两颗骰子 · 全岛生产',{kind='roll-dice'})
  elseif v.phase=='produce' then add('produce','结算产出 · '..(by['die-1'].properties.value+by['die-2'].properties.value)..' 点',{kind='produce'})
  elseif v.phase=='robber' then
    for _,e in ipairs(ts) do if e.properties.tile~=m.robber then add('robber-'..e.id,'移动强盗 · '..e.face,{kind='robber',entity=e.id},true) end end
  elseif v.phase=='auction' then
    local r=v.secret
    if not r or r.status=='settled' then add('open-auction','开启本轮港口声望竞拍',{kind='open-auction'})
    elseif r.status=='revealed' then
      local label='揭示：'
      for _,s in ipairs(v.seats) do label=label..s.id..' '..tostring(r.revealed[s.id] or '弃权')..' / ' end
      add('award',label..'结算',{kind='award'})
    elseif r.status=='closed' then add('reveal','揭示所有出价',{kind='reveal-bids'})
    elseif #r.locked==4 or r.remaining==0 then add('close','截止并锁定缺席者弃权',{kind='close-bids'})
    else
      local locked=false; for _,s in ipairs(r.locked) do if s==v.seat then locked=true end end
      if not locked then
        for n=0,math.min(3,stock.grain) do add('bid-'..n,'密封出价 '..n..' 麦 · 锁定',{kind='bid',value=n}) end
        add('abstain','放弃本轮竞拍',{kind='abstain'})
      end
      if v.seat=='p1' then add('tick','推进截止时钟（未提交者将弃权）',{kind='tick'}) end
    end
  elseif v.phase=='build' then
    if m.actions<2 then
      if stock.roads<15 and affordable(stock,costs.road) then
        for _,e in ipairs(es) do local p=e.properties
          if p.player==0 and (connected(by,links,p.a,player) or connected(by,links,p.b,player)) then add('road-'..e.id,'道路 · '..p.a..'—'..p.b..' [木1砖1]',{kind='road',entity=e.id},true) end
        end
      end
      for _,e in ipairs(vs) do
        if stock.buildings-stock.cities<5 and empty(by,e) and connected(by,links,e.properties.vertex,player) and affordable(stock,costs.settlement) then add('settle-'..e.id,'村庄 · '..e.properties.vertex..' [木砖羊麦各1]',{kind='settlement',entity=e.id},true) end
        if stock.cities<4 and e.properties.player==player and e.properties.level==1 and affordable(stock,costs.city) then add('city-'..e.id,'城市 · '..e.properties.vertex..' [麦2矿3]',{kind='city',entity=e.id},true) end
      end
      if affordable(stock,costs.develop) and by.deck.count>0 then add('develop','购买发展牌 [羊1麦1矿1]',{kind='develop'}) end
      for i,k in ipairs(resources) do if stock[k]>=3 then
        for j,t in ipairs(resources) do if i~=j then add('trade-'..i..'-'..j,'港口 · '..names[i]..'3 → '..names[j]..'1',{kind='trade',property=k,target=t}) end end
      end end
      if stock.played==0 then
        for _,e in ipairs(v.entities) do if e.container=='hand-'..v.seat then add('play-'..e.id,'使用 '..e.face,{kind='play-card',entity=e.id},true) end end
      end
    end
    add('end','结束回合 · '..m.actions..'/2 次建设已用',{kind='end-turn'})
  end
  return result
end
local function decide(ctx)
  local v,c=ctx.view,ctx.command
  local legal=false
  for _,a in ipairs(offers(v)) do local o=a.command
    if c.kind==o.kind and c.entity==o.entity and c.target==o.target and c.property==o.property and c.value==(o.value or 0) and c.x==0 and c.y==0 and c.seat==nil and c.phase==nil and c.visibility==nil and c.winners==nil and c.collection==nil then legal=true;break end
  end
  if not legal then return {allowed=false,reason='请选择当前席位的合法动作；建造须满足费用、距离和连通条件。',commands={}} end
  local by,vs,es,ts=model(v); local m=by.match.properties;local s=by['stock-'..v.seat].properties;local player=tonumber(string.sub(v.seat,2))
  local effects={}; local updates={}
  local function emit(c) effects[#effects+1]=c end
  local function set(id,k,n)
    updates[id]=updates[id] or {};updates[id][k]=n
  end
  local function get(id,k) return updates[id] and updates[id][k] or by[id].properties[k] end
  local function inc(id,k,n) set(id,k,get(id,k)+n) end
  local function phase(p) emit({kind='phase',phase=p}) end
  local function pay(cost) for i,k in ipairs(resources) do if cost[i]~=0 then inc('stock-'..v.seat,k,-cost[i]) end end end
  local function face(id,n) emit({kind='flip',entity=id,value=n});emit({kind='flip',entity=id,value=n}) end
  local function awardScore(seat,points) inc('stock-'..seat,'score',points) end
  if c.kind=='start' then
    set('match','auction',c.value);emit({kind='shuffle',entity='deck'});phase('settle')
  elseif c.kind=='settlement' or c.kind=='city' or c.kind=='road' then
    local e=by[c.entity];local setup=v.phase~='build'
    if not setup then pay(costs[c.kind]);inc('match','actions',1) end
    set(e.id,'player',player);set(e.id,'viewColor',colors[player])
    if c.kind=='road' then
      inc('stock-'..v.seat,'roads',1)
      if setup then
        inc('match','setup',1);emit({kind='next',phase=m.setup==7 and 'roll' or 'settle'})
      end
    else
      local level=c.kind=='city' and 2 or 1
      set(e.id,'level',level);set(e.id,'viewWidth',level==2 and 38 or 28);set(e.id,'viewHeight',level==2 and 44 or 34);set(e.id,'viewLabelY',18);set(e.id,'viewNoImage',0);face(e.id,level)
      awardScore(v.seat,1)
      inc('stock-'..v.seat,c.kind=='city' and 'cities' or 'buildings',1)
      if setup then set('match','lastVertex',e.properties.vertex);phase('road') end
    end
  elseif c.kind=='roll-dice' then
    emit({kind='roll',entity='die-1'});emit({kind='roll',entity='die-2'});phase('produce')
  elseif c.kind=='produce' then
    local total=by['die-1'].properties.value+by['die-2'].properties.value
    set('match','lastRoll',total);face('die-1',by['die-1'].properties.value-1);face('die-2',by['die-2'].properties.value-1)
    if total==7 then phase('robber')
    else
      for _,t in ipairs(ts) do if t.properties.number==total and t.properties.tile~=m.robber and t.properties.resource<=5 then
        for _,e in ipairs(vs) do if e.properties.player>0 then
          for j=1,3 do if e.properties['t'..j]==t.properties.tile then inc('stock-p'..e.properties.player,resources[t.properties.resource],e.properties.level) end end
        end end
      end end
      phase('build')
    end
  elseif c.kind=='robber' then
    local e=by[c.entity];set('match','robber',e.properties.tile)
    emit({kind='move',entity='robber',x=e.x,y=e.y+12});phase('build')
  elseif c.kind=='trade' then
    inc('stock-'..v.seat,c.property,-3);inc('stock-'..v.seat,c.target,1);inc('match','actions',1)
    emit({kind='wasm.counter.command',entity='trade-counter',value=1})
  elseif c.kind=='develop' then
    pay(costs.develop);inc('match','actions',1);emit({kind='draw',entity='deck',target='hand-'..v.seat})
  elseif c.kind=='play-card' then
    local kind=by[c.entity].properties.cardType
    if kind==1 then awardScore(v.seat,1);inc('stock-'..v.seat,'prestige',1);phase('robber')
    elseif kind==2 then for _,k in ipairs(resources) do inc('stock-'..v.seat,k,1) end
    else inc('stock-'..v.seat,'wood',2);inc('stock-'..v.seat,'brick',2) end
    inc('match','actions',1);set('stock-'..v.seat,'played',1)
    emit({kind='transfer',entity=c.entity,target='discard'});emit({kind='reveal',entity=c.entity,visibility='public'})
  elseif c.kind=='end-turn' then
    inc('match','turns',1);set('match','actions',0);set('stock-'..v.seat,'played',0)
    -- A small expedition allowance keeps the deliberately short showcase moving.
    local least=resources[1];for _,k in ipairs(resources) do if s[k]<s[least] then least=k end end
    inc('stock-'..v.seat,least,1)
    if player==4 then
      if m.round>=m.maxRounds then
        local best=-1;local winners={}
        for _,seat in ipairs(v.seats) do local score=by['stock-'..seat.id].properties.score
          if score>best then best=score;winners={seat.id} elseif score==best then winners[#winners+1]=seat.id end
        end
        -- Flush counters before finish below.
        updates._winners=winners
      else
        inc('match','round',1);emit({kind='next',phase=m.auction==1 and 'auction' or 'roll'})
      end
    else emit({kind='next',phase='roll'}) end
  elseif c.kind=='open-auction' then emit({kind='secret-open',collection={id='harbor-'..m.round,participants={'p1','p2','p3','p4'},minimum=0,maximum=3,collectionSteps=8,missing='abstain',allowReplace=false,reveal='closed'}})
  elseif c.kind=='bid' then emit({kind='secret-submit',value=c.value});emit({kind='secret-lock'})
  elseif c.kind=='abstain' then emit({kind='secret-abstain'})
  elseif c.kind=='tick' then emit({kind='secret-tick'})
  elseif c.kind=='close-bids' then emit({kind='secret-close'})
  elseif c.kind=='reveal-bids' then emit({kind='secret-reveal'})
  elseif c.kind=='award' then
    local winner=nil;local best=0
    -- Rotate tied-bid priority each round, explicitly including the first round.
    for n=0,3 do local i=(m.round-2+n)%4+1;local seat='p'..i;local bid=v.secret.revealed[seat]
      if bid and bid>best then best=bid;winner=seat end
    end
    if winner then inc('stock-'..winner,'grain',-best);inc('stock-'..winner,'prestige',1);awardScore(winner,1) end
    set('match','auctionWinner',winner and tonumber(string.sub(winner,2)) or 0);set('match','auctionPrice',best)
    emit({kind='secret-settle'});phase('roll')
  end
  local winners=updates._winners;updates._winners=nil
  -- Stable ordering makes command logs deterministic across Lua hash iteration orders.
  local ids={};for id in pairs(updates) do ids[#ids+1]=id end;table.sort(ids)
  for _,id in ipairs(ids) do local keys={};for k in pairs(updates[id]) do keys[#keys+1]=k end;table.sort(keys)
    for _,k in ipairs(keys) do emit({kind='set',entity=id,property=k,value=updates[id][k]}) end
  end
  if not winners then
    winners={}
    for _,seat in ipairs(v.seats) do if get('stock-'..seat.id,'score')>=m.targetScore then winners[#winners+1]=seat.id end end
    if #winners==0 then winners=nil end
  end
  if winners then emit({kind='finish',winners=winners}) end
  return {allowed=true,reason='',commands=effects}
end
return {actions=function(c) return offers(c.view) end,decide=decide}
