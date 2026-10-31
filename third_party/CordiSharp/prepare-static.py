"""Create an isolated, reviewable NativeAOT candidate from fixed CordiSharp source.

Usage: python patch-cordisharp.py <extracted upstream root> <new candidate folder>
No production files or NuGet cache are changed. Source revision is checked by the caller.
"""
from pathlib import Path
import shutil
import sys
import difflib
import hashlib
import json

upstream = Path(sys.argv[1]).resolve()
target = Path(sys.argv[2]).resolve()
if target.exists():
    raise SystemExit(f"Candidate directory already exists: {target}")
shutil.copytree(upstream / "src/CordiSharp", target / "src")
if (upstream / "LICENSE").exists():
    shutil.copy2(upstream / "LICENSE", target / "LICENSE")
else:
    (target / "UPSTREAM.txt").write_text("CordiSharp 0.2.0, commit 7b6cde4f43013ae4ff026b5f7bea5e5f9dd9300f.\nNuGet license expression: MIT; copyright: CordiSharp Contributors.\nThe pinned source archive contains no standalone LICENSE file.\n", encoding="utf-8")

def replace(name, before, after):
    path = target / "src" / name
    text = path.read_text(encoding="utf-8-sig")
    if before not in text:
        raise SystemExit(f"Upstream source changed: {name}: {before[:100]}")
    path.write_text(text.replace(before, after), encoding="utf-8")

attributes = '''[System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Dynamic CordiSharp plugin shapes require a JIT host.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Dynamic CordiSharp plugin shapes require reflection metadata.")]
    '''

for name in ("Events/EventsService.cs", "ReflectService.cs"):
    for value in (("name",) if name.startswith("Events") else ("name", "source")):
        replace(name, f"JsonSerializer.Serialize({value})", f"CordiSharp.Internal.StaticJson.Quote({value})")

(target / "src/Internal/StaticJson.cs").write_text('''using System.Text.Json;
using System.Text.Json.Serialization;
namespace CordiSharp.Internal;
internal static class StaticJson
{
    internal static string Quote(string? value) => JsonSerializer.Serialize(value, StringJsonContext.Default.String);
}
[JsonSerializable(typeof(string))]
internal partial class StringJsonContext : JsonSerializerContext;
''', encoding="utf-8")

replace("Registry/RegistryService.cs", "case Type type:", "case Type type when System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported:")
replace("Registry/RegistryService.cs", "case Delegate d:", '''case Func<Context, object?, object?> callback when !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported:
                key = callback;
                runtime = new PluginRuntime { Name = callback.Method.Name.StartsWith('<') ? null : callback.Method.Name, Callback = callback };
                return true;
            case Action<Context, object?> callback when !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported:
                key = callback;
                runtime = new PluginRuntime { Name = callback.Method.Name.StartsWith('<') ? null : callback.Method.Name, Callback = (ctx, config) => { callback(ctx, config); return null; } };
                return true;
            case Delegate d when System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported:''')
replace("Registry/RegistryService.cs", "return TryResolveObjectPlugin(plugin, out key, out runtime);", '''if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported) return TryResolveObjectPlugin(plugin, out key, out runtime);
                throw new NotSupportedException("NativeAOT registration requires Inject, IPluginObject, Func<Context, object?, object?> or Action<Context, object?>. Type plugins, other delegate signatures and structural apply objects require a JIT host.");''')
for signature in ("private static PluginRuntime BuildRuntimeFromType", "private static PluginRuntime BuildRuntimeFromDelegate", "private static bool TryResolveObjectPlugin"):
    replace("Registry/RegistryService.cs", signature, attributes + signature)

replace("Fiber.cs", '''            var resultProp = task.GetType().GetProperty("Result");
            if (resultProp is not null) return resultProp.GetValue(task);''', '''            if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported) return ReadDynamicTaskResult(task);
            throw new NotSupportedException("NativeAOT effect setup requires Task or Task<object?>; normalize typed results before registration.");''')
replace("Fiber.cs", "    // ---- state machine (ports cordis _getState/_updateState/_setEpoch/_refresh/_checkImpl) ----", '''    ''' + attributes + '''private static object? ReadDynamicTaskResult(Task task) => task.GetType().GetProperty("Result")?.GetValue(task);

    // ---- state machine (ports cordis _getState/_updateState/_setEpoch/_refresh/_checkImpl) ----''')

replace("Logger/LoggerService.cs", '''        var prop = config.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(config);''', '''        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported) return GetDynamicProperty(config, key);
        throw new NotSupportedException("NativeAOT logger interception requires dictionary configuration.");''')
replace("Logger/LoggerService.cs", "    private static string Hyphenate(string name)", "    " + attributes + '''private static object? GetDynamicProperty(object config, string key) => config.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance)?.GetValue(config);

    private static string Hyphenate(string name)''')

(target / "CordiSharp.Patched.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>CordiSharp</AssemblyName>
    <RootNamespace>CordiSharp</RootNamespace>
    <Version>0.2.0</Version>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup><Compile Include="src/**/*.cs" Exclude="src/obj/**/*.cs;src/bin/**/*.cs" /></ItemGroup>
</Project>
''', encoding="utf-8")
print(target / "CordiSharp.Patched.csproj")

diffs = []
manifest = []
for path in sorted((target / "src").rglob("*.cs")):
    relative = path.relative_to(target / "src")
    original = upstream / "src/CordiSharp" / relative
    before = original.read_text(encoding="utf-8-sig") if original.exists() else ""
    after = path.read_text(encoding="utf-8-sig")
    if before != after:
        diffs.extend(difflib.unified_diff(before.splitlines(keepends=True), after.splitlines(keepends=True), n=0,
            fromfile="a/src/CordiSharp/" + relative.as_posix(), tofile="b/src/CordiSharp/" + relative.as_posix()))
        manifest.append({"file": relative.as_posix(), "originalSha256": hashlib.sha256(original.read_bytes()).hexdigest() if original.exists() else None,
            "patchedSha256": hashlib.sha256(path.read_bytes()).hexdigest()})
(target / "CordiSharp.patch").write_text("".join(diffs), encoding="utf-8")
(target / "changed-files.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
