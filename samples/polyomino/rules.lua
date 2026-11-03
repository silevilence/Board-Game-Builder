-- Game-specific shapes live in instance dx/dy properties, never in the engine.
local names = {'I', 'O', 'A', 'B'}
local function find(view, id)
    for _, e in ipairs(view.entities) do if e.id == id then return e end end
end
local function cells(view, piece)
    local result = {}
    for _, e in ipairs(view.entities) do
        if e.properties.piece == piece then table.insert(result, e) end
    end
    table.sort(result, function(a, b) return a.properties.cell < b.properties.cell end)
    return result
end
local function shape(view, piece, rotation)
    local result, minx, miny = {}, 100, 100
    for _, e in ipairs(cells(view, piece)) do
        local x, y = e.properties.dx, e.properties.dy
        for _ = 1, rotation do x, y = -y, x end
        table.insert(result, {entity=e, x=x, y=y})
        minx, miny = math.min(minx, x), math.min(miny, y)
    end
    for _, c in ipairs(result) do c.x, c.y = c.x-minx, c.y-miny end
    return result
end
local function fits(view, piece, rotation, x, y)
    local board = find(view, 'puzzle').properties
    for _, c in ipairs(shape(view, piece, rotation)) do
        local px, py = x+c.x, y+c.y
        if px < 0 or py < 0 or px >= board.width or py >= board.height then return false end
        for _, other in ipairs(view.entities) do
            if other.properties.piece and other.properties.piece ~= piece and other.x == px and other.y == py then return false end
        end
    end
    return true
end
local function actions(ctx)
    local view = ctx.view
    local board = find(view, 'puzzle').properties
    local piece = board.active
    local anchor = cells(view, piece)[1]
    local p = anchor.properties
    local offers = {}
    local function add(id, label, command) table.insert(offers, {id=id, label=label, command=command}) end
    for n, name in ipairs(names) do
        if n ~= piece then add('select-'..n, '选择 '..name, {kind='select-piece', value=n}) end
    end
    local rotation = (p.rotation+1)%4
    if p.placed == 0 or fits(view, piece, rotation, p.anchorX, p.anchorY) then
        add('rotate', '旋转 '..names[piece]..' 90°', {kind='rotate-piece', entity=anchor.id})
    end
    if p.placed == 1 then add('return', '收回 '..names[piece], {kind='return-piece', entity=anchor.id}) end
    local offsets = shape(view, piece, p.rotation)
    for y = 0, board.height-1 do
        for x = 0, board.width-1 do
            if fits(view, piece, p.rotation, x, y) and not (p.placed == 1 and p.anchorX == x and p.anchorY == y) then
                -- The drag handle is the first cell, which need not be the bounding-box corner.
                add('place-'..x..'-'..y, '放置 '..names[piece]..' → ('..x..','..y..')',
                    {kind='move', entity=anchor.id, x=x+offsets[1].x, y=y+offsets[1].y})
            end
        end
    end
    add('give-up', '结束本局（未完成）', {kind='give-up'})
    return offers
end
local function decide(ctx)
    local view, command = ctx.view, ctx.command
    local legal = false
    for _, a in ipairs(actions({view=view})) do
        local c = a.command
        if c.kind == command.kind and c.entity == command.entity and (c.value or 0) == command.value
            and (c.x or 0) == command.x and (c.y or 0) == command.y
            and command.target == nil and command.property == nil and command.seat == nil
            and command.phase == nil and command.visibility == nil and command.winners == nil then legal = true end
    end
    if not legal then return {allowed=false, reason='请选择合法落点；骨牌不能越界、重叠或拆开移动', commands={}} end
    if command.kind == 'give-up' then return {allowed=true, commands={{kind='finish', winners={}}}} end
    local board = find(view, 'puzzle').properties
    local effects = {}
    local function set(id, key, value) table.insert(effects, {kind='set', entity=id, property=key, value=value}) end
    if command.kind == 'select-piece' then
        set('puzzle', 'active', command.value)
    else
        local piece = board.active
        local anchor = cells(view, piece)[1]
        local p = anchor.properties
        local rotation, x, y, placed = p.rotation, p.anchorX, p.anchorY, p.placed
        if command.kind == 'rotate-piece' then rotation = (rotation+1)%4
        elseif command.kind == 'return-piece' then x, y, placed = p.trayX, p.trayY, 0
        else
            local first = shape(view, piece, rotation)[1]
            x, y, placed = command.x-first.x, command.y-first.y, 1
        end
        for _, c in ipairs(shape(view, piece, rotation)) do
            table.insert(effects, {kind='move', entity=c.entity.id, x=x+c.x, y=y+c.y})
        end
        set(anchor.id, 'rotation', rotation)
        set(anchor.id, 'anchorX', x)
        set(anchor.id, 'anchorY', y)
        set(anchor.id, 'placed', placed)
        set('puzzle', 'moves', board.moves+1)
        local count = board.placed+placed-p.placed
        set('puzzle', 'placed', count)
        if count == #names then table.insert(effects, {kind='finish', winners={'p1'}}) end
    end
    return {allowed=true, commands=effects}
end
return {actions=actions, decide=decide}
