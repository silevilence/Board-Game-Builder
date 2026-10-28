using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class BoardUpdateTests
{
    public static void Run()=>Verify().GetAwaiter().GetResult();
    private static ApplicationInput Input(long sequence,long revision,string seat,Command command)=>
        new(sequence,"board.command",JsonSerializer.SerializeToUtf8Bytes(new BoardUpdateRequest(revision,seat,command),Wire.Options));
    private static async Task Verify()
    {
        foreach(var name in new[]{"quick-cards","flow-cards","wasm-counter","sealed-auction"})
        {
            var module=GameRuntime.Load("samples/"+name);
            var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu");
            using var referenceHost=RuntimeComposition.Start(module,42,controllers,500);
            var reference=referenceHost.Service<IGameSession>("session");
            while(reference.StepCpu()) { }
            Check.That(reference.Ended && reference.Fault==null,name);
            foreach(var rate in new[]{20,120})
            {
                using var updated=new PlaySession(module,42,controllers);
                for(var frame=0;frame<100 && !updated.Ended && updated.Fault==null;frame++)
                    await updated.Driver.AdvanceAsync(TimeSpan.FromSeconds(1d/rate));
                Check.That(updated.Ended && updated.Fault==null,name+" update did not finish");
                Check.That(Wire.Json(updated.ExportAuthorityLog())==Wire.Json(reference.ExportAuthorityLog()),name+" update changed transaction log");
                Check.That(updated.AuthorityStateHash()==reference.AuthorityStateHash(),name+" update changed final state");
            }
        }
        var humanModule=GameRuntime.Load("samples/quick-cards");
        var humanControllers=humanModule.Initial.Seats.ToDictionary(s=>s.Id,_=>"human");
        using(var human=new PlaySession(humanModule,42,humanControllers))
        {
            var before=Wire.Json(human.ExportAuthorityLog());
            for(var i=0;i<5;i++) await human.Driver.AdvanceAsync(TimeSpan.FromSeconds(.01));
            Check.That(Wire.Json(human.ExportAuthorityLog())==before,"Waiting human updated state");
            var action=human.Actions().First().Command;var seat=human.CurrentSeat;
            Check.That(human.Driver.Enqueue(Input(1,0,seat,action)) && !human.Driver.Enqueue(Input(1,0,seat,action)));
            human.Driver.Enqueue(Input(2,0,seat,action));
            await human.Driver.AdvanceAsync(TimeSpan.FromSeconds(.01));
            Check.That(human.Steps==1 && human.UpdateResults is [{Accepted:true},{Reason:"stale-revision"}],"Rapid input duplicated a submission");
        }
        await AtomicBoundary(humanModule,humanControllers,false);
        await AtomicBoundary(humanModule,humanControllers,true);
        await SlowDecision(humanModule);
        await Viewports(humanModule);
        var reloadDirectory=Path.Combine(Path.GetTempPath(),"bgb-update-reload-"+Guid.NewGuid());
        try
        {
            TestModules.CopyGame("samples/quick-cards",reloadDirectory);
            var old=GameRuntime.Load(reloadDirectory);
            using var reload=new PlaySession(old,42,old.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"));
            await reload.Driver.AdvanceAsync(TimeSpan.FromSeconds(.01));
            File.AppendAllText(Path.Combine(reloadDirectory,"rules.lua"),"\n-- update boundary reload\n");
            var changed=GameRuntime.Load(reloadDirectory);reload.ReloadScripts(changed);
            while(!reload.Ended && reload.Fault==null) await reload.Driver.AdvanceAsync(TimeSpan.FromSeconds(.01));
            var recording=reload.ExportRecording();
            Check.That(reload.Ended && recording.Switches.Count==2 && recording.Switches[1].AfterCommand==1);
            Check.That(Wire.Hash(Wire.Json(recording.Replay().ExportAuthorityState()))==reload.AuthorityStateHash());
        }
        finally {if(Directory.Exists(reloadDirectory)) Directory.Delete(reloadDirectory,true);}
        var flowModule=FlowTests.Module(FlowTests.Minimal(),budget:1);
        using(var flow=new PlaySession(flowModule))
        {
            for(var sequence=1;sequence<=3 && !flow.Ended;sequence++)
            {
                Check.That(flow.Fault?.Code=="flow-budget");
                flow.Driver.Enqueue(new(sequence,"board.resume",JsonSerializer.SerializeToUtf8Bytes(new BoardUpdateRequest(flow.ExportAuthorityLog().EntryCount),Wire.Options)));
                await flow.Driver.AdvanceAsync(TimeSpan.FromSeconds(.01));
                Check.That(flow.UpdateResults.Single().Accepted);
            }
            Check.That(flow.Ended && flow.ExportAuthorityLog().EntryCount==3,"Prepared flow resume repeated or lost a node");
        }
        var branchState=CommandTests.Initial();var engine=new GameEngine(branchState,42);
        var fork=engine.Fork();
        Check.That(engine.ExportLog().EntryCount==0 && fork.ExportLog().EntryCount==0);
        fork.Submit("unknown",new(){Kind="next"});
        Check.That(engine.ExportLog().EntryCount==0 && fork.ExportLog().EntryCount==1,"Candidate history shared mutable list");
    }
    private static async Task Viewports(LoadedModule module)
    {
        var controllers=module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu");var draws=0;
        var observer=new RuntimePluginDefinition("observer","1",1,[],["projection"],scope=>
        {
            var projection=scope.Service<IBoardProjectionSource>("projection").ForSeat("p1");
            scope.Render("draw",_=>{projection.View();draws++;});
        }) {Permissions=[AuthorityPlugin.Access],Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
        var startup=module.RuntimeIdentity!.Startup with {Plugins=[..module.RuntimeIdentity.Startup.Plugins,new("observer","1",1,JsonSerializer.SerializeToElement(new{}))]};
        var catalog=RuntimeComposition.Catalog(module,42,controllers,500,startup).Concat([observer]).ToArray();
        using var host=await RuntimeHost.StartScopedAsync(startup,catalog,["session"],HostMode.Graphical);
        using var first=await RuntimeHost.StartScopedAsync(startup,catalog,[],HostMode.Graphical,RuntimeScope.Viewport,host,"first");
        using var second=await RuntimeHost.StartScopedAsync(startup,catalog,[],HostMode.Graphical,RuntimeScope.Viewport,host,"second");
        var session=host.Service<IGameSession>("session");
        while(!session.Ended && session.Fault==null)
        {
            await host.Driver!.AdvanceAsync(TimeSpan.FromSeconds(.01));
            var count=session.ExportAuthorityLog().EntryCount;
            for(var draw=0;draw<8;draw++) {first.Render(TimeSpan.FromSeconds(.01));second.Render(TimeSpan.FromSeconds(.02));}
            Check.That(count==session.ExportAuthorityLog().EntryCount);
        }
        using var referenceHost=RuntimeComposition.Start(module,42,controllers,500);
        var reference=referenceHost.Service<IGameSession>("session");while(reference.StepCpu()) { }
        Check.That(draws>0 && Wire.Json(session.ExportAuthorityLog())==Wire.Json(reference.ExportAuthorityLog()),"Viewports changed board results");
    }
    private sealed class SlowCpu(TaskCompletionSource reached,TaskCompletionSource release) : ICpuDecisionProvider
    {
        public string Entry=>"slow-test";
        public Command Decide(SeatView view,IReadOnlyList<ActionOffer> actions,uint random)
        {reached.TrySetResult();release.Task.GetAwaiter().GetResult();return actions[0].Command;}
        public Action Prepare(GameDefinition definition)=>()=>{};
    }
    private static async Task SlowDecision(LoadedModule module)
    {
        var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startup=module.RuntimeIdentity!.Startup;
        var catalog=RuntimeComposition.Catalog(module,42,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"),500,startup)
            .Select(p=>p.Id=="lua.cpu"?p with {Activate=s=>s.Provide("cpu",new SlowCpu(reached,release))}:p).ToArray();
        using var host=await RuntimeHost.StartAsync(startup,catalog,["session"]);
        var session=host.Service<IGameSession>("session");
        var pending=host.Driver!.AdvanceAsync(TimeSpan.FromSeconds(.01));
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var oldView=await Task.Run(()=>session.View("p1")).WaitAsync(TimeSpan.FromSeconds(2));
            Check.That(session.ExportAuthorityLog().EntryCount==0 && !oldView.Ended,"Slow decision exposed candidate state");
            Check.Throws(()=>session.StepCpu(),"update is being prepared");
            Check.That(host.Driver.Enqueue(Input(1,0,"p1",new(){Kind="next"})));
        }
        finally {release.TrySetResult();}
        await pending;
        Check.That(session.Steps==1);
        await host.Driver.AdvanceAsync(TimeSpan.FromSeconds(.01));
        Check.That(((IBoardUpdateState)session).UpdateResults.Single().Reason=="stale-revision" && session.Steps==1);
        await host.Driver.AdvanceAsync(TimeSpan.FromSeconds(.01));Check.That(session.Steps==2);
    }
    private static async Task AtomicBoundary(LoadedModule module,Dictionary<string,string> controllers,bool fail)
    {
        var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var guard=new RuntimePluginDefinition("guard","1",1,[],[],scope=>scope.Update("wait",async (_,cancel)=>
        {
            reached.SetResult();await release.Task.WaitAsync(cancel);
            if(fail) throw new InvalidOperationException("injected later-phase failure");
            return null;
        })) {Execution=new(Contributions:[new("wait",ApplicationPhase.Update,After:["session.local/advance"])])};
        IGameSession? session=null;var renders=0;
        var observer=new RuntimePluginDefinition("observer","1",1,[],[],scope=>scope.Render("draw",_=>
        {Check.That(session!.ExportAuthorityLog().EntryCount==0,"Render saw uncommitted candidate");renders++;}))
            {Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
        var startup=module.RuntimeIdentity!.Startup with {Plugins=[..module.RuntimeIdentity.Startup.Plugins,
            new("guard","1",1,JsonSerializer.SerializeToElement(new{})),new("observer","1",1,JsonSerializer.SerializeToElement(new{}))]};
        var catalog=RuntimeComposition.Catalog(module,42,controllers,500,startup).Concat([guard,observer]).ToArray();
        using var host=await RuntimeHost.StartScopedAsync(startup,catalog,["session"],HostMode.Graphical);
        session=host.Service<IGameSession>("session");
        using var viewport=await RuntimeHost.StartScopedAsync(startup,catalog,[],HostMode.Graphical,RuntimeScope.Viewport,host);
        var before=Wire.Json(session.ExportRecording());
        host.Driver!.Enqueue(Input(1,0,session.CurrentSeat,session.Actions().First().Command));
        using var cancellation=new CancellationTokenSource();
        var pending=host.Driver.AdvanceAsync(TimeSpan.FromSeconds(.01),cancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        for(var i=0;i<10;i++) viewport.Render(TimeSpan.FromSeconds(.01));
        if(fail) release.SetResult();else cancellation.Cancel();
        try {await pending;throw new Exception("Expected aborted update");}
        catch(OperationCanceledException) when(!fail) { }
        catch(InvalidOperationException error) when(fail && error.ToString().Contains("injected later-phase failure")) { }
        Check.That(renders==10 && before==Wire.Json(session.ExportRecording()),"Aborted tick changed published state or recording");
        viewport.Render(TimeSpan.FromSeconds(.01));
    }
}
