using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;

namespace Engine.Tests;

// Current-format integration fixtures always retain their verified runtime identity.
// Historical tests call DirectoryModule/ModulePackage explicitly.
public static class TestModules
{
    public static LoadedModule Load(string source)=>GameRuntime.Load(source);
    public static void Pack(string source,string destination)=>GamePackage.Pack(source,destination);
    public static void CopyGame(string source,string destination)
    {
        _=GameRuntime.ReadStartup(source);
        Copy(source,destination);
    }
    public static void CopyLegacy(string source,string destination)
    {
        var startup=GameRuntime.ReadStartup(source);
        Copy(source,destination);
        var definition=startup.Plugins.Single(p=>p.Id=="definition.module").Configuration.GetProperty("path").GetString()!;
        var root=JsonNode.Parse(File.ReadAllText(Path.Combine(source,definition)))!.AsObject();
        var pool=GameRuntime.Load(source).Resources!;
        root["resources"]=new JsonObject();
        foreach(var image in pool.Images)root["resources"]![image.Key]=image.Value.Resource;
        foreach(var binary in pool.Binaries)root["resources"]![binary.Key]=binary.Value;
        root["id"]=startup.Id;root["version"]=startup.Version;
        root["rules"]=startup.Plugins.Single(p=>p.Id=="lua.rules").Configuration.GetProperty("file").GetString();
        root["cpu"]=startup.Plugins.Single(p=>p.Id=="lua.cpu").Configuration.GetProperty("file").GetString();
        var wasm=startup.Plugins.FirstOrDefault(p=>p.Id=="wasm");
        if(wasm!=null) root["plugins"]=JsonNode.Parse(wasm.Configuration.GetProperty("extensions").GetRawText());
        File.WriteAllText(Path.Combine(destination,"module.json"),root.ToJsonString());File.Delete(Path.Combine(destination,GameRuntime.Entry));
    }
    public static LoadedModule LuaGame(GameState state,string rules,string cpu)
    {
        var selections=new List<RuntimePluginSelection>();
        foreach(var id in new[]{"kernel","session.local","recording"}) selections.Add(Select(id,new {}));
        selections.Add(Select("definition.state",new {path="position.json"}));
        selections.Add(Select("lua.rules",new {file="rules.lua"}));
        selections.Add(Select("lua.cpu",new {file="cpu.lua"}));
        var startup=new RuntimeStartup(1,"fixture","1",selections);
        return Rebuild(new("fixture","1","",state,rules,cpu,RuntimeIdentity:new(1,startup,"position.json",new())));
    }
    // Deliberately changed rules/state/WASM become a new authored fixture via the real loader.
    public static LoadedModule Rebuild(LoadedModule module)
    {
        var startup=Wire.Copy(module.RuntimeIdentity?.Startup ?? throw new InvalidDataException("Fixture requires an explicit plugin plan"));
        var loader=startup.Plugins.FindIndex(p=>p.Id is "definition.module" or "definition.state");
        startup.Plugins[loader]=Select("definition.state",new {path="position.json"});
        startup.Plugins.RemoveAll(p=>p.Id=="wasm");
        if(module.RuntimePlugins is {Count:>0}) startup.Plugins.Add(Select("wasm",new {extensions=module.RuntimePlugins.Select(p=>
            new RuntimeDependency(p.Id,p.Version,p.Api,Kind:"wasm",File:p.File,Sha256:p.Sha256,Permissions:p.Permissions)).ToArray()}));
        var directory=Path.Combine(Path.GetTempPath(),"bgb-explicit-fixture-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            void Write(string path,byte[] content) {var full=Path.Combine(directory,path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);File.WriteAllBytes(full,content);}
            foreach(var id in new[]{"lua.rules","lua.cpu"})
            {
                var selection=startup.Plugins.SingleOrDefault(p=>p.Id==id);
                if(selection!=null) Write(selection.Configuration.GetProperty("file").GetString()!,System.Text.Encoding.UTF8.GetBytes(id=="lua.rules"?module.Rules:module.Cpu));
            }
            foreach(var plugin in module.RuntimePlugins ?? []) Write(plugin.File,plugin.Code);
            foreach(var resource in module.Resources?.Files ?? new Dictionary<string,ReadOnlyMemory<byte>>())Write(resource.Key,resource.Value.ToArray());
            File.WriteAllText(Path.Combine(directory,"position.json"),Wire.Json(new {format=1,state=module.Initial}));
            File.WriteAllText(Path.Combine(directory,GameRuntime.Entry),Wire.Json(startup));
            return GameRuntime.Load(directory);
        }
        finally {Directory.Delete(directory,true);}
    }
    private static RuntimePluginSelection Select(string id,object configuration)=>new(id,"1",1,JsonSerializer.SerializeToElement(configuration,Wire.Options));
    private static void Copy(string source,string destination)
    {
        Directory.CreateDirectory(destination);
        foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
        {
            var target=Path.Combine(destination,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);
        }
    }
}
