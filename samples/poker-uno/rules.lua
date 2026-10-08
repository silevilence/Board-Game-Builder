local function find(view, id)
    for _, e in ipairs(view.entities) do if e.id == id then return e end end
end
local function offer(id, label, entity, target)
    return {id=id .. (entity or ''), label=label, command={kind=id, entity=entity},
        gesture=target and 'drag' or nil, target=target}
end
local function matches(card, top)
    return card.properties.suit == top.properties.suit or card.properties.rank == top.properties.rank
end
local function actions(ctx)
    local v = ctx.view
    if v.phase == 'setup' then return {offer('start', '洗牌并发出每人 7 张')} end
    if v.phase == 'opening' then return {offer('open', '翻开首张弃牌，开始对局')} end
    local hand, table = find(v, 'hand-' .. v.seat), find(v, 'table')
    local top = find(v, table.contents[1])
    local result = {}
    for i, id in ipairs(hand.contents) do
        local card = find(v, id)
        if (v.phase ~= 'drawn' or i == #hand.contents) and matches(card, top) then
            result[#result+1] = offer('play', '打出 ' .. card.face, id, 'table')
        end
    end
    if v.phase == 'drawn' then
        result[#result+1] = offer('pass', '结束回合')
    elseif #result == 0 then
        if find(v, 'deck').count > 0 then result = {offer('draw', '无牌可出，摸 1 张')}
        elseif find(v, 'spent').count > 0 then result = {offer('recycle', '回收旧弃牌并洗牌')}
        else result = {offer('blocked', '无牌可摸，跳过本回合')} end
    end
    return result
end
local function decide(ctx)
    local v, c = ctx.view, ctx.command
    local legal = false
    for _, a in ipairs(actions(ctx)) do
        if a.command.kind == c.kind and a.command.entity == c.entity then legal = true end
    end
    if not legal then return {allowed=false, reason='请使用当前席位的合法动作', commands={}} end
    local commands = {}
    local function add(c) commands[#commands+1] = c end
    local function reset() add{kind='set',entity='match',property='stalled',value=0} end
    if c.kind == 'start' then
        add{kind='shuffle',entity='deck'}
        for _, s in ipairs(v.seats) do
            for i=1,7 do add{kind='draw',entity='deck',target='hand-' .. s.id} end
        end
        add{kind='draw',entity='deck',target='table'}
        add{kind='phase',phase='opening'}
    elseif c.kind == 'open' then
        add{kind='reveal',entity=find(v,'table').contents[1],visibility='public'}
        add{kind='phase',phase='play'}
    elseif c.kind == 'play' then
        add{kind='transfer',entity=find(v,'table').contents[1],target='spent'}
        add{kind='transfer',entity=c.entity,target='table'}
        add{kind='reveal',entity=c.entity,visibility='public'}
        reset()
        if find(v,'hand-' .. v.seat).count == 1 then add{kind='finish',winners={v.seat}}
        else add{kind='next',phase='play'} end
    elseif c.kind == 'draw' then
        add{kind='draw',entity='deck',target='hand-' .. v.seat}
        reset()
        add{kind='phase',phase='drawn'}
    elseif c.kind == 'recycle' then
        for i=1,find(v,'spent').count do add{kind='draw',entity='spent',target='deck',visibility='hidden'} end
        add{kind='shuffle',entity='deck'}
    elseif c.kind == 'pass' then
        add{kind='next',phase='play'}
    elseif c.kind == 'blocked' then
        local stalled = find(v,'match').properties.stalled + 1
        add{kind='set',entity='match',property='stalled',value=stalled}
        if stalled >= #v.seats then add{kind='finish',winners={}}
        else add{kind='next',phase='play'} end
    end
    return {allowed=true,reason='',commands=commands}
end
return {actions=actions,decide=decide}
