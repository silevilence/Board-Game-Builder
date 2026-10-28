using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var folder=Path.GetFullPath(".scratch/application-loop/generic-host-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
RuntimePluginSelection P(string id,object? config=null)=>new(id,"1",1,JsonSerializer.SerializeToElement(config ?? new{}));
File.WriteAllText(Path.Combine(folder,"data.json"),"{\"amount\":2}");
File.WriteAllText(Path.Combine(folder,GameRuntime.Entry),JsonSerializer.Serialize(new RuntimeStartup(1,"host-probe","1",[P("data",new{path="data.json"}),P("accumulator")]),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
var plugin=new ApprovedApplicationPlugin(content=>new RuntimePluginDefinition("accumulator","1",1,["value"],[],scope=>
{
    var state=new Value();scope.Provide("value",state);
    var amount=content!.Payload.Data.GetProperty("amount").GetInt32();
    scope.Update("advance",(frame,cancellation)=>ValueTask.FromResult<Action?>(()=>state.Count+=amount));
}){Execution=new(Contributions:[new("advance",ApplicationPhase.Update)])});
var data=new ApprovedApplicationPlugin(_=>new RuntimePluginDefinition("data","1",1,["definition"],[],scope=>scope.Provide("definition",new Loader(scope.Configuration.GetProperty("path").GetString()!)))
    {Content=new(["path"],RequiredCapabilities:["value"],Prepare:true,Definition:c=>new(c.GetProperty("path").GetString()!,new Dictionary<string,string>()))});
var unused=new ApprovedApplicationPlugin(content=>content==null?new RuntimePluginDefinition("unused","1",1,[],[],_=>throw new Exception("Unused activation")):throw new Exception("Unused factory was bound"));
var approved=new ApplicationCatalog([data,plugin,unused]);
var content=approved.Load(folder);GamePackage.Pack(folder,folder+".bgm",approved.Content);
await using var host=await approved.StartAsync(approved.Load(folder+".bgm"));
host.Driver.Paused=true;
await host.Driver.StepAsync();await host.Driver.StepAsync();
Check(host.Service<Value>("value").Count==4,"Independent update did not run");
var dependencies=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Application.Hosting.Tests.deps.json"));
foreach(var omitted in new[]{"Kernel.Abstractions/","BoardGame.Core/","BoardGame.Session/","View.Abstractions/","Lua/","Wasmtime/"})
    Check(!dependencies.Contains(omitted),"Non-board host linked "+omitted);
Console.WriteLine("PASS formal content/package and instance composition without board, scripting or graphics assemblies");

sealed class Value {public int Count;}
sealed class Loader(string path) : IContentLoader
{
    public string PayloadFormat=>"probe.counter";
    public int PayloadVersion=>1;
    public JsonElement Load(IContentSource source){using var document=JsonDocument.Parse(source.Read(path));return document.RootElement.Clone();}
}
