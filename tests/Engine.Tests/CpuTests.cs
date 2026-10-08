using BoardGame.Kernel;
using BoardGame.Runtime;
namespace Engine.Tests;
public static class CpuTests
{
    public static void Run()
    {
        var s=CommandTests.Initial();s.Seats.Add(new("p3","a","cpu"));
        const string rules="""
        return {
          actions=function(c) return {{id='pass',label='Pass',command={kind='pass'}}} end,
          decide=function(c)
            if c.command.kind~='pass' then return {allowed=false,reason='invalid',commands={}} end
            if c.view.turn>=4 then return {allowed=true,reason='',commands={{kind='finish',winners={'p1','p3'}}}} end
            return {allowed=true,reason='',commands={{kind='next'}}}
          end
        }
        """;
        var m=TestModules.LuaGame(s,rules,"return {decide=function(c) return c.actions[random_index(#c.actions)].command end}");
        using var session=new PlaySession(m,7,new Dictionary<string,string> { ["p2"]="cpu" });
        Check.That(session.SubmitHuman("p1",new() { Kind="pass" }).Accepted);
        Check.That(session.StepCpu() && session.StepCpu());
        Check.That(session.CurrentSeat=="p1" && !session.IsCpu);
        Check.That(session.SubmitHuman("p1",new() { Kind="pass" }).Accepted);
        Check.That(session.StepCpu() && session.Ended);
        Check.That(session.View("p1").Winners.SequenceEqual(new[] { "p1","p3" }));
        foreach(var cpu in new[] { "return {decide=function(c) return {kind='cheat'} end}",
            "return {decide=function(c) while true do end end}","return {decide=function(c) return c.view.authority.secret end}" })
        {
            using var bad=new PlaySession(TestModules.Rebuild(m with { Cpu=cpu }),controllers:new Dictionary<string,string> { ["p1"]="cpu" });
            Check.That(!bad.StepCpu() && bad.Fault!=null && bad.Steps==0);
        }
    }
}
