using System.Text.Json.Nodes;
using BoardGame.Modules;
namespace Engine.Tests;
public static class ModuleTests
{
    public static void Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bgb-tests-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "rules.lua"), "return {}"); File.WriteAllText(Path.Combine(dir, "cpu.lua"), "return {}");
            var json = """
            {"format":1,"id":"test","version":"1","rules":"rules.lua","cpu":"cpu.lua","resources":{},
             "seats":[{"id":"p1","team":"a"}],"phases":["setup","play"],"entities":[
             {"id":"card","components":[{"type":"owner","data":{"seat":"p1"}},{"type":"properties","data":{"value":3}}]}]}
            """;
            void Save(string text) => File.WriteAllText(Path.Combine(dir, "module.json"), text);
            Save(json); var original = DirectoryModule.Load(dir);
            var node = JsonNode.Parse(json)!; var components = node["entities"]![0]!["components"]!.AsArray();
            var first = components[0]!.DeepClone(); components.RemoveAt(0); components.Add(first); Save(node.ToJsonString());
            Check.That(DirectoryModule.Load(dir).Fingerprint == original.Fingerprint, "component order changed semantics");
            foreach (var broken in new[] { json.Replace("\"format\":1", "\"format\":2"), json.Replace("\"owner\"", "\"unknown\""),
                json.Replace("\"seat\":\"p1\"", "\"seat\":\"missing\""), json.Replace("\"id\":\"test\"", "\"id\":\"test\",\"id\":\"duplicate\""),
                json.Replace("\"rules.lua\"", "\"../outside.lua\"") })
            { Save(broken); Check.Throws(() => DirectoryModule.Load(dir), "module.json"); }
        }
        finally { Directory.Delete(dir, true); }
    }
}
