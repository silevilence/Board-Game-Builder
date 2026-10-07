return {decide=function(c)
  local bids={}
  for _,offer in ipairs(c.actions) do if offer.command.kind=='bid' then bids[#bids+1]=offer end end
  if #bids>0 then return bids[random_index(#bids)].command end
  return c.actions[1].command
end}
