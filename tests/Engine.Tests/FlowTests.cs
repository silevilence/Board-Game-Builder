using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.Networking;

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
    public static void Systems()
    {
        var graph=Minimal();
        graph.Nodes[1]=graph.Nodes[1] with {Script=new("calc.lua"),Commands=[]};
        graph.Nodes.Add(new(){Id="other",Kind="end",Winners=["p2"]});
        graph.Edges[1]=graph.Edges[1] with {Condition=new("eq",new(Path:"/state/entities/counter/properties/value"),new(JsonSerializer.SerializeToElement(7))),Priority=1};
        graph.Edges.Add(new("default","set","other",Default:true));
        var scripts=new Dictionary<string,string> {{"calc.lua","return { execute = function(x) return {commands={{kind='set',entity='counter',property='value',value=7}}, gameContext={secret=x.state.entities.private.properties.value}, result='done'} end }"}};
        var module=Module(graph,scripts);using var session=new PlaySession(module);
        Check.That(session.Ended && session.ExportAuthorityState().Flow!.Node=="end");
        Check.That(session.ExportAuthorityState().Flow!.GameContext["secret"].GetInt32()==42 && !Wire.Json(session.View("p1")).Contains("secret"));
        using var again=new PlaySession(module);Check.That(again.AuthorityStateHash()==session.AuthorityStateHash());
        graph.Edges.Add(new("conflict","set","other",Condition:graph.Edges[1].Condition,Priority:1));
        using var conflict=new PlaySession(Module(graph,scripts));
        Check.That(conflict.Fault?.Detail.Contains("flow-ambiguous-branch")==true && conflict.ExportAuthorityState().Entities["counter"].Properties["value"]==0);
        graph.Edges.RemoveAt(graph.Edges.Count-1);
        foreach(var source in new[]{"return {execute=function(x) error('hidden-value-42') end}",
            "return {execute=function(x) return {commands={{kind='set',entity='counter',property='value',value=7},{kind='finish',winners={}}}} end}",
            "return {execute=function(x) return {target='end'} end}"})
        {
            scripts["calc.lua"]=source;using var failed=new PlaySession(Module(graph,scripts));
            Check.That(failed.Fault!=null && failed.ExportAuthorityState().Entities["counter"].Properties["value"]==0);
            Check.That(!Wire.Json(failed.ExportAuthorityLog()).Contains("hidden-value-42") && !Wire.Json(failed.ExportDiagnostics()).Contains("hidden-value-42"));
        }
    }
    internal static (FlowDefinition Graph,Dictionary<string,string> Scripts) PlayerGraph()
    {
        var graph=new FlowDefinition(1,[new(){Id="start",Kind="start"},new(){Id="play",Kind="player",Seat=new(Script:new("player.lua","seat")),
            Script=new("player.lua","enter"),ActionsScript=new("player.lua","actions"),DecideScript=new("player.lua","decide")},
            new(){Id="end",Kind="end",Winners=["p2"]}],[new("a","start","play"),new("b","play","end")]);
        var scripts=new Dictionary<string,string>{{"player.lua","""
            return {
              seat=function(x) return 'p2' end,
              enter=function(x) return {gameContext={hidden=42},nodeContext={entered=1},commands={{kind='set',entity='private',property='value',value=9}}} end,
              actions=function(x)
                assert(x.state==nil and x.gameContext==nil and x.nodeContext==nil)
                local value=0
                for _,e in ipairs(x.view.entities) do assert(e.id~='private');if e.id=='counter' then value=e.properties.value end end
                if value<2 then return {{id='add',label='Add',command={kind='set',entity='counter',property='value',value=value+1}}} end
                return {{id='done',label='Done',command={kind='phase',phase='play'}}}
              end,
              decide=function(x) return {commands={x.command},complete=x.command.kind=='phase'} end
            }
            """},{"cpu.lua","return {decide=function(x) assert(x.view.flow==nil);return x.actions[1].command end}"}};
        return (graph,scripts);
    }
    public static void Players()
    {
        var (graph,scripts)=PlayerGraph();var module=Module(graph,scripts);
        using var human=new PlaySession(module);
        Check.That(human.CurrentSeat=="p2" && human.ExportAuthorityState().CurrentSeat==0 && human.Fault==null);
        var action=human.Actions().Single().Command;
        Check.That(!human.SubmitHuman("p1",action).Accepted && human.SubmitHuman("p2",action).Accepted);
        Check.That(!human.SubmitHuman("p2",action).Accepted);
        Check.That(human.ExportAuthorityState().Flow!.NodeContext["entered"].GetInt32()==1);
        Check.That(human.SubmitHuman("p2",human.Actions().Single().Command).Accepted);
        Check.That(!human.Ended && human.ExportAuthorityState().Entities["private"].Properties["value"]==9);
        Check.That(human.SubmitHuman("p2",human.Actions().Single().Command).Accepted && human.Ended);
        using var cpu=new PlaySession(module,controllers:new Dictionary<string,string>{{"p2","cpu"}});
        while(cpu.StepCpu()) { }
        Check.That(cpu.Ended && cpu.Fault==null && cpu.AuthorityStateHash()==human.AuthorityStateHash());
        graph.Nodes[1]=graph.Nodes[1] with {ActionsScript=null,DecideScript=null,Actions=[]};
        using var stuck=new PlaySession(Module(graph,scripts));
        Check.That(stuck.Fault?.Code=="flow-no-actions");
    }
    public static void Protection()
    {
        var graph=Minimal();graph.Nodes[1]=graph.Nodes[1] with {Commands=[],Script=new("loop.lua")};
        graph.Edges[1]=graph.Edges[1] with {Condition=new("ge",new(Path:"/state/entities/counter/properties/value"),new(JsonSerializer.SerializeToElement(8)))};
        graph.Edges.Add(new("again","set","set",Default:true));
        var scripts=new Dictionary<string,string>{{"loop.lua","return {execute=function(x) return {commands={{kind='set',entity='counter',property='value',value=x.state.entities.counter.properties.value+1}}} end}"}};
        using var game=new PlaySession(Module(graph,scripts,2));
        Check.That(game.Fault?.Code=="flow-budget" && game.ExportAuthorityState().Entities["counter"].Properties["value"]==1);
        for(var i=0;i<8 && !game.Ended;i++) {Check.That(game.Fault?.Code=="flow-budget");game.ResumeFlow();}
        Check.That(game.Ended && game.Fault==null && game.ExportAuthorityState().Entities["counter"].Properties["value"]==8);
        Check.That(game.ExportAuthorityState().Flow!.Trace.Count==10);
        using var source=new CancellationTokenSource();source.Cancel();
        using var cancelled=new PlaySession(Module(graph,scripts),cancellation:source.Token);
        Check.That(cancelled.Fault?.Code=="flow-cancelled" && cancelled.ExportAuthorityLog().EntryCount==0);
        var (players,playerScripts)=PlayerGraph();
        using var cpu=new PlaySession(Module(players,playerScripts),controllers:new Dictionary<string,string>{{"p2","cpu"}},maxSteps:1);
        Check.That(cpu.StepCpu() && !cpu.StepCpu() && cpu.Fault?.Code=="step-limit");
        using var pending=new PlaySession(Module(graph,scripts,1));
        pending.CancelFlow();pending.ResumeFlow();
        Check.That(pending.ExportAuthorityState().Entities["counter"].Properties["value"]==1);
    }
    public static void Restoration()
    {
        var (graph,scripts)=PlayerGraph();var module=Module(graph,scripts);
        using var original=new PlaySession(module);original.SubmitHuman("p2",original.Actions().Single().Command);
        var recording=Wire.Copy(original.ExportRecording());
        using var restored=PlaySession.Restore(module,recording);
        Check.That(restored.AuthorityStateHash()==original.AuthorityStateHash() && restored.ExportAuthorityLog().EntryCount==recording.Log.EntryCount && restored.Steps==1);
        while(!restored.Ended) Check.That(restored.SubmitHuman("p2",restored.Actions().Single().Command).Accepted);
        using var ended=PlaySession.Restore(module,restored.ExportRecording());
        Check.That(ended.Ended && ended.AuthorityStateHash()==restored.AuthorityStateHash());
        Check.That(Wire.Hash(Wire.Json(restored.ExportRecording().Replay().ExportAuthorityState()))==restored.AuthorityStateHash());
        var boundaryModule=Module(Minimal(),budget:1);
        using var boundary=new PlaySession(boundaryModule);
        using var continuation=PlaySession.Restore(boundaryModule,boundary.ExportRecording());
        Check.That(continuation.Fault?.Code=="flow-restored" && continuation.ExportAuthorityState().Entities["private"].Properties["value"]==42);
        continuation.ResumeFlow();Check.That(continuation.ExportAuthorityState().Entities["private"].Properties["value"]==7);
        scripts["player.lua"]+="\n-- changed";
        Check.Throws(()=>PlaySession.Restore(Module(graph,scripts),recording),"mismatch");
        var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,_=>"human");
        using var online=new OnlineSession(module,1,controllers);
        _=online.Join(OnlineIdentity.For(module),"p1");var lease=online.Join(OnlineIdentity.For(module),"p2");
        var frame=online.Read(lease.Token);
        Check.That(frame.View.CurrentSeat=="p2" && !Wire.Json(frame).Contains("gameContext"));
        var before=online.ExportAuthorityRecording().Log.EntryCount;
        online.Disconnect(lease.Token);var reconnect=online.Resume(OnlineIdentity.For(module),lease.ResumeToken,frame.Revision);
        Check.That(online.ExportAuthorityRecording().Log.EntryCount==before);
        frame=online.Read(reconnect.Lease.Token);
        Check.That(online.Submit(reconnect.Lease.Token,new(1,frame.Revision,"p2",frame.Actions.Single().Command)).Result.Accepted);
        using var debug=new InProcessDebugSession(module,1,controllers,"p2");
        var response=debug.SendAsync(new("frame")).Result;
        Check.That(response.Frame.View.CurrentSeat=="p2" && response.Frame.Actions.Count==1);
        using var player=PlayerSession.CreateAsync(module,1,"p2").Result;
        while(!player.Frame.View.Ended) Check.That(player.SubmitAsync(player.Frame.Revision,new("action",Action:player.Frame.Actions.Single().Id)).Result.Accepted);
        Check.That(player.Frame.View.Winners.SequenceEqual(["p2"]));
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
        Check.That(FlowConditions.Constant(new("eq",new(JsonSerializer.SerializeToElement(true)),new(JsonSerializer.SerializeToElement(false))))==false);
        Check.That(FlowConditions.Evaluate(new("all",Items:[new("false"),new("eq",new(Path:"/absent"),new(JsonSerializer.SerializeToElement(1)))]),_=>throw new Exception("Must short circuit"),_=>false)==false);
        Check.Throws(()=>FlowFile.Parse("{\"format\":1,\"format\":1,\"nodes\":[],\"edges\":[]}"),"duplicate field");
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
        var shadowed=cycle with {Edges=[..cycle.Edges,new("shadowed","b","e",Default:true)]};
        Check.That(FlowValidation.Validate(shadowed).Any(d=>d.Code=="no-terminal-path"));
    }
    public static void Review()
    {
        var graph=Minimal();graph.Nodes[1]=graph.Nodes[1] with {Commands=[],Script=new("loop.lua")};
        graph.Edges[1]=graph.Edges[1] with {Condition=new("lua",Script:new("loop.lua","exit")),Priority=1};
        graph.Edges.Add(new("again","set","set",Default:true));
        var scripts=new Dictionary<string,string>{{"loop.lua","return {execute=function(x) return {commands={{kind='set',entity='counter',property='value',value=x.state.entities.counter.properties.value+1}}} end, exit=function(x) local exit=false; return exit end}"}};
        using var game=new PlaySession(Module(graph,scripts,1));
        using var cancel=new CancellationTokenSource();
        cancel.CancelAfter(10);
        var work=Task.Run(()=> {while(!cancel.IsCancellationRequested)game.ResumeFlow(cancel.Token);});
        Check.That(work.Wait(TimeSpan.FromSeconds(5)),"Cancellation left a hanging flow task");
        if(game.Fault?.Code=="flow-budget")game.ResumeFlow(cancel.Token);
        Check.That(game.Fault?.Code=="flow-cancelled");
        var before=game.ExportAuthorityState().Entities["counter"].Properties["value"];
        game.ResumeFlow();Check.That(game.ExportAuthorityState().Entities["counter"].Properties["value"]==before+1);
        Check.That(game.AuthorityStateHash()==Wire.Hash(Wire.Json(game.ExportRecording().Replay().ExportAuthorityState())));
        var invalid=Minimal();invalid.Nodes[1]=invalid.Nodes[1] with {Script=new("bad.lua")};
        using var oversized=new PlaySession(Module(invalid,new(){{"bad.lua","return {execute=function(x) return {nodeContext={a=string.rep('a',9000),b=string.rep('b',9000)}} end}"}}));
        Check.That(oversized.Fault!=null && oversized.ExportAuthorityState().Entities["private"].Properties["value"]==42);
        var failedRecording=oversized.ExportRecording();
        using var failedRestore=PlaySession.Restore(failedRecording.Versions.Values.Single(),failedRecording);
        Check.That(failedRestore.Fault?.Code=="flow-execution");Check.Throws(()=>failedRestore.ResumeFlow(),"restart");
        using var cancelling=new PlaySession(Module(graph,scripts,1));
        using var invoked=new ManualResetEventSlim();
        using var listener=new System.Diagnostics.ActivityListener {
            ShouldListenTo=source=>source.Name=="BoardGame.Lua",
            Sample=(ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> options)=>System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStarted=_=>invoked.Set()
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        var running=Task.Run(()=>cancelling.ResumeFlow());
        Check.That(invoked.Wait(TimeSpan.FromSeconds(5)),"Lua invocation did not start");
        cancelling.CancelFlow();
        Check.That(running.Wait(TimeSpan.FromSeconds(5)),"In-flight Lua cancellation hung");
        if(cancelling.Fault?.Code=="flow-budget")cancelling.ResumeFlow(new CancellationToken(true));
        Check.That(cancelling.Fault?.Code=="flow-cancelled");
        // Player-query randomness is a function of the visible input, never authority random state.
        var (players,playerScripts)=PlayerGraph();
        playerScripts["player.lua"]="return {seat=function(x) return 'p2' end,enter=function(x) return {} end, actions=function(x) return {{id='r',label='R',command={kind='set',entity='counter',property='value',value=random_index(10000)}}} end,decide=function(x) return {commands={x.command}} end}";
        var module=Module(players,playerScripts);
        using var first=new PlaySession(module,1);using var second=new PlaySession(module,999);
        Check.That(Wire.Json(first.Actions())==Wire.Json(second.Actions()),"Player actions depended on hidden random state");
    }
}
