return {decide=function(ctx)
    local best, choices = -1, {}
    for _, action in ipairs(ctx.actions) do
        local value = 0
        for _, entity in ipairs(ctx.view.entities) do
            if entity.id == action.command.entity then value = entity.properties.value or 0 end
        end
        if value > best then best, choices = value, {action.command}
        elseif value == best then table.insert(choices, action.command) end
    end
    return choices[random_index(#choices)]
end}
