using BoardGame.Kernel;
namespace BoardGame.Modules;
public static class ModuleSnapshot
{
    public static string Fingerprint(LoadedModule module) => Wire.Hash(Wire.Json(new {
        id=module.Id,version=module.Version,state=module.Initial,rules=module.Rules,cpu=module.Cpu,
        resources=module.ResourceHashes ?? new SortedDictionary<string,string>(StringComparer.Ordinal) }));
    public static LoadedModule Load(string path)
    {
        try
        {
            var module=Wire.Read<LoadedModule>(File.ReadAllText(path));
            if(string.IsNullOrWhiteSpace(module.Id) || string.IsNullOrWhiteSpace(module.Version) || module.Initial==null || module.Rules==null || module.Cpu==null) throw new InvalidDataException("Missing module metadata.");
            StateValidation.Validate(module.Initial);
            if(module.Fingerprint!=Fingerprint(module)) throw new InvalidDataException("Module snapshot content fingerprint mismatch.");
            return module;
        }
        catch(Exception e) when(e is not OutOfMemoryException) { throw new InvalidDataException($"{path}: {e.Message}",e); }
    }
}
