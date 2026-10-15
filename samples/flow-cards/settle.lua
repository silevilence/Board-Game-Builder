return {
  score=function(x)
    local best,winners=-1,{}
    for _,seat in ipairs(x.state.seats) do
      local score=x.state.entities['score-'..seat.id].properties.score
      if score>best then best,winners=score,{seat.id}
      elseif score==best then table.insert(winners,seat.id) end
    end
    return {result=#winners==1 and 'win' or 'draw',gameContext={winners=winners}}
  end,
  finish=function(x) return {winners=x.gameContext.winners} end
}
