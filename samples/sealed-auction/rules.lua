local participants={'p1','p2','p3'}
local function contains(t,value)
  for _,v in ipairs(t) do if v==value then return true end end
  return false
end
local function offers(v)
  local r=v.secret
  if not r then return {{id='open',label='开始密封拍卖',command={kind='open'}}} end
  if r.status=='revealed' then
    local text='揭示：'
    for _,p in ipairs(participants) do text=text..p..'='..tostring(r.revealed[p] or '弃权')..' ' end
    return {{id='settle',label=text..' · 结算（同价席位顺序优先）',command={kind='settle'}}}
  end
  if r.status=='closed' then return {{id='reveal',label='揭示所有出价',command={kind='reveal'}}} end
  if r.remaining==0 or #r.locked==#participants then
    return {{id='close',label='截止并将未锁定席位记为弃权',command={kind='close'}}}
  end
  local result={}
  if not contains(r.locked,v.seat) then
    for value=0,5 do result[#result+1]={id='bid'..value,label='出价 '..value..' 并锁定',command={kind='bid',value=value}} end
    result[#result+1]={id='abstain',label='弃权',command={kind='abstain'}}
  end
  if v.seat=='p1' then result[#result+1]={id='tick',label='推进一个收集步（剩余 '..r.remaining..'）',command={kind='tick'}} end
  return result
end
return {
  actions=function(c) return offers(c.view) end,
  decide=function(c)
    local command=c.command
    local found=false
    for _,offer in ipairs(offers(c.view)) do
      if offer.command.kind==command.kind and (command.kind~='bid' or offer.command.value==command.value) then found=true end
    end
    if not found then return {allowed=false,reason='Unavailable auction action',commands={}} end
    local effects={}
    if command.kind=='open' then
      effects={{kind='secret-open',collection={id='lot-1',participants=participants,minimum=0,maximum=5,collectionSteps=12,missing='abstain',allowReplace=false,reveal='closed'}}}
    elseif command.kind=='bid' then effects={{kind='secret-submit',value=command.value},{kind='secret-lock'}}
    elseif command.kind=='abstain' then effects={{kind='secret-abstain'}}
    elseif command.kind=='tick' then effects={{kind='secret-tick'}}
    elseif command.kind=='close' then effects={{kind='secret-close'}}
    elseif command.kind=='reveal' then effects={{kind='secret-reveal'}}
    elseif command.kind=='settle' then
      local best=-1
      local winner=nil
      local index=0
      for i,p in ipairs(participants) do
        local bid=c.view.secret.revealed[p]
        if bid and bid>best then best=bid;winner=p;index=i end
      end
      effects={{kind='secret-settle'},{kind='set',entity='lot',property='price',value=math.max(0,best)},
        {kind='set',entity='lot',property='winner',value=index},{kind='finish',winners=winner and {winner} or {}}}
    end
    return {allowed=true,reason='',commands=effects}
  end
}
