using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BoardGame.Runtime;

namespace BoardGame.Plugins;

public sealed record PluginCapability(string Name,int Api);
public sealed record PluginFile(string Path,string Sha256,string Role);
public sealed record PluginEntry(string Assembly,string Type,string Method);
public sealed record PluginAot(bool Compatible,string[] Targets,string Reason);

/// <summary>Publisher metadata, never an instruction to activate code from game content.
/// A true AOT declaration admits a candidate to analysis; it is not verification.</summary>
public sealed record PluginManifest(int Format,string Kind,string Id,string Version,int Api,
    PluginEntry Entry,string[] Targets,PluginAot? Aot,PluginCapability[] Provides,
    PluginCapability[] Requires,PluginExecution Execution,PluginFile[] Files)
{
    public const string FileName="runtime.plugin.json";
    // Needed only for validating dormant graphical declarations without linking their implementation.
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string[]? ContentFields {get;init;}
    public static PluginManifest Read(string json)
    {
        try
        {
            using var document=JsonDocument.Parse(json,new(){MaxDepth=32});
            RejectDuplicates(document.RootElement);
            var manifest=JsonSerializer.Deserialize(json,ManifestJson.Default.PluginManifest)
                ?? throw new InvalidDataException("Empty runtime plugin manifest");
            manifest.Validate();return manifest;
        }
        catch(JsonException error){throw new InvalidDataException("Invalid runtime plugin manifest: "+error.Message,error);}
    }
    public string Write(){Validate();return JsonSerializer.Serialize(this,ManifestJson.Default.PluginManifest);}
    [JsonIgnore] public string Fingerprint=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Write())));
    public void Validate()
    {
        if(Format!=1 || Kind!="runtime" || !Name(Id) || string.IsNullOrWhiteSpace(Version) || Version.Length>80 || Api!=1)
            throw new InvalidDataException("Unsupported runtime plugin identity, kind, format or API");
        if(Entry==null || !QualifiedName(Entry.Type) || !Identifier(Entry.Method))throw new InvalidDataException(Id+": invalid static factory entry");
        ContentPath.Validate(Entry.Assembly);
        if(Targets==null || Targets.Length==0 || Targets.Any(t=>!Name(t)) || Targets.Distinct(StringComparer.Ordinal).Count()!=Targets.Length)
            throw new InvalidDataException(Id+": invalid target platforms");
        if(Aot!=null && (Aot.Targets==null || Aot.Targets.Any(t=>!Targets.Contains(t,StringComparer.Ordinal)) ||
            Aot.Targets.Distinct(StringComparer.Ordinal).Count()!=Aot.Targets.Length || Aot.Reason==null ||
            Aot.Compatible && Aot.Targets.Length==0 || !Aot.Compatible && string.IsNullOrWhiteSpace(Aot.Reason)))
            throw new InvalidDataException(Id+": invalid AOT declaration");
        ValidateCapabilities(Provides,"provided");ValidateCapabilities(Requires,"required");
        if(ContentFields!=null && (ContentFields.Any(string.IsNullOrWhiteSpace) || ContentFields.Distinct(StringComparer.Ordinal).Count()!=ContentFields.Length))
            throw new InvalidDataException(Id+": invalid content configuration fields");
        if(Execution==null || Execution.Modes==0 || (Execution.Modes & ~HostMode.All)!=0 || !Enum.IsDefined(Execution.Scope))
            throw new InvalidDataException(Id+": invalid execution scope/mode");
        if(Files==null || Files.Length==0 || Files.Length>2048)throw new InvalidDataException(Id+": invalid package files");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in Files)
        {
            if(file==null)throw new InvalidDataException(Id+": null package file");
            ContentPath.Validate(file.Path);
            if(!paths.Add(file.Path) || file.Path.Equals(FileName,StringComparison.OrdinalIgnoreCase) ||
                file.Role is not ("implementation" or "reference" or "managed" or "native" or "content") ||
                file.Sha256==null || !Regex.IsMatch(file.Sha256,"\\A[0-9a-f]{64}\\z",RegexOptions.CultureInvariant))
                throw new InvalidDataException(Id+": invalid/duplicate package file "+file.Path);
        }
        if(!Files.Any(f=>f.Path==Entry.Assembly && f.Role=="implementation"))
            throw new InvalidDataException(Id+": static entry must name an implementation, not a reference assembly");
        // Cross-plugin edges are checked against the selected set, not in isolation.
        var contributions=Execution.Contributions ?? [];
        if(contributions.Any(c=>c==null || (c.Before ?? []).Concat(c.After ?? []).Any(e=>string.IsNullOrWhiteSpace(e) || e.Split('/').Length!=2)))
            throw new InvalidDataException(Id+": invalid phase ordering edge");
        _=PhaseOrder.Sort(contributions.Select(c=>new ScheduledContribution(Id,Execution.Scope,c with {Before=null,After=null})));
    }
    internal RuntimePluginDefinition Definition()=>new(Id,Version,Api,Provides.Select(c=>c.Name).ToArray(),[],_=>{}) {Execution=Execution};
    internal static BoardGame.Kernel.RuntimeStartup Startup(IEnumerable<PluginManifest> manifests)=>new(1,"plugin.validation","1",
        manifests.Select(m=>new BoardGame.Kernel.RuntimePluginSelection(m.Id,m.Version,m.Api,JsonSerializer.SerializeToElement(new Dictionary<string,string>(),ManifestJson.Default.DictionaryStringString))).ToList());
    private void ValidateCapabilities(PluginCapability[]? capabilities,string kind)
    {
        if(capabilities==null || capabilities.Any(c=>c==null || !Name(c.Name) || c.Api<1) || capabilities.Select(c=>c.Name).Distinct(StringComparer.Ordinal).Count()!=capabilities.Length)
            throw new InvalidDataException(Id+": invalid "+kind+" capabilities");
    }
    private static bool Name(string? value)=>value!=null && Regex.IsMatch(value,"\\A[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}\\z",RegexOptions.CultureInvariant);
    private static bool Identifier(string? value)=>value!=null && Regex.IsMatch(value,"\\A[A-Za-z_][A-Za-z0-9_]*\\z",RegexOptions.CultureInvariant);
    private static bool QualifiedName(string? value)=>value!=null && value.Length<=512 && value.Split('.').All(Identifier);
    private static void RejectDuplicates(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in value.EnumerateObject())
            {if(!names.Add(property.Name))throw new InvalidDataException("Duplicate manifest field: "+property.Name);RejectDuplicates(property.Value);}
        }
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())RejectDuplicates(item);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,WriteIndented=true,
    UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter=true)]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(Dictionary<string,string>))]
internal partial class ManifestJson : JsonSerializerContext;
