using System.Text.Json;
using BoardGame.Kernel;
namespace BoardGame.Modules;
public static class DirectoryModule
{
    private enum DefinitionInput { Legacy, Startup }
    public static LoadedModule Load(string directory)=>LoadCore(directory,"module.json",DefinitionInput.Legacy,null);
    // Only the selected definition plugin supplies capability-owned metadata.
    public static LoadedModule LoadDefinition(string directory,string path,System.Text.Json.Nodes.JsonObject metadata)=>LoadCore(directory,path,DefinitionInput.Startup,metadata ?? throw new ArgumentNullException(nameof(metadata)));
    private static LoadedModule LoadCore(string directory,string path,DefinitionInput input,System.Text.Json.Nodes.JsonObject? supplied)
    {
        var file = Path.Combine(Path.GetFullPath(directory),path);
        try
        {
            file=SafeFile(directory,path);
            using var original = JsonDocument.Parse(File.ReadAllText(file));
            CheckDuplicates(original.RootElement,"$");
            var authored=System.Text.Json.Nodes.JsonNode.Parse(original.RootElement.GetRawText())!.AsObject();
            if(supplied!=null)
                foreach(var item in supplied)
                {
                    if(authored.ContainsKey(item.Key)) Fail($"$.{item.Key}: belongs to game.start.json; duplicate configuration is forbidden");
                    authored[item.Key]=item.Value?.DeepClone();
                }
            using var document = JsonDocument.Parse(AuthorConfiguration.Expand(authored).ToJsonString());
            var root = document.RootElement;
            CheckDuplicates(root, "$");
            var metadata=System.Text.Json.Nodes.JsonNode.Parse(root.GetRawText())!.AsObject();
            var dependencies=ModulePackage.Dependencies(metadata);
            var runtimeHashes=new SortedDictionary<string,string>(StringComparer.Ordinal) { ["dependencies"]=Wire.Hash(Wire.Json(dependencies)) };
            foreach(var runtimeFile in ModulePackage.ReferencedFiles(metadata)) _=SafeFile(directory,runtimeFile);
            var plugins=new List<RuntimePlugin>();
            foreach(var dependency in dependencies.Where(d=>d.Kind=="wasm"))
            {
                var code=File.ReadAllBytes(SafeFile(directory,dependency.File!));
                if(code.Length>256*1024 || ModulePackage.Hash(code)!=dependency.Sha256?.ToUpperInvariant()) Fail($"plugins[{dependency.Id}]: WASM content hash/size mismatch");
                runtimeHashes[dependency.File!]=ModulePackage.Hash(code);
                plugins.Add(new(dependency.Id,dependency.Version,dependency.Api,dependency.File!,dependency.Sha256!,dependency.Permissions ?? [],code));
            }
            if(metadata["runtimeFiles"] is System.Text.Json.Nodes.JsonArray runtimeFiles)
                foreach(var runtimeFile in runtimeFiles) { var contentPath=runtimeFile!.GetValue<string>(); runtimeHashes[contentPath]=ModulePackage.Hash(File.ReadAllBytes(SafeFile(directory,contentPath))); }
            if (root.GetProperty("format").GetInt32() != 1) Fail("$.format: unsupported version");
            var id = Text(root, "id"); var version = Text(root, "version");
            var rulesPath = root.TryGetProperty("rules",out var ruleFile) && ruleFile.ValueKind==JsonValueKind.String?SafeFile(directory,ruleFile.GetString()!):"";
            var cpuPath = root.TryGetProperty("cpu",out var cpuFile) && cpuFile.ValueKind==JsonValueKind.String?SafeFile(directory,cpuFile.GetString()!):"";
            if(input==DefinitionInput.Legacy && (rulesPath=="" || cpuPath=="")) Fail("rules/cpu: missing legacy script entry");
            var rules = rulesPath==""?"":File.ReadAllText(rulesPath); var cpu = cpuPath==""?"":File.ReadAllText(cpuPath);
            var resources = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var resourceData = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var resource in root.GetProperty("resources").EnumerateObject())
            {
                var bytes=File.ReadAllBytes(SafeFile(directory, resource.Value.GetString()!));
                resources.Add(resource.Name, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
                resourceData.Add(resource.Name,bytes);
            }
            var state = new GameState();
            if(root.TryGetProperty("layout",out var layout)) state.Layout=Wire.Read<BoardLayout>(layout.GetRawText());
            foreach (var seat in root.GetProperty("seats").EnumerateArray())
            {
                var controller = seat.TryGetProperty("controller", out var control) ? control.GetString()! : "human";
                if (controller is not ("human" or "cpu")) Fail("$.seats.controller: expected human or cpu");
                state.Seats.Add(new Seat(Text(seat, "id"), Text(seat, "team"), controller));
            }
            if (state.Seats.Count == 0 || state.Seats.Select(s => s.Id).Distinct().Count() != state.Seats.Count) Fail("$.seats: empty or duplicate identity");
            state.Phases = root.GetProperty("phases").EnumerateArray().Select(v => v.GetString()!).ToList();
            if (state.Phases.Count == 0 || state.Phases.Any(string.IsNullOrWhiteSpace) || state.Phases.Distinct().Count() != state.Phases.Count) Fail("$.phases: empty or duplicate phase");
            state.Phase = state.Phases[0];
            foreach (var item in root.GetProperty("entities").EnumerateArray())
            {
                var entity = new Entity { Id = Text(item, "id") };
                var components = item.GetProperty("components").EnumerateArray().ToArray();
                if (components.Select(c => Text(c, "type")).Distinct().Count() != components.Length) Fail($"$.entities[{entity.Id}].components: duplicate type");
                foreach (var component in components)
                {
                    var data = component.GetProperty("data");
                    switch (Text(component, "type"))
                    {
                        case "owner": entity.Owner = Text(data, "seat"); break;
                        case "visibility": entity.Visibility = Text(data, "mode"); break;
                        case "container":
                            if(entity.IsContainer) Fail($"{entity.Id}: duplicate container capability");
                            entity.IsContainer = true; entity.Capacity = data.GetProperty("capacity").GetInt32(); entity.Ordered = data.GetProperty("ordered").GetBoolean();
                            entity.Contents = data.GetProperty("contents").EnumerateArray().Select(v => v.GetString()!).ToList(); break;
                        case "faces":
                            entity.Face = data.TryGetProperty("index",out var faceIndex)?faceIndex.GetInt32():0;
                            entity.Faces = data.GetProperty("names").EnumerateArray().Select(v => v.GetString()!).ToList(); entity.FaceUp = data.GetProperty("up").GetBoolean();
                            entity.FaceResources=data.GetProperty("resources").EnumerateArray().Select(v=>v.GetString()!).ToList();
                            foreach (var asset in data.GetProperty("resources").EnumerateArray())
                                try {ImageReference.Parse(asset.GetString()!).Validate(resourceData);}
                                catch(InvalidDataException error) {throw new InvalidDataException($"$.entities[{entity.Id}].faces: {error.Message}",error);}
                            break;
                        case "position": entity.X = data.GetProperty("x").GetDouble(); entity.Y = data.GetProperty("y").GetDouble(); break;
                        case "properties": foreach (var property in data.EnumerateObject()) entity.Properties.Add(property.Name, property.Value.GetInt32()); break;
                        case "counter":
                            var name=Text(data,"property"); entity.CounterBounds.Add(name,new(data.GetProperty("min").GetInt32(),data.GetProperty("max").GetInt32())); break;
                        case "dice":
                            entity.DiceFaces=data.GetProperty("values").EnumerateArray().Select(v=>v.GetInt32()).ToList();
                            if(entity.DiceFaces.Count==0) Fail($"{entity.Id}.dice: empty values"); break;
                        default:
                            var type=Text(component,"type");
                            var plugin=plugins.FirstOrDefault(p=>type=="wasm."+p.Id || type=="wasm."+p.Id+".container");
                            if(plugin!=null)
                            {
                                if(type.EndsWith(".container",StringComparison.Ordinal) && plugin.Permissions.Contains("container"))
                                {
                                    if(entity.IsContainer) Fail($"{entity.Id}: duplicate container capability");
                                    entity.IsContainer=true;entity.Capacity=data.GetProperty("capacity").GetInt32();entity.Ordered=data.GetProperty("ordered").GetBoolean();
                                    entity.Contents=data.GetProperty("contents").EnumerateArray().Select(v=>v.GetString()!).ToList();
                                }
                                else if(type=="wasm."+plugin.Id && plugin.Permissions.Contains("component"))
                                {
                                    entity.Properties.Add(type,data.GetProperty("value").GetInt32());
                                    entity.CounterBounds.Add(type,new(data.GetProperty("min").GetInt32(),data.GetProperty("max").GetInt32()));
                                }
                                else Fail($"{entity.Id}: WASM component permission missing");
                                break;
                            }
                            if (!component.TryGetProperty("required", out var required) || required.GetBoolean()) Fail($"$.entities[{entity.Id}].components: unknown required component {Text(component, "type")}");
                            break;
                    }
                }
                if (!state.Entities.TryAdd(entity.Id, entity)) Fail($"$.entities[{entity.Id}]: duplicate identity");
            }
            foreach (var entity in state.Entities.Values)
            {
                if (entity.Owner != null && !state.Seats.Any(s => s.Id == entity.Owner)) Fail($"{entity.Id}.owner: unknown seat");
                if (entity.Visibility is not ("public" or "owner" or "hidden")) Fail($"{entity.Id}.visibility: invalid mode");
                if (entity.Visibility == "owner" && entity.Owner == null) Fail($"{entity.Id}.owner: required for owner visibility");
                if (!double.IsFinite(entity.X) || !double.IsFinite(entity.Y)) Fail($"{entity.Id}.position: invalid coordinates");
                if (entity.Capacity < entity.Contents.Count || entity.Capacity < 0) Fail($"{entity.Id}.capacity: exceeded");
                foreach (var child in entity.Contents)
                {
                    if (!state.Entities.ContainsKey(child)) Fail($"{entity.Id}.contents: invalid reference {child}");
                    var value = state.Entities[child];
                    if (value.Container != null) Fail($"{child}.container: multiple membership");
                    value.Container = entity.Id;
                }
            }
            foreach (var entity in state.Entities.Values)
            {
                var visited = new HashSet<string> { entity.Id };
                for (var parent = entity.Container; parent != null; parent = state.Entities[parent].Container)
                    if (!visited.Add(parent)) Fail($"{entity.Id}.container: cycle");
            }
            StateValidation.Validate(state);
            var module = new LoadedModule(id,version,"",state,rules,cpu,rulesPath,cpuPath,resources,resourceData,runtimeHashes,plugins.Count==0?null:plugins);
            return module with { Fingerprint=ModuleSnapshot.Fingerprint(module) };
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or IOException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        { throw new InvalidDataException($"{file}: {error.Message}", error); }
    }
    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() is { Length: > 0 } text ? text : throw new InvalidDataException($"{key}: required text");
    private static void Fail(string message) => throw new InvalidDataException(message);
    private static void CheckDuplicates(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>();
            foreach (var p in element.EnumerateObject())
            {
                if (!names.Add(p.Name)) Fail($"{path}.{p.Name}: duplicate key");
                CheckDuplicates(p.Value, path + "." + p.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        { var i = 0; foreach (var item in element.EnumerateArray()) CheckDuplicates(item, $"{path}[{i++}]"); }
    }
    public static string SafeFile(string directory, string relative)
    {
        ModulePackage.ValidatePath(relative);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); var path = Path.GetFullPath(Path.Combine(root, relative));
        if((File.GetAttributes(root) & FileAttributes.ReparsePoint)!=0) Fail("Linked module roots are unsupported");
        if (Path.IsPathRooted(relative) || !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Fail($"{relative}: path escapes module");
        if (!File.Exists(path)) Fail($"{relative}: missing file");
        if(new FileInfo(path).Length>ModuleContentLimits.MaxFileBytes) Fail($"{relative}: file size budget exceeded");
        for (FileSystemInfo? current = new FileInfo(path); current != null && current.FullName != root; current = Directory.GetParent(current.FullName))
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) Fail($"{relative}: linked paths are unsupported");
        return path;
    }
}
