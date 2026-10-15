return {
  seat=function(x) return x.state.seats[x.state.currentSeat+1].id end,
  judge=function(x)
    local finished=x.state.turn+1>=x.state.entities.match.properties.rounds*#x.state.seats
    return {result=finished and 'finished' or 'again'}
  end,
  next=function(x)
    local commands={}
    if #x.state.entities.deck.contents>0 then
      table.insert(commands,{kind='draw',entity='deck',target='hand-'..x.state.seats[x.state.currentSeat+1].id})
    end
    table.insert(commands,{kind='next',phase='play'})
    return {commands=commands}
  end
}
