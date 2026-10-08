using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;

namespace Engine.Tests;

public static class DefinitionPluginTests
{
    public static void Run()
    {
        foreach(var sample in new[]{"quick-cards","hex-skirmish","freecell","sealed-auction","wasm-counter","island-settlers"})
        {
            var source="samples/"+sample;var target=Path.Combine(Path.GetTempPath(),"bgb-definition-"+Guid.NewGuid());var legacyDirectory=target+"-legacy";
            try
            {
                TestModules.CopyLegacy(source,legacyDirectory);var legacy=DirectoryModule.Load(legacyDirectory);TestModules.CopyGame(source,target);
                var loaded=GameRuntime.Load(target);
                Check.That(Wire.Json(legacy.Initial)==Wire.Json(loaded.Initial),sample+": definition changed initial state");
                foreach(var resource in legacy.ResourceData!)
                {
                    var pool=loaded.Resources!;var bytes=pool.Images.TryGetValue(resource.Key,out var image)?pool.GetFile(image.Resource):pool.GetBinary(resource.Key);
                    Check.That(bytes.Span.SequenceEqual(resource.Value),sample+": definition changed resources");
                }
                var file=Path.Combine(target,"module.json");var root=JsonNode.Parse(File.ReadAllText(file))!;
                foreach(var entity in root["entities"]!.AsArray())
                {
                    var components=entity!["components"]!.AsArray();var reversed=components.Select(c=>c!.DeepClone()).Reverse().ToArray();
                    components.Clear();foreach(var component in reversed) components.Add(component);
                }
                File.WriteAllText(file,root.ToJsonString());
                Check.That(Wire.Json(GameRuntime.Load(target).Initial)==Wire.Json(loaded.Initial),sample+": component order changed definition");
                File.WriteAllText(file,"invalid definition");
                var entry=Path.Combine(target,GameRuntime.Entry);var startup=JsonNode.Parse(File.ReadAllText(entry))!;
                var plugins=startup["plugins"]!.AsArray();var provider=plugins.First(p=>p!["id"]!.GetValue<string>()=="definition.module");plugins.Remove(provider);
                File.WriteAllText(entry,startup.ToJsonString());
                Check.Throws(()=>GameRuntime.Load(target),"required capability: definition");
            }
            finally {if(Directory.Exists(target)) Directory.Delete(target,true);if(Directory.Exists(legacyDirectory)) Directory.Delete(legacyDirectory,true);}
        }
    }
}
