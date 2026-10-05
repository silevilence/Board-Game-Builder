local directions = {{1,0},{-1,0},{0,1},{0,-1},{1,-1},{-1,1}}
local function find(v,id)
    for _,e in ipairs(v.entities) do if e.id==id then return e end end
end
local function team(v,seat)
    for _,s in ipairs(v.seats) do if s.id==seat then return s.team end end
end
local function distance(a,b)
    return math.max(math.abs(a.x-b.x),math.abs(a.y-b.y),math.abs(a.x+a.y-b.x-b.y))
end
local function occupied(v,x,y)
    for _,e in ipairs(v.entities) do
        if e.properties.hp and e.properties.hp>0 and e.x==x and e.y==y then return true end
    end
    return false
end
local function actions(ctx)
    local v=ctx.view
    if v.phase=='resolve' then return {{id='resolve',label='结算掷骰与伤害',command={kind='resolve'}}} end
    local unit=find(v,'unit-'..v.seat)
    local offers={}
    if unit.properties.hp>0 and unit.properties.ap>0 then
        for _,enemy in ipairs(v.entities) do
            if enemy.properties.hp and enemy.properties.hp>0 and team(v,enemy.owner)~=team(v,v.seat) and distance(unit,enemy)==1 then
                table.insert(offers,{id='attack-'..enemy.id,label='攻击 '..enemy.id..'（消耗 1 AP）',command={kind='attack',entity=unit.id,target=enemy.id}})
            end
        end
        for _,d in ipairs(directions) do
            local x,y=unit.x+d[1],unit.y+d[2]
            if x>=0 and x<=4 and y>=0 and y<=3 and not occupied(v,x,y) then
                table.insert(offers,{id='move-'..x..'-'..y,label='移动至 '..x..','..y..'（1 AP）',command={kind='move',entity=unit.id,x=x,y=y}})
            end
        end
    end
    table.insert(offers,{id='end-turn',label='结束回合',command={kind='end-turn'}})
    return offers
end
local function winners(v,damaged,hp)
    local totals={}
    for _,s in ipairs(v.seats) do
        local e=find(v,'unit-'..s.id)
        totals[s.team]=(totals[s.team] or 0)+(e.id==damaged and hp or e.properties.hp)
    end
    local alive,best,bestTeam=0,-1,nil
    for name,total in pairs(totals) do
        if total>0 then alive=alive+1 end
        if total>best then best,bestTeam=total,name elseif total==best then bestTeam=nil end
    end
    if alive>1 and v.turn<29 then return nil end
    local result={}
    for _,s in ipairs(v.seats) do if s.team==bestTeam then table.insert(result,s.id) end end
    return result
end
local function decide(ctx)
    local v,c=ctx.view,ctx.command
    local legal=false
    for _,a in ipairs(actions({view=v})) do
        local o=a.command
        if c.kind==o.kind and c.entity==o.entity and c.target==o.target and (c.kind~='move' or (c.x==o.x and c.y==o.y)) then legal=true end
    end
    if not legal then return {allowed=false,reason='只能执行当前席位的合法移动、攻击或结束回合',commands={}} end
    local commands={}
    local unit=find(v,'unit-'..v.seat)
    if c.kind=='move' then
        commands={c,{kind='set',entity=unit.id,property='ap',value=unit.properties.ap-1}}
    elseif c.kind=='attack' then
        local index=0
        for i,s in ipairs(v.seats) do if 'unit-'..s.id==c.target then index=i end end
        commands={{kind='set',entity=unit.id,property='ap',value=unit.properties.ap-1},
            {kind='set',entity='match',property='target',value=index},{kind='roll',entity='die'},{kind='phase',phase='resolve'}}
    elseif c.kind=='resolve' then
        local target=find(v,'unit-'..v.seats[find(v,'match').properties.target].id)
        local damage=find(v,'die').properties.value>=4 and 2 or 1
        local health=math.max(0,target.properties.hp-damage)
        commands={{kind='set',entity=target.id,property='hp',value=health},{kind='phase',phase='act'}}
        local result=winners(v,target.id,health)
        if result then table.insert(commands,{kind='finish',winners=result}) end
    else
        local result=winners(v,nil,0)
        if result then commands={{kind='finish',winners=result}}
        else
            local nextSeat=nil
            for i,s in ipairs(v.seats) do if s.id==v.seat then nextSeat=v.seats[i % #v.seats+1].id end end
            commands={{kind='set',entity='unit-'..nextSeat,property='ap',value=2},{kind='next',phase='act'}}
        end
    end
    return {allowed=true,reason='',commands=commands}
end
return {actions=actions,decide=decide}
