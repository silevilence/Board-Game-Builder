using BoardGame.Runtime;

namespace BoardGame.Authoring;

/// <summary>Legacy board author policy, selected by declared loader identity rather than project name or path.</summary>
public sealed class BoardAuthorRuntime : IAuthorPackageRuntime,IAuthorPlayerRuntime
{
    public void PreflightPlayer(string release,ProjectView view,CancellationToken cancellation)
    {cancellation.ThrowIfCancellationRequested();using var snapshot=new ProjectSnapshot(view);_=PlayerDistribution.Preflight(release,snapshot.Directory);}
    public string ExportPlayer(string release,ProjectView view,string target,AuthorExportRequest request)=>ExportSnapshots.Use(view,request,snapshot=>
    {
        PlayerDistribution.Export(release,snapshot.Directory,target,path=>request.AdditionalSteps?.Invoke(snapshot.Directory,path),request.Progress,cancellation:request.Cancellation);
        return GameRuntime.Load(Path.Combine(target,"game.bgm")).Fingerprint;
    });
    public string Pack(ProjectView view,string destination,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();using var snapshot=new ProjectSnapshot(view);
        GamePackage.Pack(snapshot.Directory,destination);
        return GameRuntime.Load(destination).Fingerprint;
    }
    public string Id=>"board";
    public static AuthorRuntimes LegacyCatalog(){var catalog=new AuthorRuntimes();catalog.Register(new BoardAuthorRuntime());return catalog;}
    public static AuthorPlugin Plugin=>new("author.runtime.board","1",1,[],["author.runtimes"],scope=>
        scope.Own(scope.Service<AuthorRuntimes>("author.runtimes").Register(new BoardAuthorRuntime())),"Installed legacy board validation adapter",true);
    public bool Supports(IAuthorFiles files)=>System.Text.Json.Nodes.JsonNode.Parse(files.Text(GameRuntime.Entry))!["plugins"]!.AsArray()
        .Any(p=>p?["id"]?.GetValue<string>() is "definition.module" or "definition.state");
    public Task<IReadOnlyList<string>> Validate(ProjectView view,string layer,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();using var snapshot=new ProjectSnapshot(view);var checks=new List<string>();
        try
        {
            var startup=GameRuntime.ReadStartup(snapshot.Directory);
            if(layer=="dependencies")
            {
                _=GameResources.Load(startup,snapshot.Directory);
                foreach(var plugin in startup.Plugins.Where(p=>p.Id is "lua.rules" or "lua.cpu"))
                {
                    var file=plugin.Configuration.GetProperty("file").GetString()!;
                    if(!view.Exists(file))throw new AuthorOperationException("dependency.missing","Missing script for "+plugin.Id,file);
                }
            }
            if(layer is "content" or "runtime")
            {
                var module=GameRuntime.Load(snapshot.Directory);
                if(layer=="runtime")
                {
                    using(var game=new PlaySession(module,cancellation:cancellation))
                    {_=game.Actions();if(game.Fault!=null)throw new AuthorOperationException(game.Fault.Code,game.Fault.Detail);}
                    if(startup.Plugins.Any(p=>p.Id=="lua.cpu"))
                    {
                        checks.Add("initial-cpu-action");
                        using var cpu=new PlaySession(module,1,module.Initial.Seats.ToDictionary(s=>s.Id,_=>"cpu"),cancellation:cancellation);
                        _=cpu.StepCpu();if(cpu.Fault!=null)throw new AuthorOperationException(cpu.Fault.Code,cpu.Fault.Detail);
                    }
                }
            }
            return Task.FromResult<IReadOnlyList<string>>(checks);
        }
        catch(AuthorOperationException){throw;}
        catch(Exception error) when(error is not OperationCanceledException and not OutOfMemoryException)
        {throw new AuthorOperationException("validation."+layer,error.Message.Replace(snapshot.Directory,"<snapshot>"));}
    }
}
