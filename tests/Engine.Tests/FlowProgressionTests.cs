using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class FlowProgressionTests
{
    public static void Run()
    {
        var (graph,scripts)=FlowTests.PlayerGraph();
        var module=FlowTests.Module(graph,scripts);
        var startup=module.RuntimeIdentity!.Startup with
            {Plugins=module.RuntimeIdentity.Startup.Plugins.Where(p=>p.Id is "kernel" or "flow").ToList()};
        var render=new RuntimePluginDefinition("observer","1",1,[],["projection"],scope=>
        {
            var view=scope.Service<IBoardProjectionSource>("projection").ForSeat("p1");
            scope.Render("draw",_=>Check.That(!Wire.Json(view.View()).Contains("hidden"),"System context leaked to render"));
        }) {Permissions=[AuthorityPlugin.Access],Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
        startup=startup with {Plugins=[..startup.Plugins,new("observer","1",1,JsonSerializer.SerializeToElement(new{}))]};
        RuntimePluginDefinition[] catalog=[AuthorityPlugin.Create(()=>module,1),FlowRuntimePlugin.Create(()=>module),render];
        Check.That(catalog[1].Requires.All(c=>c is "authority" or "flow-transactions"),"Flow depends on the loop");
        using var host=RuntimeHost.StartScopedAsync(startup,catalog,["flow","rules"],HostMode.Graphical).GetAwaiter().GetResult();
        var flow=host.Service<IFlowProgression>("flow");
        var initial=Wire.Json(flow.ExportAuthorityState());
        host.Driver!.AdvanceAsync(TimeSpan.FromSeconds(.01)).GetAwaiter().GetResult();
        Check.That(initial==Wire.Json(flow.ExportAuthorityState()),"Idle driver advanced a flow node");
        flow.Advance();Check.That(flow.Fault==null && flow.ExportAuthorityState().Flow!.Status=="waiting");
        var waiting=Wire.Json(flow.ExportLog());
        for(var reconnect=0;reconnect<2;reconnect++)
        {
            using var viewport=RuntimeHost.StartScopedAsync(startup,catalog,[],HostMode.Graphical,RuntimeScope.Viewport,host,"observer").GetAwaiter().GetResult();
            for(var tick=0;tick<5;tick++)
            {
                flow.Advance();_=flow.Project("p1");_=flow.Actions("p2");
                host.Driver.AdvanceAsync(TimeSpan.FromSeconds(.01)).GetAwaiter().GetResult();
                viewport.Render(TimeSpan.FromSeconds(.01));
            }
        }
        Check.That(waiting==Wire.Json(flow.ExportLog()),"Waiting/render/reconnect re-entered a committed node");
        var stale=flow.Actions("p2").Single().Command;
        Check.That(flow.Submit("p2",stale).Accepted && !flow.Submit("p2",stale).Accepted);
        while(!flow.ExportAuthorityState().Ended) Check.That(flow.Submit("p2",flow.Actions("p2").Single().Command).Accepted);
        var ended=Wire.Json(flow.ExportLog());flow.Advance();
        Check.That(ended==Wire.Json(flow.ExportLog()) && flow.Fault==null,"Terminal flow repeated effects");
    }
}
