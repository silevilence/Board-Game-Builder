return {
  deal=function(x)
    local commands={{kind='shuffle',entity='deck'}}
    for _,seat in ipairs(x.state.seats) do
      for i=1,x.state.entities.match.properties.initialHand do
        table.insert(commands,{kind='draw',entity='deck',target='hand-'..seat.id})
      end
    end
    table.insert(commands,{kind='phase',phase='play'})
    return {commands=commands}
  end,
  dealt=function(x)
    for _,seat in ipairs(x.state.seats) do
      if #x.state.entities['hand-'..seat.id].contents~=x.state.entities.match.properties.initialHand then return false end
    end
    return true
  end
}
