using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;
using GameRuntime=BoardGame.AppRuntime.GameRuntime;
using GamePackage=BoardGame.AppRuntime.GamePackage;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var folder=Path.GetFullPath(".scratch/application-loop/no-script-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var state=new GameState{Seats=[new("p1","one")],Phases=["play"],Phase="play"};
File.WriteAllText(Path.Combine(folder,"state.json"),Wire.Json(new{format=1,state}));
RuntimePluginSelection P(string id,object? config=null)=>new(id,"1",1,JsonSerializer.SerializeToElement(config ?? new{},Wire.Options));
var startup=new RuntimeStartup(1,"plain-board","1",[P("definition.board-state",new{path="state.json"}),P("resources"),P("kernel"),P("session.local"),P("recording"),
    P("rules.commands",new{actions=new[]{new ActionOffer("end","End",new(){Kind="finish",Winners=["p1"]})}})]);
File.WriteAllText(Path.Combine(folder,GameRuntime.Entry),Wire.Json(startup));
var board=new BoardApplication(42);var approved=new ApplicationCatalog(board.Plugins());
var content=GameRuntime.Load(folder,approved.Content);
GamePackage.Pack(folder,folder+".bgm",approved.Content);
var packaged=GameRuntime.Load(folder+".bgm",approved.Content);
Check(packaged.Fingerprint==content.Fingerprint,"Package content identity differs");
await using var host=await approved.StartAsync(packaged);
var session=host.Service<IGameSession>("session");
host.Driver.Enqueue("board.command",JsonSerializer.SerializeToUtf8Bytes(new BoardUpdateRequest(0,"p1",session.Actions()[0].Command),Wire.Options));
await host.Driver.AdvanceAsync(TimeSpan.FromSeconds(1.0/30));
Check(session.Ended && session.Steps==1,"Plain board did not finish through update");
var recording=session.ExportRecording();var replay=recording.Replay(module=>board.Validate(packaged,module));
Check(Wire.Json(replay.Project("p1"))==Wire.Json(session.View("p1")),"Selective recording replay differs");
var bad=startup with {Plugins=startup.Plugins.Where(p=>p.Id!="recording").ToList()};
File.WriteAllText(Path.Combine(folder,GameRuntime.Entry),Wire.Json(bad));
try{approved.Load(folder);throw new Exception("Missing required capability was trimmed away");}catch(InvalidDataException){}
var runtimeDeps=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"BoardGame.Hosting.Tests.deps.json"));
foreach(var omitted in new[]{"\"Runtime/","\"Lua/","\"Lua.Execution/","\"BoardGame.Lua/","\"BoardGame.Wasm/","Wasmtime/","MonoGame.Framework/"})
    Check(!runtimeDeps.Contains(omitted),"Unselected implementation linked: "+omitted);
Console.WriteLine("PASS explicit no-script assembly, formal load/pack, update, recording and absent scripting/graphics dependencies");
