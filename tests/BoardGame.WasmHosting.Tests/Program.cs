using System.Text.Json;
using System.Text.Json.Serialization;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;
using GameRuntime=BoardGame.AppRuntime.GameRuntime;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var folder=Path.Combine(Path.GetTempPath(),"wasm-board-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var state=new GameState{Seats=[new("p1","one")],Phases=["play"],Phase="play",Entities=new(StringComparer.Ordinal){["counter"]=new(){Id="counter",Properties=new(StringComparer.Ordinal){["wasm.counter"]=0}}}};
File.WriteAllText(Path.Combine(folder,"state.json"),JsonSerializer.Serialize(new StateFile(1,state),TestJson.Default.StateFile));
File.Copy(Path.Combine(AppContext.BaseDirectory,"fixture/counter.wasm"),Path.Combine(folder,"counter.wasm"));
var extension=Wire.Read<RuntimeStartup>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixture/game.start.json"))).Plugins.Single(p=>p.Id=="wasm");
RuntimePluginSelection P(string id,string config="{}") {using var json=JsonDocument.Parse(config);return new(id,"1",1,json.RootElement.Clone());}
var action=new Command{Kind="wasm.counter.command",Entity="counter",Value=2};
var startup=new RuntimeStartup(1,"wasm-board","1",[P("definition.board-state","""{"path":"state.json"}"""),P("resources"),P("kernel"),P("session.local"),P("recording"),
    P("rules.commands","""{"actions":[{"id":"add","label":"Add","command":{"kind":"wasm.counter.command","entity":"counter","value":2}}]}"""),extension]);
File.WriteAllText(Path.Combine(folder,GameRuntime.Entry),Wire.Json(startup));
var board=new BoardApplication();var catalog=new ApplicationCatalog(board.Plugins().Append(new(content=>WasmRuntimePlugin.Create(()=>board.Definition(content!)))));
var content=catalog.Load(folder);await using var host=await catalog.StartAsync(content);var session=host.Service<IGameSession>("session");
host.Driver.Enqueue("board.command",JsonSerializer.SerializeToUtf8Bytes(new BoardUpdateRequest(0,"p1",action),BoardSessionJson.Default.BoardUpdateRequest));
await host.Driver.AdvanceAsync(TimeSpan.FromSeconds(1.0/30));
Check(session.View("p1").Entities.Single().Properties["wasm.counter"]==2,"WASM expansion not applied: "+session.Fault?.Detail);
var replay=session.ExportRecording().Replay(module=>board.Validate(content,module));Check(replay.Project("p1").Entities.Single().Properties["wasm.counter"]==2,"WASM replay differs");
File.WriteAllBytes(Path.Combine(folder,"counter.wasm"),[0,1,2]);
try{catalog.Load(folder);throw new Exception("WASM content hash mismatch accepted at load");}catch(InvalidDataException error){Check(error.Message.Contains("hash"),"Wrong rejection: "+error.Message);}
if(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
{
    var deps=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"BoardGame.WasmHosting.Tests.deps.json"));
    Check(deps.Contains("Wasmtime/") && !deps.Contains("Lua.Execution/") && !deps.Contains("\"Runtime/") && !deps.Contains("YamlDotNet/"),"WASM host linked Lua/default bundle/author parser");
}
Console.WriteLine("State hash: "+Wire.Hash(Wire.Json(session.ExportAuthorityState())));
Console.WriteLine("Recording hash: "+Wire.Hash(RecordingCodec.Write(session.ExportRecording())));
Console.WriteLine("PASS explicit WASM-only host, content hash validation, update expansion and replay without Lua");

internal sealed record StateFile(int Format,GameState State);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StateFile))]
internal partial class TestJson:JsonSerializerContext;
