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
}
