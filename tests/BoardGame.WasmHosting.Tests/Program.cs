using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;
using GameRuntime=BoardGame.AppRuntime.GameRuntime;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var folder=Path.GetFullPath(".scratch/application-loop/wasm-board-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var state=new GameState{Seats=[new("p1","one")],Phases=["play"],Phase="play",Entities=new(StringComparer.Ordinal){["counter"]=new(){Id="counter",Properties=new(StringComparer.Ordinal){["wasm.counter"]=0}}}};
File.WriteAllText(Path.Combine(folder,"state.json"),Wire.Json(new{format=1,state}));
File.Copy("samples/wasm-counter/counter.wasm",Path.Combine(folder,"counter.wasm"));
var extension=Wire.Read<RuntimeStartup>(File.ReadAllText("samples/wasm-counter/game.start.json")).Plugins.Single(p=>p.Id=="wasm");
RuntimePluginSelection P(string id,object? config=null)=>new(id,"1",1,JsonSerializer.SerializeToElement(config ?? new{},Wire.Options));
var action=new Command{Kind="wasm.counter.command",Entity="counter",Value=2};
var startup=new RuntimeStartup(1,"wasm-board","1",[P("definition.board-state",new{path="state.json"}),P("resources"),P("kernel"),P("session.local"),P("recording"),
    P("rules.commands",new{actions=new[]{new ActionOffer("add","Add",action)}}),extension]);
File.WriteAllText(Path.Combine(folder,GameRuntime.Entry),Wire.Json(startup));
var board=new BoardApplication();var catalog=new ApplicationCatalog(board.Plugins().Append(new(content=>WasmRuntimePlugin.Create(()=>board.Definition(content!)))));
var content=catalog.Load(folder);await using var host=await catalog.StartAsync(content);var session=host.Service<IGameSession>("session");
host.Driver.Enqueue("board.command",JsonSerializer.SerializeToUtf8Bytes(new BoardUpdateRequest(0,"p1",action),Wire.Options));
await host.Driver.AdvanceAsync(TimeSpan.FromSeconds(1.0/30));
Check(session.View("p1").Entities.Single().Properties["wasm.counter"]==2,"WASM expansion not applied: "+session.Fault?.Detail);
var replay=session.ExportRecording().Replay(module=>board.Validate(content,module));Check(replay.Project("p1").Entities.Single().Properties["wasm.counter"]==2,"WASM replay differs");
File.WriteAllBytes(Path.Combine(folder,"counter.wasm"),[0,1,2]);
try{catalog.Load(folder);throw new Exception("WASM content hash mismatch accepted at load");}catch(InvalidDataException error){Check(error.Message.Contains("hash"),"Wrong rejection: "+error.Message);}
var deps=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"BoardGame.WasmHosting.Tests.deps.json"));
Check(deps.Contains("Wasmtime/") && !deps.Contains("Lua.Execution/") && !deps.Contains("\"Runtime/"),"WASM host linked Lua/default bundle");
Console.WriteLine("PASS explicit WASM-only host, content hash validation, update expansion and replay without Lua");
