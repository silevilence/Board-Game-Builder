using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

internal sealed class ModuleDefinitionLoader(RuntimeStartup startup,string source) : IDefinitionLoader
{
    public string Source=>source;
    public GameDefinition Load(string directory)
    {
        var metadata=new JsonObject { ["id"]=startup.Id,["version"]=startup.Version,["rules"]=null,["cpu"]=null,["plugins"]=new JsonArray() };
        foreach(var plugin in startup.Plugins)
        {
            if(plugin.Id=="lua.rules") metadata["rules"]=plugin.Configuration.GetProperty("file").GetString();
            if(plugin.Id=="lua.cpu") metadata["cpu"]=plugin.Configuration.GetProperty("file").GetString();
            if(plugin.Id=="wasm") metadata["plugins"]=JsonNode.Parse(plugin.Configuration.GetProperty("extensions").GetRawText());
        }
        var loaded=DirectoryModule.LoadDefinition(directory,Source,metadata);
        return RuntimeDefinition.From(loaded with {RulesPath=metadata["rules"]?.GetValue<string>() ?? "",CpuPath=metadata["cpu"]?.GetValue<string>() ?? ""});
    }
    public IReadOnlyList<string> ContentFiles(string directory)
    {
        var files=new List<string>();
        foreach(var path in GamePresets.Choices(startup).Values.Append(Source).Distinct())
        {
            var root=JsonNode.Parse(File.ReadAllText(DirectoryModule.SafeFile(directory,path)))!.AsObject();
            files.Add(path);
            if(root["runtimeFiles"] is JsonArray extra) files.AddRange(extra.Select(p=>p!.GetValue<string>()));
        }
        return files;
    }
}

internal static class DefinitionPlugins
{
    public static RuntimePluginDefinition Module(Func<RuntimeStartup> startup)=>new("definition.module","1",1,["definition"],[],scope=>
        scope.Provide("definition",new ModuleDefinitionLoader(startup(),scope.Configuration.GetProperty("path").GetString()!)))
        {Content=DefinitionContent.Contract(true)};
}
