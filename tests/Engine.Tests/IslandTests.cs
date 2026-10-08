using BoardGame.Kernel;
using BoardGame.Lua;
using BoardGame.Modules;
using BoardGame.Runtime;
using BoardGame.View;
namespace Engine.Tests;

public static class IslandTests
{
    private static LoadedModule Load()=>TestModules.Load("samples/island-settlers");
    private static Dictionary<string,string> Humans(LoadedModule m)=>m.Initial.Seats.ToDictionary(s=>s.Id,_=>"human");
    private static void Act(PlaySession s,string id)
    {
        var offer=s.Actions().Single(a=>a.Id==id);
        var result=s.SubmitHuman(s.CurrentSeat,offer.Command);
        Check.That(result.Accepted,$"{id}: {result.Reason}; {Wire.Json(s.ExportDiagnostics())}");
    }
    public static void Run()
    {
        var m=Load();
        Check.That(m.Initial.Entities.Values.Count(e=>e.Properties.ContainsKey("vertex"))==54);
        Check.That(m.Initial.Entities.Values.Count(e=>e.Properties.ContainsKey("a"))==72);
        using var s=new PlaySession(m,1,Humans(m));
        Act(s,"start-auction");
        var first=s.Actions().First();var vertex=m.Initial.Entities[first.Command.Entity!].Properties["vertex"];
        Act(s,first.Id);
        var before=s.AuthorityStateHash();
        Check.That(!s.SubmitHuman("p2",new() {Kind="road",Entity="edge-1"}).Accepted);
        Check.That(!s.SubmitHuman("p1",new() {Kind="settlement",Entity=first.Command.Entity}).Accepted);
        Check.That(s.AuthorityStateHash()==before,"Illegal actions mutated the island");
        Act(s,s.Actions().First().Id);
        Check.That(s.Actions().All(a=>a.Command.Entity!=first.Command.Entity));
        var adjacent=m.Initial.Entities.Values.Where(e=>e.Properties.GetValueOrDefault("vertex")==vertex).Single().Properties.Where(p=>p.Key.StartsWith('n')).Select(p=>"vertex-"+p.Value).ToArray();
        Check.That(s.Actions().All(a=>!adjacent.Contains(a.Command.Entity)),"Distance rule missing");
        while(s.View(s.CurrentSeat).Phase is "settle" or "road") Act(s,s.Actions().First().Id);
        Check.That(s.View("p1").Entities.Where(e=>e.Id.StartsWith("stock-")).All(e=>e.Properties["score"]==2));
        // Roll, production, then deliberately end each turn to reach a secret round.
        while(s.View(s.CurrentSeat).Phase!="auction")
        {
            Check.That(s.Actions().Count>0,Wire.Json(s.Fault)+Wire.Json(s.ExportDiagnostics().TakeLast(3)));
            var a=s.Actions().FirstOrDefault(a=>a.Id=="end") ?? s.Actions()[0];Act(s,a.Id);
        }
        Act(s,"open-auction");Act(s,"bid-1");
        Check.That(s.View("p2").Secret?.Revealed==null && s.View("p2").Secret?.OwnValue==null,"Bid leaked");
        while(s.View(s.CurrentSeat).Secret!.Locked.Count<4) Act(s,"abstain");
        Act(s,"close");Act(s,"reveal");
        var grain=s.View("p1").Entities.Single(e=>e.Id=="stock-p1").Properties["grain"];
        Act(s,"award");
        Check.That(s.View("p1").Entities.Single(e=>e.Id=="stock-p1").Properties["grain"]==grain-1);
        before=s.AuthorityStateHash();Check.That(!s.SubmitHuman("p1",new() {Kind="award"}).Accepted && s.AuthorityStateHash()==before);
        Check.That(Wire.Json(s.ExportRecording().Replay().Project("p1"))==Wire.Json(s.View("p1")));
        RulesAndCards(m);
        ProductionAndCap(m);
        using var cpu=new PlaySession(m,42,m.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
        while(!cpu.Ended && cpu.Fault==null) cpu.StepCpu();
        Check.That(cpu.Fault==null && cpu.Ended,Wire.Json(cpu.Fault)+" "+Wire.Json(cpu.ExportDiagnostics().TakeLast(4)));
        Check.That(cpu.Steps<500);
        Check.That(Wire.Json(cpu.ExportRecording().Replay().Project("p1"))==Wire.Json(cpu.View("p1")));
        var scene=TableScene.From(cpu.View("p1"),m.ResourceData);
        Check.That(scene.Items.Count(i=>i.Kind=="line")==72 && scene.Items.Count(i=>i.Kind=="panel")==6);
        Check.That(scene.Items.All(i=>!i.Label.Contains("{score}")));
        Console.WriteLine($"Island seed 42: {cpu.Steps} commands, winners {string.Join(',',cpu.View("p1").Winners)}");
    }
    private static void RulesAndCards(LoadedModule original)
    {
        var m=Wire.Copy(original);m.Initial.Phase="build";
        var stock=m.Initial.Entities["stock-p1"];stock.Properties["wood"]=4;
        m=m with {Fingerprint=ModuleSnapshot.Fingerprint(m)};
        using var s=new PlaySession(m,9,Humans(m));
        Act(s,"trade-1-5");
        Check.That(s.View("p1").Entities.Single(e=>e.Id=="stock-p1").Properties["wood"]==1);
        Check.That(s.View("p1").Entities.Single(e=>e.Id=="trade-counter").Properties["wasm.counter"]==1);
        Act(s,"develop");
        Check.That(s.Actions().All(a=>a.Id=="end"),"Two-action budget bypassed");
        var own=s.View("p1").Entities.Single(e=>e.Container=="hand-p1");
        Check.That(!s.View("p2").Entities.Any(e=>e.Id==own.Id),"Development identity leaked");
        Check.That(!TableScene.From(s.View("p2"),m.ResourceData).Resources.ContainsKey(own.Resource!));
        Check.That(s.View("p1").Entities.Single(e=>e.Id=="stock-p1").Properties["ore"]==2);
        var before=s.AuthorityStateHash();Check.That(!s.SubmitHuman("p1",new() {Kind="play-card",Entity=own.Id}).Accepted && before==s.AuthorityStateHash());
        Check.That(Wire.Json(s.ExportRecording().Replay().Project("p2"))==Wire.Json(s.View("p2")));
    }
    private static void ProductionAndCap(LoadedModule original)
    {
        foreach(var blocked in new[] {false,true})
        {
            var m=Wire.Copy(original);m.Initial.Phase="produce";
            m.Initial.Entities["die-1"].Properties["value"]=2;m.Initial.Entities["die-2"].Properties["value"]=3;
            var vertex=m.Initial.Entities["vertex-1"];vertex.Properties["player"]=1;vertex.Properties["level"]=2;
            if(blocked) m.Initial.Entities["match"].Properties["robber"]=1;
            m=m with {Fingerprint=ModuleSnapshot.Fingerprint(m)};
            using var s=new PlaySession(m,1,Humans(m));Act(s,"produce");
            Check.That(s.View("p1").Entities.Single(e=>e.Id=="stock-p1").Properties["wood"]==(blocked?2:4),"City production/robber block wrong");
            var before=s.AuthorityStateHash();Check.That(!s.SubmitHuman("p1",new() {Kind="city",Entity="vertex-2"}).Accepted && s.AuthorityStateHash()==before);
        }
        var cap=Wire.Copy(original);cap.Initial.Entities["match"].Properties["maxRounds"]=1;
        cap=cap with {Fingerprint=ModuleSnapshot.Fingerprint(cap)};
        using var passive=new PlaySession(cap,2,Humans(cap));Act(passive,"start-classic");
        for(var i=0;i<100 && !passive.Ended;i++)
        {
            Check.That(passive.Fault==null && passive.Actions().Count>0);
            Act(passive,passive.Actions().FirstOrDefault(a=>a.Id=="end")?.Id ?? passive.Actions()[0].Id);
        }
        Check.That(passive.Ended && passive.View("p1").Secret==null && passive.View("p1").Winners.Count==4,"Disabled auction / round-cap draw failed");
    }
    public static void Modes()=>ModesAsync().GetAwaiter().GetResult();
    private static async Task ModesAsync()
    {
        var m=GameRuntime.Load("samples/island-settlers");Directory.CreateDirectory("artifacts/island-validation");
        var package="artifacts/island-validation/island-"+Guid.NewGuid()+".bgm";TestModules.Pack("samples/island-settlers",package);
        var packed=GameRuntime.Load(package);Check.That(m.Fingerprint==packed.Fingerprint);
        var cpus=m.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu");
        using IDebugSession local=new InProcessDebugSession(m,42,cpus,"p1");
        using IDebugSession remote=await ExternalDebugSession.Launch("src/Player/bin/Release/net10.0-windows/Player.exe",packed,42,cpus,"p1",true);
        var frame=(await local.SendAsync(new("frame"))).Frame;
        for(var n=0;n<500 && !frame.View.Ended;n++)
        {
            var request=new DebugRequest("step",frame.Revision);
            var a=await local.SendAsync(request);var b=await remote.SendAsync(request);
            Check.That(a.Frame.Fault==null && Wire.Json(a.Frame)==Wire.Json(b.Frame),"Directory/package and in/external process diverged");frame=a.Frame;
        }
        Check.That(frame.View.Ended);
        var recording=(await remote.SendAsync(new("recording"))).Recording!;
        var localRecording=(await local.SendAsync(new("recording"))).Recording!;
        // Package extraction changes diagnostic source paths, while the command log is identical.
        Check.That(Wire.Json(recording.Log)==Wire.Json(localRecording.Log));
        Check.That(Wire.Hash(Wire.Json(localRecording.Replay().ExportAuthorityState()))==frame.StateHash);
        Check.That(Wire.Hash(Wire.Json(recording.Replay().ExportAuthorityState()))==frame.StateHash);
        File.WriteAllText("artifacts/island-validation/modes.json",Wire.Json(new {success=true,frame.StateHash,steps=recording.Log.EntryCount,m.Fingerprint}));
        foreach(var mode in Enum.GetValues<LocalTableMode>())
        {
            using var table=new LocalTableSession(m,42,m.Initial.Seats.ToDictionary(s=>s.Id,s=>s.Id is "p1" or "p2"?"human":"cpu"),mode);
            for(var n=0;n<650 && !table.Frame.Ended;n++)
            {
                var f=table.Frame;Check.That(f.Fault==null,Wire.Json(f.Fault));
                if(f.Cpu) {Check.That(await table.StepCpuAsync());continue;}
                if(f.WaitingFor is {} seat)
                {
                    Check.That(f.Views.Count==0,"Previous private view survived handoff");
                    Check.That(await table.TakeSeatAsync(f.Revision,seat));continue;
                }
                var current=f.Views.Single(s=>s.View.Seat==s.View.CurrentSeat);
                foreach(var view in f.Views) Check.That(view.View.Entities.All(e=>!e.Id.StartsWith("dev-") || e.FaceUp || e.Owner==view.View.Seat));
                var command=Wire.Read<Command>(LuaEvaluator.Invoke(m.Cpu,m.CpuPath,"decide",Wire.Json(new {view=current.View,actions=current.Actions}),42));
                var offer=current.Actions.Single(a=>Wire.Json(a.Command)==Wire.Json(command));
                var intent=new InteractionIntent("action",Action:offer.Id);
                Check.That((await table.SubmitAsync(f.Revision,current.View.Seat,intent)).Accepted);
                Check.That(!(await table.SubmitAsync(f.Revision,current.View.Seat,intent)).Accepted);
            }
            Check.That(table.Frame.Ended && table.Frame.Winners?.Count>0,$"{mode} mixed game did not expose public results");
        }
        Console.WriteLine("Island: directory/package, both Play modes, replay and mixed hotseat/split sessions passed.");
    }
}
