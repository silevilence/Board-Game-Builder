-- A bounded smoke-test driver, not a FreeCell solver. Never report resignation as a solved deal.
return {decide=function(ctx)
    for _, a in ipairs(ctx.actions) do
        if a.command.kind == 'start' or a.command.kind == 'deal' then return a.command end
    end
    local by = {}
    for _, e in ipairs(ctx.view.entities) do by[e.id] = e end
    for _, a in ipairs(ctx.actions) do
        if a.command.kind == 'place' and by[a.command.target].properties.zone == 3 then return a.command end
    end
    for _, a in ipairs(ctx.actions) do if a.id == 'resign' then return a.command end end
end}
