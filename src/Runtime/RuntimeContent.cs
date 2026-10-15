using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Modules;

namespace BoardGame.Runtime;

// Content conventions belong to the built-in script adapters, shared by loading, reload and export.
internal static class RuntimeContent
{
    public static bool NeedsLua(LoadedModule module)=>Scripts(module.RuntimeIdentity!.Startup).Any() ||
        module.FlowFiles!=null && FlowContent.Read(module.RuntimeIdentity.Startup,module.FlowFiles).Scripts.Count>0;
    public static IEnumerable<RuntimePluginSelection> Scripts(RuntimeStartup startup)=>
        startup.Plugins.Where(p=>p.Id is "lua.rules" or "lua.cpu");
    public static IEnumerable<string> ScriptFiles(RuntimeStartup startup)=>Scripts(startup).Select(p=>p.Configuration.GetProperty("file").GetString()!);
    public static List<RuntimeDependency> Extensions(RuntimeStartup startup)=>
        startup.Plugins.SingleOrDefault(p=>p.Id=="wasm")?.Configuration.GetProperty("extensions").Deserialize<List<RuntimeDependency>>(Wire.Options) ?? [];
    public static IEnumerable<string> Files(RuntimeStartup startup)=>ScriptFiles(startup).Concat(Extensions(startup).Select(p=>p.File!));
}
