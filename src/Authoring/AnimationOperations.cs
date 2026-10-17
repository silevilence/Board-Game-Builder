using System.Text;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public sealed record AnimationAuthorModel(ProjectSource Source,string Path,AnimationLibrary Definition,string[] Diagnostics) : IAuthorOutcome
{public bool Success=>Diagnostics.Length==0;public int ExitCode=>Success?0:1;}

public static class AnimationOperations
{
    public static string Path(IAuthorFiles view)
    {
        var startup=Wire.Read<RuntimeStartup>(view.Text(GameRuntime.Entry));
        var plugin=startup.Plugins.SingleOrDefault(p=>p.Id=="resources.animations") ?? throw new InvalidDataException("resources.animations: not enabled");
        if(plugin.Version!="1" || plugin.Api!=1)throw new InvalidDataException("resources.animations: unsupported version");
        var path=plugin.Configuration.GetProperty("path").GetString()!;ProjectPaths.Validate(path);return path;
    }
    public static AnimationAuthorModel Read(ProjectView view)
    {
        var path=Path(view);var definition=AnimationFile.Parse(view.Text(path));var errors=new List<string>();
        try {using var snapshot=new ProjectSnapshot(view);_=GameRuntime.Load(snapshot.Directory);}
        catch(Exception error) when(error is InvalidDataException or System.Text.Json.JsonException or FileNotFoundException) {errors.Add(error.Message);}
        return new(view.Source,path,definition,errors.ToArray());
    }
    public static ProjectChanges Change(ProjectView view,AnimationLibrary definition,string request)=>view.Change(request,new Dictionary<string,byte[]?>{[Path(view)]=Encoding.UTF8.GetBytes(AnimationFile.Write(definition))});
    public static ProjectChanges Create(ProjectView view,string path,string request)
    {
        ProjectPaths.Validate(path);if(view.Exists(path))throw new InvalidDataException(path+": target already exists");
        var startup=JsonNode.Parse(view.Text(GameRuntime.Entry))!.AsObject();
        if(startup["plugins"]!.AsArray().Any(p=>p!["id"]!.GetValue<string>()=="resources.animations"))throw new InvalidDataException("resources.animations: already enabled");
        ResourceOperations.Ensure(startup,"resources");
        startup["plugins"]!.AsArray().Add(new JsonObject{["id"]="resources.animations",["version"]="1",["api"]=1,["configuration"]=new JsonObject{["path"]=path}});
        return view.Change(request,new Dictionary<string,byte[]?>{[GameRuntime.Entry]=Encoding.UTF8.GetBytes(startup.ToJsonString(new(){WriteIndented=true})),[path]=Encoding.UTF8.GetBytes(AnimationFile.Write(new(1,[])))});
    }
    private static AuthorOperationInfo Info(string id,string description)=>new(id,"author.configuration",description,new(),new(){["type"]="object"},[],[]);
    public static AuthorOperationInfo[] Operations=>[
        Info("animation.read","Read animation definitions and shared diagnostics"),
        Info("animation.validate","Validate the current animation draft and object/resource references"),
        Info("animation.create","Enable an empty animation library without overwriting files") with {Parameters=new(){["path"]=new("string",true,"New project JSON file")},Effects=["project"]},
        Info("animation.replace","Replace animation content through the current edit mode") with {Parameters=new(){["version"]=new("string",true,"Source version from animation.read"),["definition"]=new("object",true,"AnimationLibrary format 1")},Effects=["project"]}
    ];
    public static void Register(AuthorScope scope)
    {
        foreach(var info in Operations)scope.Register(new(info,async call=>
        {
            if(info.Id is "animation.read" or "animation.validate")return Read(call.View);
            ProjectChanges changes;
            if(info.Id=="animation.create")changes=Create(call.View,call.String("path"),call.RequestId);
            else
            {
                if(call.String("version")!=call.View.Source.Version)throw new AuthorOperationException("project.conflict","Animation source version changed");
                changes=Change(call.View,AnimationFile.Parse(call.Arguments.GetProperty("definition").GetRawText()),call.RequestId);
            }
            return await call.Mode.Apply(changes,call.Cancellation);
        }));
    }
}
