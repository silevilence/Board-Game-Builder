using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class FlowTests
{
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
