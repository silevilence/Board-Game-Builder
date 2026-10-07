-- CPU receives exactly its seat projection and the same legal offers as a human.
return {decide=function(ctx)
  local by={};for _,e in ipairs(ctx.view.entities) do by[e.id]=e end
  local stock=by['stock-'..ctx.view.seat].properties
  local best,score=nil,-100000
  for _,a in ipairs(ctx.actions) do
    local c=a.command;local weight=0
    if c.kind=='start' then weight=c.value==1 and 100 or 0
    elseif c.kind=='settlement' then
      weight=100;local e=by[c.entity]
      for i=1,3 do local t=by['tile-'..tostring(e.properties['t'..i])]
        if t and t.properties.number>0 then weight=weight+6-math.abs(7-t.properties.number) end
      end
    elseif c.kind=='city' then weight=120
    elseif c.kind=='play-card' then weight=by[c.entity].properties.cardType==1 and 130 or 80
    elseif c.kind=='develop' then weight=75
    elseif c.kind=='road' then
      weight=20;local p=by[c.entity].properties
      for _,n in ipairs({p.a,p.b}) do local v=by['vertex-'..n]
        if v.properties.player==0 then weight=weight+8
          for i=1,3 do local near=by['vertex-'..tostring(v.properties['n'..i])];if near and near.properties.player~=0 then weight=weight-4 end end
        end
      end
    elseif c.kind=='trade' then
      weight=stock[c.property]-stock[c.target]*4
      if c.target=='ore' and stock.grain>=2 and stock.ore<3 then weight=60 end
      if stock[c.target]>=3 then weight=-10 end
    elseif c.kind=='bid' then weight=c.value==math.min(3,math.max(0,stock.grain-1)) and 100 or 0
    elseif c.kind=='tick' or c.kind=='abstain' then weight=-20
    elseif c.kind=='robber' then
      local tile=by[c.entity].properties.tile;weight=0
      for _,e in ipairs(ctx.view.entities) do if e.properties.vertex and e.properties.player>0 then
        for i=1,3 do if e.properties['t'..i]==tile then weight=weight+(('p'..e.properties.player)==ctx.view.seat and -10 or 4) end end
      end end
    elseif c.kind=='end-turn' then weight=-5 end
    weight=weight+random_index(100)/1000
    if weight>score then score=weight;best=c end
  end
  return best
end}
