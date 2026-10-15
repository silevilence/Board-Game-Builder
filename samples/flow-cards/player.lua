local function find(view,id)
  for _,e in ipairs(view.entities) do if e.id==id then return e end end
end
return {
  actions=function(x)
    local actions={}
    for _,card in ipairs(x.view.entities) do
      if card.container=='hand-'..x.view.seat and card.properties.value then
        table.insert(actions,{id=card.id,label='打出 '..card.face,command={kind='play',entity=card.id},gesture='drag',target='table'})
      end
    end
    return actions
  end,
  decide=function(x)
    local card=find(x.view,x.command.entity)
    assert(card and card.container=='hand-'..x.view.seat)
    local score=find(x.view,'score-'..x.view.seat).properties.score
    return {commands={
      {kind='transfer',entity=card.id,target='table'},
      {kind='reveal',entity=card.id,visibility='public'},
      {kind='set',entity='score-'..x.view.seat,property='score',value=score+card.properties.value}
    },complete=true}
  end
}
