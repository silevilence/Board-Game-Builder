using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Authoring;

public static class ResourceOperations
{
    public static JsonObject AddImages(JsonObject startup,IReadOnlyDictionary<string,string> images)
    {
        _=Ensure(startup,"resources");var config=Ensure(startup,"resources.images");
        foreach(var image in images) config["sources"]!.AsArray().Add(new JsonObject {["path"]=image.Value,["name"]=image.Key});
        return startup;
    }
    private static AuthorOperationInfo ImportInfo(string kind) => new("resources.import." + kind, "author.resources",
        kind == "image" ? "Import PNG/JPEG/GIF/BMP without changing pixels; reject existing paths and resource names" : "Import TexturePacker JSON Hash and its meta.image together; frame names use the supplied prefix",
        new() { ["source"] = new("string", true, "External source file"), ["target"] = new("string", true, "New relative project file"),
            ["name"] = new("string", kind == "image", kind == "image" ? "Unique resource name" : "Optional frame-name prefix") },
        FileOperations.WriteInfo.ResultSchema.DeepClone().AsObject(), ["Readable startup resource configuration"], ["project"]);
    private static AuthorOperationInfo MoveInfo => new("resources.move", "author.resources", "Move an explicitly declared resource file, preserving names. Directory/glob sources require manual coordination. Dynamic script path references are not rewritten.",
        new() { ["from"] = new("string", true, "Current relative file"), ["to"] = new("string", true, "New relative file") },
        FileOperations.WriteInfo.ResultSchema.DeepClone().AsObject(), ["Explicit resource source, no target collision"], ["project"]);
    public static AuthorPlugin Plugin => new("author.resources", "1", 1, [], ["author.operations"], scope =>
    {
        scope.Register(new(ImportInfo("image"), call => Import(call, false)));
        scope.Register(new(ImportInfo("atlas"), call => Import(call, true)));
        scope.Register(new(MoveInfo, Move));
    }, "Resource import and declaration-aware file organization") { Documents=[AuthorDocuments.Bundled("resources","author.resources","图片与图集")], Operations = [ImportInfo("image"), ImportInfo("atlas"), MoveInfo] };
    private static async Task<object> Import(AuthorCall call, bool atlas)
    {
        var target = call.String("target"); ProjectPaths.Validate(target);
        var external = Path.GetFullPath(call.String("source"));
        if (new FileInfo(external).Length > 16 * 1024 * 1024) throw new AuthorOperationException("resource.size", "Source exceeds 16 MiB", external);
        var bytes = File.ReadAllBytes(external);
        var files = new Dictionary<string, byte[]?> { [target] = bytes };
        if (atlas)
        {
            var image = GameResources.AtlasImage(bytes, external);
            var relative = (Path.GetDirectoryName(target) is { Length: > 0 } parent ? parent.Replace('\\', '/') + "/" : "") + image;
            ProjectPaths.Validate(relative);
            var sourceImage = ProjectPaths.Resolve(Path.GetDirectoryName(external)!, image);
            if (new FileInfo(sourceImage).Length > 16 * 1024 * 1024) throw new AuthorOperationException("resource.size", "Image exceeds 16 MiB", sourceImage);
            if (!files.TryAdd(relative, File.ReadAllBytes(sourceImage))) throw new AuthorOperationException("resource.path", "Atlas image collides with JSON", relative);
        }
        foreach (var path in files.Keys)
            if (call.View.Exists(path)) throw new AuthorOperationException("resource.conflict", "Import target already exists", path);
        var startup = JsonNode.Parse(call.View.Text(GameRuntime.Entry))!.AsObject();
        Ensure(startup, "resources");
        var configuration = Ensure(startup, atlas ? "resources.texturepacker" : "resources.images");
        var declaration = new JsonObject { ["path"] = target };
        if (atlas) declaration["prefix"] = call.OptionalString("name") ?? ""; else declaration["name"] = call.String("name");
        configuration["sources"]!.AsArray().Add(declaration);
        files[GameRuntime.Entry] = Encoding.UTF8.GetBytes(startup.ToJsonString(new() { WriteIndented = true }));
        Validate(call.View, files);
        return await call.Mode.Apply(call.View.Change(call.RequestId, files), call.Cancellation);
    }
    public static JsonObject Ensure(JsonObject startup, string id)
    {
        var plugins = startup["plugins"]!.AsArray();
        var matches = plugins.Where(p => p!["id"]!.GetValue<string>() == id).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Duplicate resource plugin: " + id);
        var plugin = matches.SingleOrDefault()?.AsObject();
        if (plugin == null)
        {
            plugin = new() { ["id"] = id, ["version"] = "1", ["api"] = 1, ["configuration"] = new JsonObject() };
            plugins.Add(plugin);
        }
        var configuration = plugin["configuration"]!.AsObject();
        configuration["sources"] ??= new JsonArray();
        return configuration;
    }
    private static ResourcePool Validate(ProjectView view, IReadOnlyDictionary<string, byte[]?>? changes = null)
    {
        using var snapshot = new ProjectSnapshot(view, changes);
        var startup = JsonSerializer.Deserialize<RuntimeStartup>(File.ReadAllText(Path.Combine(snapshot.Directory, GameRuntime.Entry)), Wire.Options)!;
        return GameResources.Load(startup, snapshot.Directory);
    }
    private static async Task<object> Move(AuthorCall call)
    {
        var from = call.String("from"); var to = call.String("to"); ProjectPaths.Validate(to);
        if (call.View.Exists(to)) throw new AuthorOperationException("resource.conflict", "Move target exists", to);
        var before = Validate(call.View);
        var startup = JsonNode.Parse(call.View.Text(GameRuntime.Entry))!.AsObject();
        var declarations = startup["plugins"]!.AsArray().Where(p => p!["id"]!.GetValue<string>() is "resources" or "resources.images" or "resources.texturepacker")
            .SelectMany(p => (p!["configuration"]?["sources"] as JsonArray ?? []).Select(s => (Plugin: p["id"]!.GetValue<string>(), Source: s!.AsObject()))).ToArray();
        var matching = declarations.Where(d => d.Source["path"]?.GetValue<string>() == from).ToArray();
        if (matching.Length != 1 || matching[0].Source.ContainsKey("pattern")) throw new AuthorOperationException("resource.scope", "Only a single explicit source can be moved automatically", from);
        var files = new Dictionary<string, byte[]?> { [from] = null, [to] = call.View.Bytes(from) };
        var declaration = matching[0];
        declaration.Source["path"] = to;
        if (declaration.Plugin == "resources.texturepacker")
        {
            var image = GameResources.AtlasImage(files[to]!, from);
            var oldImage = Path.Combine(Path.GetDirectoryName(from) ?? "", image).Replace('\\', '/');
            var newImage = Path.Combine(Path.GetDirectoryName(to) ?? "", image).Replace('\\', '/');
            if (oldImage != newImage)
            {
                if (call.View.Exists(newImage)) throw new AuthorOperationException("resource.conflict", "Atlas image target exists", newImage);
                files[newImage] = call.View.Bytes(oldImage);
                // Keep the original image when other declarations may still use it.
            }
        }
        else if (declaration.Source["name"] == null) declaration.Source["name"] = Path.GetFileNameWithoutExtension(from);
        files[GameRuntime.Entry] = Encoding.UTF8.GetBytes(startup.ToJsonString(new() { WriteIndented = true }));
        var after = Validate(call.View, files);
        if (!before.Images.Keys.Order().SequenceEqual(after.Images.Keys.Order()) || !before.Binaries.Keys.Order().SequenceEqual(after.Binaries.Keys.Order()))
            throw new AuthorOperationException("resource.references", "Move would change resource names; coordinate directory/glob declarations manually");
        return await call.Mode.Apply(call.View.Change(call.RequestId, files), call.Cancellation);
    }
}
