using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class BoardKernelPluginTests
{
    public static void Run()
    {
        var module=GameRuntime.Load("samples/quick-cards");
        var kernel=AuthorityPlugin.Create(()=>module,42);
        var startup=new RuntimeStartup(1,"kernel-test","1",[new("kernel","1",1,JsonSerializer.SerializeToElement(new{}))]);
        using var host=RuntimeHost.StartAsync(startup,[kernel],AuthorityPlugin.Required).GetAwaiter().GetResult();
        var authority=host.Service<IAuthoritySession>("authority");
        var projection=host.Service<IBoardProjectionSource>("projection");
        Check.That(projection is not IAuthoritySession,"Projection source exposes authority state");
        var seat=projection.ForSeat("p1");Check.That(seat is not IAuthoritySession,"Bound projection exposes authority state");
        Check.That(Wire.Json(seat.View())==Wire.Json(authority.Project("p1")));
        var copy=seat.View();copy.Entities.Clear();Check.That(seat.View().Entities.Count>0,"Projection shares mutable state");
        foreach(var capability in AuthorityPlugin.Required.Concat(["flow-transactions","board-preparation"]))
        {
            var intruder=new RuntimePluginDefinition("rules.untrusted","1",1,[],[capability],_=>throw new Exception("Should never activate"));
            var selected=startup with {Plugins=[..startup.Plugins,new(intruder.Id,"1",1,JsonSerializer.SerializeToElement(new{permissions=new[]{AuthorityPlugin.Access}}))]};
            Check.Throws(()=>RuntimeHost.StartAsync(selected,[kernel,intruder],[]).GetAwaiter().GetResult(),"permission denied");
        }
        var authorized=new RuntimePluginDefinition("trusted-adapter","1",1,["bound-view"],["projection"],s=>s.Provide("bound-view",s.Service<IBoardProjectionSource>("projection").ForSeat("p1")))
            {Permissions=[AuthorityPlugin.Access]};
        using var scoped=RuntimeHost.StartAsync(startup with {Plugins=[..startup.Plugins,new(authorized.Id,"1",1,JsonSerializer.SerializeToElement(new{}))]},[kernel,authorized],[]).GetAwaiter().GetResult();
        Check.That(scoped.Service<ISeatProjection>("bound-view").View().Entities.Count>0);
        Check.That(!typeof(AuthorityPlugin).Assembly.GetReferencedAssemblies().Any(a=>a.Name is "Runtime" or "Lua" or "Wasmtime" or "Editor" or "View.MonoGame"),"Board base plugin depends on optional implementations");
        var preparation=host.Service<IBoardTransactionSource>("board-preparation");
        var branch=preparation.Begin();var retained=branch.Authority;
        retained.Submit("missing",new(){Kind="next"});
        Check.That(authority.ExportLog().EntryCount==0 && branch.TryCommit() && authority.ExportLog().EntryCount==1);
        Check.That(!branch.TryCommit());Check.Throws(()=>retained.Submit("p1",new(){Kind="next"}),"already been published");
        var stale=preparation.Begin();authority.Submit("missing",new(){Kind="next"});
        Check.That(!stale.TryCommit(),"Stale authority branch overwrote a newer transaction");
        var before=Wire.Json(authority.ExportAuthorityState());
        Check.That(!authority.Submit("p2",new(){Kind="move",Entity="missing"}).Accepted && Wire.Json(authority.ExportAuthorityState())==before,"Kernel failure changed authority state");
    }
}
