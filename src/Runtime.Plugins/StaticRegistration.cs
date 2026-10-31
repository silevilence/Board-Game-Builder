using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BoardGame.Runtime;

namespace BoardGame.Plugins;

public sealed record PluginBuildInput(PluginManifest Manifest,string Directory);
public sealed record StaticRegistrationResult(string Source,IReadOnlyList<string> References,IReadOnlyList<string> NativeFiles,string ProjectItems);

public static class StaticRegistration
{
    /// <summary>Generate compile-time calls only for the selected, complete dependency set.
    /// Does not evaluate a factory, scan installed plugins, or infer usage from a playthrough.</summary>
    public static StaticRegistrationResult Generate(IEnumerable<PluginBuildInput> selected,string target,HostMode mode)
    {
        var inputs=selected.ToArray();var result=PluginCompatibility.Check(inputs.Select(i=>i.Manifest),target,true,mode);
        if(!result.Success)throw new InvalidDataException(string.Join(Environment.NewLine,result.Diagnostics.Select(d=>string.Join(" -> ",d.Chain)+": "+d.Message)));
        var references=new SortedDictionary<string,(string Path,string Hash)>(StringComparer.OrdinalIgnoreCase);
        var native=new List<string>();var source=new StringBuilder("// Generated from verified plugin manifests.\nusing BoardGame.AppRuntime;\nusing BoardGame.Plugins;\npublic static class GeneratedCatalog\n{\n    public static ApprovedApplicationPlugin[] Create() =>\n    [\n");
        foreach(var manifest in result.Ordered)
        {
            var input=inputs.Single(i=>i.Manifest.Id==manifest.Id);
            foreach(var file in manifest.Files)
            {
                var path=PluginStore.Inside(input.Directory,file.Path);
                using var stream=File.OpenRead(path);
                if(Convert.ToHexStringLower(SHA256.HashData(stream))!=file.Sha256)throw new InvalidDataException(manifest.Id+": build input content changed: "+file.Path);
                if(file.Role is "implementation" or "managed")
                {
                    var assembly=System.Reflection.AssemblyName.GetAssemblyName(path).Name!;
                    if(references.TryGetValue(assembly,out var prior) && prior.Hash!=file.Sha256)throw new InvalidDataException("Conflicting compile implementation: "+assembly);
                    references[assembly]=(path,file.Sha256);
                }
                if(file.Role=="native")native.Add(path);
            }
            source.Append("        PluginRegistration.Bind(PluginManifest.Read(").Append(Literal(manifest.Write()))
                .Append("), global::").Append(string.Join('.',manifest.Entry.Type.Split('.').Select(p=>"@"+p))).Append(".@").Append(manifest.Entry.Method).Append("()),\n");
        }
        source.Append("    ];\n}\n");
        var items=new XElement("ItemGroup",references.Select(r=>new XElement("Reference",new XAttribute("Include",r.Key),
            new XElement("HintPath",r.Value.Path),new XElement("Private","true"))));
        return new(source.ToString(),references.Values.Select(r=>r.Path).ToArray(),native.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),items.ToString());
    }
    private static string Literal(string text)=>"\""+text.Replace("\\","\\\\",StringComparison.Ordinal).Replace("\"","\\\"",StringComparison.Ordinal)
        .Replace("\r","\\r",StringComparison.Ordinal).Replace("\n","\\n",StringComparison.Ordinal).Replace("\t","\\t",StringComparison.Ordinal)+"\"";
}
