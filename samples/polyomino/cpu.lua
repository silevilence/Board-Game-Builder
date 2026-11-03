-- Demonstrate an authored public solution, using exactly the player's offers.
-- This is deliberately not a general polyomino solver.
return {decide=function(ctx)
    local anchors, active = {}, nil
    for _, e in ipairs(ctx.view.entities) do
        if e.id == 'puzzle' then active = e.properties.active end
        if e.properties.cell == 0 and e.properties.piece then anchors[e.properties.piece] = e end
    end
    local function offer(id)
        for _, a in ipairs(ctx.actions) do if a.id == id then return a.command end end
        error('demonstration solution is unavailable: '..id)
    end
    -- Clear placements that disagree with the authored solution before proceeding.
    for i = 1, #anchors do
        local p = anchors[i].properties
        if p.placed == 1 and (p.anchorX ~= p.goalX or p.anchorY ~= p.goalY or p.rotation ~= p.goalR) then
            return offer(active == i and 'return' or 'select-'..i)
        end
    end
    for i = 1, #anchors do
        local p = anchors[i].properties
        if p.placed == 0 then
            if active ~= i then return offer('select-'..i) end
            if p.rotation ~= p.goalR then return offer('rotate') end
            return offer('place-'..p.goalX..'-'..p.goalY)
        end
    end
    error('no unfinished piece')
end}
