using System.Text.Json;
using BoardGame.Kernel;
namespace BoardGame.Modules;
public static class DirectoryModule
{
    public static LoadedModule Load(string directory)
    {
        var file = Path.Combine(Path.GetFullPath(directory), "module.json");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            CheckDuplicates(root, "$");
            if (root.GetProperty("format").GetInt32() != 1) Fail("$.format: unsupported version");
            var id = Text(root, "id"); var version = Text(root, "version");
            var rulesPath = SafeFile(directory, Text(root, "rules"));
            var cpuPath = SafeFile(directory, Text(root, "cpu"));
            var rules = File.ReadAllText(rulesPath); var cpu = File.ReadAllText(cpuPath);
            var resources = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var resource in root.GetProperty("resources").EnumerateObject())
                resources.Add(resource.Name, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(SafeFile(directory, resource.Value.GetString()!)))));
            var state = new GameState();
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
                            entity.IsContainer = true; entity.Capacity = data.GetProperty("capacity").GetInt32(); entity.Ordered = data.GetProperty("ordered").GetBoolean();
                            entity.Contents = data.GetProperty("contents").EnumerateArray().Select(v => v.GetString()!).ToList(); break;
                        case "faces":
                            entity.Faces = data.GetProperty("names").EnumerateArray().Select(v => v.GetString()!).ToList(); entity.FaceUp = data.GetProperty("up").GetBoolean();
                            foreach (var asset in data.GetProperty("resources").EnumerateArray())
                                if (!resources.ContainsKey(asset.GetString()!)) Fail($"$.entities[{entity.Id}].faces: unknown resource");
                            break;
                        case "position": entity.X = data.GetProperty("x").GetDouble(); entity.Y = data.GetProperty("y").GetDouble(); break;
                        case "properties": foreach (var property in data.EnumerateObject()) entity.Properties.Add(property.Name, property.Value.GetInt32()); break;
                        default:
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
            var fingerprint = Wire.Hash(Wire.Json(new { id, version, state, rules, cpu, resources }));
            return new(id, version, fingerprint, state, rules, cpu, rulesPath, cpuPath);
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
    private static string SafeFile(string directory, string relative)
    {
        var root = Path.GetFullPath(directory); var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Fail($"{relative}: path escapes module");
        if (!File.Exists(path)) Fail($"{relative}: missing file");
        for (FileSystemInfo? current = new FileInfo(path); current != null && current.FullName != root; current = Directory.GetParent(current.FullName))
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) Fail($"{relative}: linked paths are unsupported");
        return path;
    }
}
