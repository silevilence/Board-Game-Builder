local function distance(x,y,e) return math.max(math.abs(x-e.x),math.abs(y-e.y),math.abs(x+y-e.x-e.y)) end
return {decide=function(ctx)
    local ownTeam=nil
    for _,s in ipairs(ctx.view.seats) do if s.id==ctx.view.seat then ownTeam=s.team end end
    local enemies={}
    for _,e in ipairs(ctx.view.entities) do
        if e.properties.hp and e.properties.hp>0 then
            for _,s in ipairs(ctx.view.seats) do if s.id==e.owner and s.team~=ownTeam then table.insert(enemies,e) end end
        end
    end
    local best=nil; local score=99999
    for _,action in ipairs(ctx.actions) do
        if action.command.kind=='attack' or action.command.kind=='resolve' then return action.command end
        if action.command.kind=='move' then
            for _,enemy in ipairs(enemies) do
                local d=distance(action.command.x,action.command.y,enemy)
                if d<score then best,score=action,d end
            end
        end
    end
    return (best or ctx.actions[#ctx.actions]).command
end}
