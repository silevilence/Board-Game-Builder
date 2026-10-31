using BoardGame.Kernel;
namespace BoardGame.Modules;
public static class ModuleSnapshot
{
    public static string Fingerprint(LoadedModule module) => Wire.Hash(Wire.Json(new ModuleFingerprintInput(
        module.Id,module.Version,module.Initial,module.Rules,module.Cpu,
        module.ResourceHashes ?? new SortedDictionary<string,string>(StringComparer.Ordinal),module.RuntimeHashes),Wire.Type<ModuleFingerprintInput>(ModuleJson.Default))+(module.RuntimePlugins==null?"":Wire.Json(module.RuntimePlugins))+
        (module.RuntimeIdentity==null?"":Canonical(Wire.Json(module.RuntimeIdentity)))+
        (module.Resources==null?"":Canonical(Wire.Json(module.Resources))));
    private static string Canonical(string json)
    {
        using var document=System.Text.Json.JsonDocument.Parse(json);
        using var buffer=new MemoryStream();
        using(var writer=new System.Text.Json.Utf8JsonWriter(buffer)) Write(document.RootElement,writer);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        static void Write(System.Text.Json.JsonElement value,System.Text.Json.Utf8JsonWriter writer)
        {
            if(value.ValueKind==System.Text.Json.JsonValueKind.Object)
            {
                writer.WriteStartObject();foreach(var p in value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal)) {writer.WritePropertyName(p.Name);Write(p.Value,writer);}writer.WriteEndObject();
            }
            else if(value.ValueKind==System.Text.Json.JsonValueKind.Array)
            {writer.WriteStartArray();foreach(var item in value.EnumerateArray()) Write(item,writer);writer.WriteEndArray();}
            else value.WriteTo(writer);
        }
    }
    public static LoadedModule Load(string path)
    {
        try
        {
            var module=Wire.Read<LoadedModule>(File.ReadAllText(path));
            if(string.IsNullOrWhiteSpace(module.Id) || string.IsNullOrWhiteSpace(module.Version) || module.Initial==null || module.Rules==null || module.Cpu==null) throw new InvalidDataException("Missing module metadata.");
            StateValidation.Validate(module.Initial);
            foreach(var resource in module.ResourceHashes ?? [])
                if(module.ResourceData==null || !module.ResourceData.TryGetValue(resource.Key,out var data) || ModuleHash.Compute(data)!=resource.Value)
                    throw new InvalidDataException($"resources[{resource.Key}]: missing or content hash mismatch");
            if(module.Fingerprint!=Fingerprint(module)) throw new InvalidDataException("Module snapshot content fingerprint mismatch.");
            return module;
        }
        catch(Exception e) when(e is not OutOfMemoryException) { throw new InvalidDataException($"{path}: {e.Message}",e); }
    }
}
