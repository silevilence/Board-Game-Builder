return {
  actions=function(c) return {{id='add',label='使用 WASM 计数器 +1',command={kind='add'}}} end,
  decide=function(c)
    if c.command.kind~='add' then return {allowed=false,reason='Unknown action',commands={}} end
    local value=c.view.entities[1].properties['wasm.counter']
    local effects={{kind='wasm.counter.command',entity='counter',value=1}}
    if value>=2 then effects[#effects+1]={kind='finish',winners={c.view.seat}} end
    return {allowed=true,reason='',commands=effects}
  end
}
