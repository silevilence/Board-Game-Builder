using BoardGame.Authoring;
using System.Text.Json;
namespace Engine.Tests;

public static class AuthorExportContractTests
{
    public static void Run()=>Verify().GetAwaiter().GetResult();
    private static async Task Verify()
    {
        var registry=new AuthorExports();var a=new Provider("a");var b=new Provider("b");
        var first=registry.Register(a);using var second=registry.Register(b);
        Check.That(registry.Items.Count==2);
        Check.Throws(()=>registry.Register(a),"export.duplicate");
        var view=new ProjectView(new("","contract","memory"),new Dictionary<string,byte[]>());
        var request=new AuthorExportRequest(view,new(),JsonSerializer.SerializeToElement(new{}));
        a.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var run=registry.Export("a",request);
        Check.That(registry.Active("a") && !registry.Active("b"));
        Check.Throws(first.Dispose,"export.active");
        Check.That((await registry.Export("b",request)).Source==view.Source);
        a.Pending.SetResult();await run;first.Dispose();
        Check.Throws(()=>registry.Export("a",request).GetAwaiter().GetResult(),"Enable export provider");
        using var restarted=registry.Register(a);a.Pending=null;
        Check.That((await registry.Preflight("a",request)).Available && registry.Items.Count==2);
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        try {await registry.Export("a",request with {Cancellation=cancellation.Token});throw new Exception("Ignored cancellation");}
        catch(OperationCanceledException) { }
        Check.That(!registry.Active("a"));
    }
    private sealed class Provider(string id):IAuthorExporter
    {
        public TaskCompletionSource? Pending;
        public AuthorExportInfo Info=>new(id,"plugin."+id,"1",1,id,"fixture","No SDK",[],[],new("export."+id,"plugin."+id,"fixture",new(),new(),[],["artifact"]));
        public Task<AuthorExportCheck> Preflight(AuthorExportRequest request)=>Task.FromResult(new AuthorExportCheck(request.View.Source,id,true,[]));
        public async Task<AuthorArtifact> Export(AuthorExportRequest request)
        {if(Pending!=null)await Pending.Task;return new(request.View.Source,"fixture","fixture",request.View.Source.Version);}
    }
}
