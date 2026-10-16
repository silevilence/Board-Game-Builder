using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public static IGameAuthorFormat Format(IAuthorFiles view)
    {
        var startup=Wire.Read<RuntimeStartup>(view.Text(GameRuntime.Entry));
        var plugin=startup.Plugins.SingleOrDefault(p=>p.Id=="flow") ?? throw new AuthorOperationException("flow.disabled","Enable the flow runtime plugin",GameRuntime.Entry);
        if(plugin.Version!="1" || plugin.Api!=1) throw new AuthorOperationException("flow.version","Unsupported flow plugin version",GameRuntime.Entry);
        var path=plugin.Configuration.GetProperty("path").GetString()!;ProjectPaths.Validate(path);
        return new FlowAuthorFormat(path);
    }
    private sealed record FlowAuthorFormat(string DefinitionPath) : IGameAuthorFormat
    {
        public JsonObject Read(IAuthorFiles view)=>JsonNode.Parse(FlowFile.Write(FlowFile.Parse(view.Text(DefinitionPath))))!.AsObject();
        public string Serialize(IAuthorFiles view,JsonObject configuration)=>FlowFile.Write(FlowFile.Parse(configuration.ToJsonString()));
    }
    public static FlowAuthorModel Read(ProjectView view)
    {
        var format=Format(view);
        var definition=FlowFile.Parse(format.Read(view).ToJsonString());
        var scripts=new Dictionary<string,string>();
        var references=new List<FlowDiagnostic>();
        foreach(var script in FlowFile.Scripts(definition))
            if(!string.IsNullOrWhiteSpace(script.Path))
                try {if(view.Exists(script.Path)) scripts[script.Path]=view.Text(script.Path);}
                catch(Exception error) when(error is ArgumentException or InvalidDataException) {references.Add(new("script-reference",error.Message));}
        return new(view.Source,format.DefinitionPath,definition,[..references,..FlowValidation.Validate(definition,scripts),..FlowReferences.Validate(view,definition)]);
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
                var format=Format(call.View);
                var definition=format.Serialize(call.View,JsonNode.Parse(call.Arguments.GetProperty("definition").GetRawText())!.AsObject());
                return await call.Mode.Apply(call.View.Change(call.RequestId,new Dictionary<string,byte[]?>{
                    [format.DefinitionPath]=Encoding.UTF8.GetBytes(definition)}),call.Cancellation);
            }));
    }
}
