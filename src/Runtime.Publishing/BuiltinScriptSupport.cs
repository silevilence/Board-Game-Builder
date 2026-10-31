using System.Security.Cryptography;
using BoardGame.Plugins;

namespace BoardGame.Publishing;

/// <summary>Maintainer evidence for built-in implementations; never inferred from language names.
/// External publishers supply their own declarations, which still undergo strict compilation.</summary>
public static class BuiltinScriptSupport
{
    public static PluginAot Declaration(string id,string version,int api)
    {
        if(api!=1 || version!="1")return new(false,[],"This built-in implementation version has no native validation evidence");
        return id switch
        {
            "wasm"=>new(true,["win-x64"],"Wasmtime 48.0.2 fixed i32 API; native execution, limits, traps and board replay verified. Wasmtime itself generates native guest code."),
            "lua.rules" or "lua.cpu"=>new(false,[],"MoonSharp 2.0.0 and its worker have unresolved NativeAOT/trimming paths; no dotnet fallback is permitted"),
            "flow"=>new(false,[],"Flow script/condition nodes transitively use MoonSharp 2.0.0; this flow implementation is not NativeAOT compatible even without lua.rules/lua.cpu selections"),
            _=>throw new ArgumentException("Not a built-in script implementation: "+id,nameof(id))
        };
    }
    public static PluginFile[] NativeFiles(string id,string version,int api,string target,string runtimeDirectory)
    {
        var declaration=Declaration(id,version,api);
        if(!declaration.Compatible || !declaration.Targets.Contains(target,StringComparer.Ordinal))
            throw new InvalidDataException(id+": "+declaration.Reason+"; target="+target);
        // There is no supported Lua worker to silently replace with a managed process.
        var file=Path.Combine(Path.GetFullPath(runtimeDirectory),"wasmtime.dll");
        if(!File.Exists(file))throw new InvalidDataException(id+": missing native runtime wasmtime.dll (48.0.2, win-x64)");
        using var stream=File.OpenRead(file);
        var hash=Convert.ToHexStringLower(SHA256.HashData(stream));
        if(hash!="19b3880fbb92f1826c58ac9f5e2a472692b5fc3d012a1a7a41983ccf77544607")
            throw new InvalidDataException(id+": wasmtime.dll differs from the verified 48.0.2 win-x64 binary");
        return [new("wasmtime.dll",hash,"native")];
    }
}
