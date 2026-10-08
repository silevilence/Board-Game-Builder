return {decide=function(ctx)
    -- Prefer a legal play retaining the largest matching suit; ties follow stable action order.
    local best, score = ctx.actions[1], -1
    for _, a in ipairs(ctx.actions) do
        if a.command.kind == 'play' then
            local suit
            for _, e in ipairs(ctx.view.entities) do if e.id == a.command.entity then suit = e.properties.suit end end
            local count = 0
            for _, e in ipairs(ctx.view.entities) do
                if e.container == 'hand-' .. ctx.view.seat and e.properties.suit == suit then count = count + 1 end
            end
            if count > score then best, score = a, count end
        end
    end
    return best.command
end}
