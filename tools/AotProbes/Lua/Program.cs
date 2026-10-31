using BoardGame.Lua;
using MoonSharp.Interpreter;

// This probe calls the production executor and bundled real sample scripts;
// real sample scripts; it adds no trimming roots or alternate implementation.
var worker=args.Length==1?Path.GetFullPath(args[0]):null;
if(worker!=null && !File.Exists(worker)) throw new FileNotFoundException("Explicit worker required; no dotnet fallback",worker);
var failures=0;
if(worker==null)
{
    try
    {
        var script=new Script(CoreModules.Preset_HardSandbox);
        Console.WriteLine($"CORE ipairs={script.Globals.Get("ipairs").Type}; table.insert={script.Globals.Get("table").Table.Get("insert").Type}; math.ceil={script.Globals.Get("math").Table.Get("ceil").Type}; platform={script.Globals.Get("_MOONSHARP").Table.Get("platform")}; is_aot={script.Globals.Get("_MOONSHARP").Table.Get("is_aot")}");
    }
    catch(Exception error) {Fail("core-inspection",error);}
}
var cases=new (string Name,string Path,string Entry,string Input,LuaCallShape Shape)[]
{
    ("rules-actions","samples/quick-cards/rules.lua","actions","""{"view":{"phase":"setup"}}""",new(true,["commands","winners"])),
    ("rules-decide","samples/quick-cards/rules.lua","decide","""{"view":{"phase":"setup","entities":[{"id":"match","properties":{"initialHand":1}}],"seats":[{"id":"p1"},{"id":"p2"}]},"command":{"kind":"start"}}""",new(false,["commands","winners"])),
    ("cpu-seeded","samples/quick-cards/cpu.lua","decide","""{"view":{"entities":[{"id":"a","properties":{"value":2}},{"id":"b","properties":{"value":2}}]},"actions":[{"command":{"kind":"play","entity":"a"}},{"command":{"kind":"play","entity":"b"}}]}""",new()),
    ("flow-seat","samples/flow-cards/turn.lua","seat","""{"state":{"seats":[{"id":"p1"}],"currentSeat":0}}""",new()),
    ("flow-judge","samples/flow-cards/turn.lua","judge","""{"state":{"turn":0,"entities":{"match":{"properties":{"rounds":2}}},"seats":[{"id":"p1"}]}}""",new()),
    ("flow-next","samples/flow-cards/turn.lua","next","""{"state":{"entities":{"deck":{"contents":["card"]}},"seats":[{"id":"p1"}],"currentSeat":0}}""",new(false,["commands"]))
};
foreach(var item in cases)
{
    try
    {
        var source=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,item.Path));
        var first=await Invoke(source,item.Path,item.Entry,item.Input,item.Shape);
        var second=await Invoke(source,item.Path,item.Entry,item.Input,item.Shape);
        if(first!=second) throw new InvalidOperationException("Fixed seed differs between fresh VMs");
        Console.WriteLine($"PASS {item.Name}: {first}");
    }
    catch(Exception error) { Fail(item.Name,error); }
}
try
{
    await Invoke("return {run=function(x) while true do end end}","instruction-budget.lua","run","{}",new());
    throw new InvalidOperationException("Unbounded script unexpectedly returned");
}
catch(InvalidDataException error) when(error.Message.Contains("instruction budget",StringComparison.OrdinalIgnoreCase))
{ Console.WriteLine("PASS instruction-budget: "+error.Message); }
catch(Exception error) { Fail("instruction-budget",error); }
if(worker!=null)
{
    using var session=new LuaProcessSession(worker,worker);
    using var cancellation=new CancellationTokenSource();
    cancellation.Cancel();
    try
    {
        await session.InvokeAsync(new("return {run=function(x) return true end}","cancel.lua","run","{}",7),cancellation.Token);
        throw new InvalidOperationException("Cancelled request unexpectedly ran");
    }
    catch(OperationCanceledException) {Console.WriteLine("PASS pre-cancelled request");}
    catch(Exception error) {Fail("pre-cancelled request",error);}
}
Console.WriteLine($"RESULT failures={failures}; mode={(worker==null?"direct":"explicit-worker")}");
return failures==0?0:1;

async Task<string> Invoke(string source,string path,string entry,string input,LuaCallShape shape)
{
    if(worker==null) return LuaExecution.Invoke(source,path,entry,input,7,shape);
    using var session=new LuaProcessSession(worker,worker);
    return await session.InvokeAsync(new(source,path,entry,input,7,shape));
}
void Fail(string name,Exception error)
{
    failures++;
    Console.WriteLine($"FAIL {name}: {error}");
}
