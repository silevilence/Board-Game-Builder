using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class FlowTests
{
    internal static LoadedModule Module(FlowDefinition graph,Dictionary<string,string>? scripts=null,int budget=256)
    {
        var directory=Path.Combine(Path.GetTempPath(),"bgb-flow-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try {WriteGame(directory,graph,scripts,budget);return GameRuntime.Load(directory);}
        finally {Directory.Delete(directory,true);}
    }
    internal static void WriteGame(string directory,FlowDefinition graph,Dictionary<string,string>? scripts=null,int budget=256)
    {
        var state=new GameState {Seats=[new("p1","one"),new("p2","two")],Phases=["play"],Phase="play",
            Entities=new(StringComparer.Ordinal) { ["counter"]=new(){Id="counter",Properties=new(StringComparer.Ordinal){{"value",0}}},
                ["private"]=new(){Id="private",Visibility="hidden",Properties=new(StringComparer.Ordinal){{"value",42}}} }};
        var plugins=new List<RuntimePluginSelection>();
        void Add(string id,object config)=>plugins.Add(new(id,"1",1,JsonSerializer.SerializeToElement(config)));
        foreach(var id in new[]{"kernel","session.local","recording"}) Add(id,new {});
        Add("definition.state",new {path="position.json"});Add("flow",new {path="flow.json",maxAutomaticSteps=budget});
        if(scripts?.ContainsKey("cpu.lua")==true) Add("lua.cpu",new{file="cpu.lua"});
        File.WriteAllText(Path.Combine(directory,GameRuntime.Entry),Wire.Json(new RuntimeStartup(1,"flow-fixture","1",plugins)));
        File.WriteAllText(Path.Combine(directory,"position.json"),Wire.Json(new {format=1,state}));
        File.WriteAllText(Path.Combine(directory,"flow.json"),FlowFile.Write(graph));
        foreach(var script in scripts ?? []) File.WriteAllText(Path.Combine(directory,script.Key),script.Value);
    }
    internal static FlowDefinition Minimal()=>new(1,[new(){Id="start",Kind="start"},
        new(){Id="set",Kind="system",Commands=[new(){Kind="set",Entity="private",Property="value",Value=7}]},
        new(){Id="end",Kind="end",Winners=[]}],[new("a","start","set"),new("b","set","end")]);
    public static void Runtime()
    {
        var module=Module(Minimal());using var session=new PlaySession(module);
        Check.That(session.Ended && session.Fault==null);
        Check.That(session.ExportAuthorityState().Entities["private"].Properties["value"]==7);
        var count=session.ExportAuthorityLog().EntryCount;
        for(var i=0;i<3;i++) {_=session.View("p1");_=session.Actions();}
        Check.That(session.ExportAuthorityLog().EntryCount==count);
        Check.That(session.ExportAuthorityLog().Entries.All(e=>e.Submission.Actor=="" && e.Submission.Execution=="system"));
        Check.That(session.AuthorityStateHash()==Wire.Hash(Wire.Json(session.ExportRecording().Replay().ExportAuthorityState())));
        var bad=Minimal();bad.Nodes[1]=bad.Nodes[1] with {Commands=[new(){Kind="set",Entity="private",Property="value",Value=8},new(){Kind="set",Entity="missing"}]};
        using var failed=new PlaySession(Module(bad));
        Check.That(failed.Fault!=null && failed.ExportAuthorityState().Entities["private"].Properties["value"]==42 && failed.ExportAuthorityState().Flow!.Node=="set");
        var engine=new GameEngine(module.Initial);
        var accepted=engine.SubmitFlow(null,new(){Kind="flow-enter"},0,tx=>tx.Complete(tx.Snapshot().Flow! with {Revision=1,Node="set"}));
        Check.That(accepted.Accepted && !engine.SubmitFlow(null,new(){Kind="flow-enter"},0,_=>throw new Exception()).Accepted);
        Check.That(!engine.Submit("p1",new(){Kind="finish",Winners=[]}).Accepted);
    }
    internal static FlowDefinition Example() => new(1,
    [
        new() { Id="start", Kind="start" },
        new() { Id="deal", Kind="system", Script=new("deal.lua") },
        new() { Id="play", Kind="player", Seat=new(Script:new("seat.lua","select")),
            Actions=[new("play","Play",[new() {Kind="next"}],false), new("done","Done",[])] },
        new() { Id="judge", Kind="system", Results=["again","win","draw"], Script=new("judge.lua") },
        new() { Id="score", Kind="system" },
        new() { Id="win", Kind="end", Winners=["p1"] },
        new() { Id="draw", Kind="end", Winners=[] }
    ],
    [new("a","start","deal"),new("b","deal","play"),new("c","play","judge"),
        new("d","judge","play","again"),new("e","judge","score","win"),
        new("f","score","win"),new("g","judge","draw","draw")]);

    public static void Contract()
    {
        var graph=Example();
        Check.That(FlowFile.Write(FlowFile.Parse(FlowFile.Write(graph)))==FlowFile.Write(graph));
        Check.That(FlowFile.Scripts(graph).Count()==3);
        foreach(var json in new[] {"{}", "{\"format\":2,\"nodes\":[],\"edges\":[]}",
            "{\"format\":1,\"nodes\":[{\"id\":\"x\"}],\"edges\":[]}",
            "{\"format\":1,\"nodes\":[],\"edges\":[],\"unknown\":1}"})
        {
            var failed=false;try {FlowFile.Parse(json);} catch(InvalidDataException e) {failed=e.Message.Contains("flow");}
            Check.That(failed,"Invalid flow contract accepted");
        }
    }
    public static void Validation()
    {
        Check.That(!FlowValidation.Validate(Example()).Any(d=>d.Severity=="error"));
        var nodes=new List<FlowNode> {new(){Id="s",Kind="start"},new(){Id="a",Kind="system"},new(){Id="b",Kind="system"},new(){Id="e",Kind="end",Winners=[]}};
        var cycle=new FlowDefinition(1,nodes,[new("sa","s","a"),new("ab","a","b"),new("ba","b","a")]);
        Check.That(FlowValidation.Validate(cycle).Count(d=>d.Code=="no-terminal-path")==3);
        foreach(var condition in new FlowCondition?[] {new("false"),new("eq",new(JsonSerializer.SerializeToElement(1)),new(JsonSerializer.SerializeToElement(2))),
            new("lua",Script:new("false.lua","test")),new("all",Items:[new("true"),new("false")])})
        {
            var graph=cycle with {Edges=[..cycle.Edges,new("exit","b","e",Condition:condition)]};
            Check.That(FlowValidation.Validate(graph,new Dictionary<string,string>{{"false.lua","return false"}}).Any(d=>d.Code=="no-terminal-path"));
        }
        var valid=cycle with {Edges=[..cycle.Edges,new("exit","b","e",Condition:new("lua",Script:new("x.lua","test")))]};
        Check.That(!FlowValidation.Validate(valid,new Dictionary<string,string>{{"x.lua","return { test = function(x) return x.state.turn > 2 end }"}}).Any(d=>d.Severity=="error"));
        var feeder=cycle with {Nodes=[..nodes,new(){Id="c",Kind="system"}],Edges=[..cycle.Edges,new("bc","b","c"),new("cc","c","c")]};
        Check.That(FlowValidation.Validate(feeder).Count(d=>d.Code=="no-terminal-path")==4);
        var bad=Example() with {Edges=[..Example().Edges,new("illegal","win","start")]};
        Check.That(FlowValidation.Validate(bad).Any(d=>d.Code=="edge-direction"));
        var waiting=valid with {Nodes=nodes.Select(n=>n.Id=="a"?n with {Kind="player",Seat=new(Current:true),Actions=[new("wait","Wait",[],false)]}:n).ToList()};
        Check.That(!FlowValidation.Validate(waiting).Any(d=>d.Severity=="error"));
    }
}
