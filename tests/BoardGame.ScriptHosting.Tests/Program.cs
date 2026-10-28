using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Runtime;
using GameRuntime=BoardGame.AppRuntime.GameRuntime;
using GamePackage=BoardGame.AppRuntime.GamePackage;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var folder=Path.GetFullPath(".scratch/application-loop/script-board-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var state=new GameState{Seats=[new("p1","one","cpu")],Phases=["play"],Phase="play"};
File.WriteAllText(Path.Combine(folder,"state.json"),Wire.Json(new{format=1,state}));
File.WriteAllText(Path.Combine(folder,"rules.lua"),"return {actions=function(v) return {{id='end',label='End',command={kind='finish',winners={'p1'}}}} end,decide=function(c) return {allowed=true,reason='ok',commands={c.command}} end}");
File.WriteAllText(Path.Combine(folder,"cpu.lua"),"return {decide=function(c) return c.actions[1].command end}");
RuntimePluginSelection P(string id,object? config=null)=>new(id,"1",1,JsonSerializer.SerializeToElement(config ?? new{},Wire.Options));
var startup=new RuntimeStartup(1,"script-board","1",[P("definition.board-state",new{path="state.json"}),P("resources"),P("kernel"),P("session.local"),P("recording"),P("lua.rules",new{file="rules.lua"}),P("lua.cpu",new{file="cpu.lua"})]);
File.WriteAllText(Path.Combine(folder,GameRuntime.Entry),Wire.Json(startup));
var board=new BoardApplication(42);
var approved=new ApplicationCatalog(board.Plugins().Concat([
    new(content=>LuaRuntimePlugins.Rules(()=>board.Definition(content!),42)),new(content=>LuaRuntimePlugins.Cpu(()=>board.Definition(content!)))]));
var content=approved.Load(folder);GamePackage.Pack(folder,folder+".bgm",approved.Content);
var packaged=approved.Load(folder+".bgm");Check(content.Fingerprint==packaged.Fingerprint,"Script package differs");
await using var host=await approved.StartAsync(packaged);var session=host.Service<IGameSession>("session");
await host.Driver.AdvanceAsync(TimeSpan.FromSeconds(1.0/30));
Check(session.Ended && session.Steps==1 && session.View("p1").Winners.SequenceEqual(new[]{"p1"}),"CPU did not finish: "+session.Fault?.Detail);
var replay=session.ExportRecording().Replay(module=>board.Validate(packaged,module));Check(replay.Project("p1").Ended,"Script record replay failed");
var restricted=new ApplicationCatalog(board.Plugins());
try{await restricted.StartAsync(content);throw new Exception("Foreign approved catalog trusted");}catch(InvalidDataException){}
var dependencies=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"BoardGame.ScriptHosting.Tests.deps.json"));
Check(dependencies.Contains("Lua.Execution/") && !dependencies.Contains("Wasmtime/") && !dependencies.Contains("\"Runtime/"),"Script host linked default bundle or WASM");
Console.WriteLine("PASS explicit Lua board assembly, worker, CPU update, package/recording and restricted catalog rejection");
