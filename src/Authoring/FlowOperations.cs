using System.Text;
using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public sealed record FlowAuthorModel(ProjectSource Source,string Path,FlowDefinition Definition,IReadOnlyList<FlowDiagnostic> Diagnostics) : IAuthorOutcome
{
    public bool Success=>Diagnostics.All(d=>d.Severity!="error");
    public int ExitCode=>Success?0:1;
}

// Shared author operations extend the existing configuration plugin; no UI or alternate executor.
public static class FlowOperations
{
    public static FlowAuthorModel Read(ProjectView view)
    {
        var startup=Wire.Read<RuntimeStartup>(view.Text(GameRuntime.Entry));
        var plugin=startup.Plugins.SingleOrDefault(p=>p.Id=="flow") ?? throw new AuthorOperationException("flow.disabled","Enable the flow runtime plugin",GameRuntime.Entry);
        var path=plugin.Configuration.GetProperty("path").GetString()!;ProjectPaths.Validate(path);
        var definition=FlowFile.Parse(view.Text(path));
        var scripts=new Dictionary<string,string>();
        foreach(var script in FlowFile.Scripts(definition))
            if(view.Exists(script.Path)) scripts[script.Path]=view.Text(script.Path);
        return new(view.Source,path,definition,FlowValidation.Validate(definition,scripts));
    }
    private static AuthorOperationInfo Info(string id,string description)=>new(id,"author.configuration",description,new(),
        new(){["type"]="object",["description"]="Project source/version, flow definition and node/edge diagnostics"},["flow enabled in game.start.json"],[]);
    public static AuthorOperationInfo[] Operations=>[
        Info("flow.read","Read the flow graph and shared diagnostics from the current project view"),
        Info("flow.validate","Validate the flow graph without running scripts or saving drafts"),
        Info("flow.replace","Replace a flow draft through the selected edit mode; structural errors remain editable") with {
            Parameters=new(){["definition"]=new("object",true,"Versioned FlowDefinition"),["version"]=new("string",true,"Project version from flow.read")},Effects=["project"]}
    ];
    public static void Register(AuthorScope scope)
    {
        foreach(var info in Operations)
            scope.Register(new(info,async call=>
            {
                if(info.Id!="flow.replace") return Read(call.View);
                if(call.String("version")!=call.View.Source.Version) throw new AuthorOperationException("project.conflict","Flow draft was read from another project version");
                var existing=Read(call.View);
                var definition=FlowFile.Parse(call.Arguments.GetProperty("definition").GetRawText());
                return await call.Mode.Apply(call.View.Change(call.RequestId,new Dictionary<string,byte[]?>{
                    [existing.Path]=Encoding.UTF8.GetBytes(FlowFile.Write(definition))}),call.Cancellation);
            }));
    }
}
