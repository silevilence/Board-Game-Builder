using BoardGame.AppRuntime;
using BoardGame.Plugins;
using BoardGame.Rendering;
using BoardGame.Resources;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public static class AuthorRuntimePlugins
{
    public static string DirectoryPath=>Path.GetFullPath(Environment.GetEnvironmentVariable("BGB_RUNTIME_PLUGIN_STORE") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BoardGameBuilder","runtime-plugins"));
    public static PluginStore CreateStore()=>new(DirectoryPath,"win-x64",
        [typeof(IApplicationView).Assembly,typeof(IDraw2D).Assembly,typeof(AssetSet).Assembly,typeof(AssetRegistry).Assembly,
            typeof(BoardGame.Kernel.GameState).Assembly,typeof(IAuthoritySession).Assembly,typeof(IGameSession).Assembly]);
    private static AuthorOperationInfo Operation(string id,string description,Dictionary<string,AuthorParameter> parameters,string[] effects)=>
        new(id,"author.runtime.plugins",description,parameters,new(){["type"]="object"},[],effects){ReadsProject=false};
    private static AuthorOperationInfo List=>Operation("runtime.plugins.list","Inspect explicitly installed runtime implementations and compatibility declarations",new(),[]);
    private static AuthorOperationInfo Import=>Operation("runtime.plugins.import","Explicitly import a precompiled runtime package; does not enable it in a project",
        new(){["package"]=new("string",true,"Publisher package directory"),["replace"]=new("boolean",false,"Explicitly replace this installed version")},["installation"]);
    private static AuthorOperationInfo Remove=>Operation("runtime.plugins.remove","Remove an unused explicitly installed version; never edit project selections",
        new(){["id"]=new("string",true,"Installed runtime plugin ID"),["version"]=new("string",true,"Exact installed version")},["installation"]);
    private static AuthorOperationInfo Run=>new("runtime.run","author.run.application","Run the selected approved application with bounded fixed updates",
        new(){["ticks"]=new("integer",false,"1..10000 updates, default 60"),["count"]=new("integer",false,"1..100 fresh instances, default 1")},new(){["type"]="object"},[],[]);
    public static AuthorPlugin RunPlugin=>new("author.run.application","1",1,[],["author.runtimes","author.operations"],scope=>
        scope.Register(new(Run,async call=>
        {
            var ticks=call.Integer("ticks",60);var count=call.Integer("count",1);
            if(ticks is <1 or >10000 || count is <1 or >100)throw new AuthorOperationException("arguments.invalid","ticks must be 1..10000 and count 1..100");
            var runtime=call.Runtimes.Resolve(call.View) as IAuthorApplicationRuntime ?? throw new AuthorOperationException("runtime.unsupported","The selected runtime does not expose application instances");
            using var lease=runtime.Open(call.View);
            var content=BoardGame.AppRuntime.GameRuntime.LoadFiles(call.View.CopyFiles(),lease.Catalog.Content);
            var results=new List<object>();
            for(var index=0;index<count;index++)
            {
                call.Cancellation.ThrowIfCancellationRequested();
                await using var host=await lease.Catalog.StartAsync(content,clock:new(TimeSpan.FromSeconds(1d/60)));
                host.Driver.Paused=true;
                for(var tick=0;tick<ticks;tick++)await host.Driver.StepAsync(call.Cancellation);
                if(host.Driver.Fault is {} fault)throw new AuthorOperationException("runtime.update",fault.Error.Message);
                results.Add(new{index,ticks=host.Driver.CompletedTick,simulationMilliseconds=host.Driver.SimulationTime.TotalMilliseconds});
            }
            return new{call.View.Source,content.Fingerprint,identity=content.Identity,runs=results};
        })),"Bounded batch runs use the same selected catalog and snapshot as validation and publication") {Operations=[Run]};
    public static AuthorPlugin Plugin=>new("author.runtime.plugins","1",1,["author.runtime-plugins"],["author.operations"],scope=>
    {
        var store=CreateStore();scope.Provide("author.runtime-plugins",store);
        scope.Register(new(List,_=>Task.FromResult<object>(new{directory=DirectoryPath,plugins=store.Installed()})));
        scope.Register(new(Import,call=>Task.FromResult<object>(store.Import(call.String("package"),call.Arguments.TryGetProperty("replace",out var replace) && replace.GetBoolean()))));
        scope.Register(new(Remove,call=>{store.Remove(call.String("id"),call.String("version"));return Task.FromResult<object>(new{removed=true});}));
    },"Explicit runtime implementation installation; separate from game enablement") {Operations=[List,Import,Remove]};
    public static AuthorPlugin RuntimePlugin=>new("author.runtime.external","1",1,[],["author.runtime-plugins","author.runtimes"],scope=>
    {
        var store=scope.Service<PluginStore>("author.runtime-plugins");
        scope.Own(scope.Service<AuthorRuntimes>("author.runtimes").Register(new ApplicationAuthorRuntime("external",new ApplicationCatalog(
            [AssetStaticFactories.Resources(),AssetStaticFactories.Images(),AssetStaticFactories.TexturePacker(),AssetStaticFactories.Font()]),store,true)));
    },"Author validation and exports for explicitly installed external application definitions") {Includes=[new("author.runtime.plugins")]};
}
