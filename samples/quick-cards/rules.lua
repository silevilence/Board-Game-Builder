local function find(view, id)
    for _, entity in ipairs(view.entities) do
        if entity.id == id then return entity end
    end
    return nil
end
local function reject(reason)
    return {allowed=false, reason=reason, commands={}}
end
local function actions(ctx)
    local view = ctx.view
    if view.phase == 'setup' then
        return {{id='start', label='洗牌并发牌', command={kind='start'}}}
    end
    local result = {}
    for _, card in ipairs(view.entities) do
        if card.container == 'hand-' .. view.seat and card.properties.value then
            table.insert(result, {id=card.id, label='打出 ' .. card.face,
                command={kind='play', entity=card.id}, gesture='drag', target='table'})
        end
    end
    return result
end
local function decide(ctx)
    local view, command = ctx.view, ctx.command
    local config = find(view, 'match').properties
    if view.phase == 'setup' then
        if command.kind ~= 'start' then return reject('请先开局') end
        local commands = {{kind='shuffle', entity='deck'}}
        for _, seat in ipairs(view.seats) do
            for i=1,config.initialHand do
                table.insert(commands, {kind='draw', entity='deck', target='hand-' .. seat.id})
            end
        end
        table.insert(commands, {kind='phase', phase='play'})
        return {allowed=true, reason='', commands=commands}
    end
    local card = find(view, command.entity)
    if command.kind ~= 'play' or not card or card.container ~= 'hand-' .. view.seat
        or not card.properties.value then return reject('只能打出自己的手牌') end
    local total = find(view, 'score-' .. view.seat).properties.score + card.properties.value
    local commands = {
        {kind='transfer', entity=card.id, target='table'},
        {kind='reveal', entity=card.id, visibility='public'},
        {kind='set', entity='score-' .. view.seat, property='score', value=total}
    }
    if view.turn + 1 >= config.rounds * #view.seats then
        local winners, best = {}, -1
        for _, seat in ipairs(view.seats) do
            local score = find(view, 'score-' .. seat.id).properties.score
            if seat.id == view.seat then score = total end
            if score > best then winners, best = {seat.id}, score
            elseif score == best then table.insert(winners, seat.id) end
        end
        table.insert(commands, {kind='finish', winners=winners})
    else
        if find(view, 'deck').count > 0 then
            table.insert(commands, {kind='draw', entity='deck', target='hand-' .. view.seat})
        end
        table.insert(commands, {kind='next', phase='play'})
    end
    return {allowed=true, reason='', commands=commands}
end
return {actions=actions, decide=decide}
