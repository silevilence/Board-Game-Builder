-- All FreeCell policy lives here. Kernel effects are ordinary atomic transfers and counters.
local function index(view)
    local by = {}
    for _, e in ipairs(view.entities) do by[e.id] = e end
    local cells, columns = 0, 0
    for i=1,4 do if #by['cell-'..i].contents == 0 then cells = cells+1 end end
    for i=1,8 do if #by['column-'..i].contents == 0 then columns = columns+1 end end
    by.capacity = (cells+1)*2^columns
    return by
end
local function reject(reason) return {allowed=false, reason=reason, commands={}} end
local function set(key, value) return {kind='set', entity='match', property=key, value=value} end
local function card_id(n) return string.format('card-%02d', n) end
local function zone_id(n)
    if n <= 8 then return 'column-'..n end
    if n <= 12 then return 'cell-'..(n-8) end
    return 'home-'..(n-12)
end
local function sequence(by, card)
    if not card or not card.properties.rank or not card.container then return nil end
    local source = by[card.container]
    if not source or source.properties.zone == 0 or source.properties.zone == 3 then return nil end
    local first
    for i, id in ipairs(source.contents) do if id == card.id then first = i end end
    if not first then return nil end
    for i=first+1,#source.contents do
        local below, above = by[source.contents[i-1]], by[source.contents[i]]
        if below.properties.rank ~= above.properties.rank+1 or below.properties.color == above.properties.color then return nil end
    end
    return source, first, #source.contents-first+1
end
local function legal(by, card, target, source, first, count)
    if not source then source, first, count = sequence(by, card) end
    if not source or not target or target.id == source.id then return nil end
    local kind = target.properties.zone
    if kind == 2 then
        if count ~= 1 or #target.contents ~= 0 then return nil end
    elseif kind == 3 then
        if count ~= 1 or card.properties.suit ~= target.properties.number-12 or card.properties.rank ~= #target.contents+1 then return nil end
    elseif kind == 1 then
        local top = by[target.contents[#target.contents]]
        if top and (top.properties.rank ~= card.properties.rank+1 or top.properties.color == card.properties.color) then return nil end
        if count > by.capacity/(#target.contents == 0 and 2 or 1) then return nil end
    else return nil end
    return source, first, count
end
local function moves(by)
    local result = {}
    -- Foundation moves first provide a useful (not necessarily safe) manual hint.
    local targets = {13,14,15,16,1,2,3,4,5,6,7,8,9,10,11,12}
    for n=1,52 do
        local card = by[card_id(n)]
        local source, first, length = sequence(by, card)
        if source then
            for _, t in ipairs(targets) do
                local target = by[zone_id(t)]
                local _, _, count = legal(by, card, target, source, first, length)
                if count then
                    table.insert(result, {id=card.id..'-'..target.id,
                        label=card.face..(count>1 and ' 等 '..count..' 张' or '')..' → '..target.face,
                        command={kind='place', entity=card.id, target=target.id}, gesture='drag', target=target.id})
                end
            end
        end
    end
    return result
end
local function actions(ctx)
    if ctx.view.phase == 'setup' then return {{id='start', label='洗牌（Enter）', command={kind='start'}, shortcuts={'confirm'}}} end
    if ctx.view.phase == 'deal' then return {{id='deal', label='发出 52 张明牌（Enter）', command={kind='deal'}, shortcuts={'confirm'}}} end
    local by = index(ctx.view)
    local result = moves(by)
    local homes, cells = {}, {}
    for _, action in ipairs(result) do
        local card, target = by[action.command.entity], by[action.target]
        if target.properties.zone == 3 then
            action.shortcuts={'home'}; homes[card.id]=action
        elseif target.properties.zone == 2 and by[card.container].properties.zone == 1 and not cells[card.id] then
            action.shortcuts={'cell'}; cells[card.id]=action
        end
    end
    for n=1,52 do
        local id=card_id(n)
        local preferred=homes[id] or cells[id]
        if preferred then table.insert(preferred.shortcuts, 'double-click') end
    end
    if by.match.properties.lastCount > 0 then table.insert(result, {id='undo', label='撤销上一步（Ctrl+Z / F10）', command={kind='undo'}, shortcuts={'undo'}}) end
    table.insert(result, {id='resign', label='认输并结束本局', command={kind='resign'}})
    return result
end
local function decide(ctx)
    local view, command = ctx.view, ctx.command
    local by = index(view)
    if view.phase == 'setup' then
        if command.kind ~= 'start' then return reject('请先洗牌') end
        return {allowed=true, reason='', commands={{kind='shuffle', entity='deck'}, {kind='phase', phase='deal'}}}
    end
    if view.phase == 'deal' then
        if command.kind ~= 'deal' then return reject('请先发牌') end
        local commands = {}
        for i, id in ipairs(by.deck.contents) do
            table.insert(commands, {kind='transfer', entity=id, target='column-'..((i-1)%8+1)})
        end
        table.insert(commands, {kind='phase', phase='play'})
        return {allowed=true, reason='', commands=commands}
    end
    if command.kind == 'resign' then return {allowed=true, reason='', commands={{kind='finish', winners={}}}} end
    local match = by.match.properties
    if command.kind == 'undo' then
        if match.lastCount == 0 then return reject('没有可撤销的移动（仅保留上一步）') end
        local card = by[card_id(match.lastCard)]
        local current = by[card.container]
        local commands = {}
        for i=#current.contents-match.lastCount+1,#current.contents do
            table.insert(commands, {kind='transfer', entity=current.contents[i], target=zone_id(match.lastSource)})
        end
        if current.properties.zone == 3 then table.insert(commands, set('home', match.home-1)) end
        table.insert(commands, set('lastCount', 0))
        table.insert(commands, set('moves', match.moves-1))
        table.insert(commands, {kind='next', phase='play'})
        return {allowed=true, reason='', commands=commands}
    end
    if command.kind ~= 'place' then return reject('请选择合法的搬牌动作') end
    local card, target = by[command.entity], by[command.target]
    local source, first, count = legal(by, card, target)
    if not source then return reject('必须异色递减；空当只能放一张；连牌不得超过可用空当和空列容量') end
    local commands = {}
    for i=first,#source.contents do table.insert(commands, {kind='transfer', entity=source.contents[i], target=target.id}) end
    table.insert(commands, set('lastCard', card.properties.number))
    table.insert(commands, set('lastSource', source.properties.number))
    table.insert(commands, set('lastCount', count))
    table.insert(commands, set('moves', match.moves+1))
    if target.properties.zone == 3 then table.insert(commands, set('home', match.home+1)) end
    table.insert(commands, {kind='next', phase='play'})
    if target.properties.zone == 3 and match.home+1 == 52 then table.insert(commands, {kind='finish', winners={'p1'}}) end
    return {allowed=true, reason='', commands=commands}
end
return {actions=actions, decide=decide}
