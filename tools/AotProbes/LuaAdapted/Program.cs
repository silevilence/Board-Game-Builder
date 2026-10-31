using BoardGame.Lua;
using MoonSharp.Interpreter;
using System.Diagnostics;

LuaSandbox.Initialize();
if(args.Length==2 && args[0]=="--server" && int.TryParse(args[1],out var parentId))
{
    using var parent=Process.GetProcessById(parentId);
    _=Task.Run(async ()=>{await parent.WaitForExitAsync();Environment.Exit(0);});
    using var input=Console.OpenStandardInput();using var output=Console.OpenStandardOutput();
    while(true)
    {
        LuaResponse response;
        try
        {
            var request=LuaJson.Read<LuaRequest>(await LuaProtocol.Read(input,LuaProtocol.RequestLimit));
            if(request.Api!=2) throw new InvalidDataException("Unsupported Lua executor protocol");
            if(request.Source.Length>262144 || request.Input.Length>2097152) throw new InvalidDataException("Lua input budget exceeded.");
            response=new(true,LuaExecution.Invoke(request.Source,request.Path,request.Entry,request.Input,request.Seed,request.Shape),"");
        }
        catch(EndOfStreamException) {return 0;}
        catch(Exception error) {response=new(false,"",error.Message.Length>65536?error.Message[..65536]:error.Message);}
        await LuaProtocol.Write(output,LuaJson.Json(response),LuaProtocol.ResponseLimit);
        if(!response.Success) return 1;
    }
}

// Experimental adapted executor; never used by production or the baseline probe.
var worker=args.Length==1?args[0]=="--worker"?Environment.ProcessPath:Path.GetFullPath(args[0]):null;
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
