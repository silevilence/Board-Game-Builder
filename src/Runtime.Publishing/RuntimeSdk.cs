using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Plugins;
using BoardGame.Runtime;

namespace BoardGame.Publishing;

public sealed record NativeHostProfile(string Id,string EntryType,string EntryMethod,string[] Payloads,string[] RequiredCapabilities,PluginFile[] Files);
public sealed record NativeSdkManifest(int Format,string Target,NativeHostProfile[] Hosts,string[] Packages);

/// <summary>Verified maintainer runtime distribution. Reading it does not load publisher assemblies.</summary>
public sealed class RuntimeSdk
{
    private static readonly JsonSerializerOptions json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
    private readonly string root;
    private readonly NativeSdkManifest manifest;
    private readonly AvailablePlugin[] plugins;
    public IReadOnlyList<NativeHostProfile> Hosts=>manifest.Hosts.Select(h=>h with {Payloads=h.Payloads.ToArray(),RequiredCapabilities=h.RequiredCapabilities.ToArray(),Files=h.Files.ToArray()}).ToArray();
    public IReadOnlyList<AvailablePlugin> Plugins=>plugins.Select(p=>p with {Input=p.Input with {Manifest=PluginManifest.Read(p.Input.Manifest.Write())}}).ToArray();
    public string Target=>manifest.Target;
    private RuntimeSdk(string root,NativeSdkManifest manifest,AvailablePlugin[] plugins){this.root=root;this.manifest=manifest;this.plugins=plugins;}
    public static RuntimeSdk Open(string directory)
    {
        var root=Path.GetFullPath(directory);
        var manifest=JsonSerializer.Deserialize<NativeSdkManifest>(System.IO.File.ReadAllText(Path.Combine(root,"native.sdk.json")),json)
            ?? throw new InvalidDataException("Missing native runtime SDK manifest");
        if(manifest.Format!=1 || manifest.Target!="win-x64" || manifest.Hosts is not {Length:>0 and <=64} || manifest.Hosts.Any(h=>h==null) || manifest.Packages==null || manifest.Packages.Length>2048 ||
            manifest.Packages.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=manifest.Packages.Length ||
            manifest.Hosts.Select(h=>h.Id).Distinct(StringComparer.Ordinal).Count()!=manifest.Hosts.Length)
            throw new InvalidDataException("Unsupported native runtime SDK identity or target");
        foreach(var host in manifest.Hosts)
        {
            if(string.IsNullOrWhiteSpace(host.Id) || string.IsNullOrWhiteSpace(host.EntryType) || string.IsNullOrWhiteSpace(host.EntryMethod) || host.Payloads==null || host.RequiredCapabilities==null || host.Files is not {Length:>0 and <=2048} || host.Files.Any(f=>f==null))throw new InvalidDataException("Invalid native host profile");
            foreach(var file in host.Files)PublicationPlanner.Verify(File(root,file,host.Id));
        }
        var plugins=manifest.Packages.Select(path=>
        {
            ContentPath.Validate(path);var folder=Path.Combine(root,path);
            var plugin=PluginManifest.Read(System.IO.File.ReadAllText(Path.Combine(folder,PluginManifest.FileName)));
            foreach(var file in plugin.Files)PublicationPlanner.Verify(File(folder,file,plugin.Id));
            return new AvailablePlugin(new(plugin,folder),true);
        }).ToArray();
        return new(root,manifest,plugins);
    }
    public PublicationPlan Plan(GameContent content,string hostId,IEnumerable<RuntimePluginSelection> hostSelections,IEnumerable<AvailablePlugin>? external=null)
    {
        var host=manifest.Hosts.SingleOrDefault(h=>h.Id==hostId) ?? throw new InvalidDataException("Unavailable native host: "+hostId);
        if(host.Payloads.Length>0 && !host.Payloads.Contains(content.Payload.Format,StringComparer.Ordinal))throw new InvalidDataException(hostId+": unsupported content payload "+content.Payload.Format);
        var plan=PublicationPlanner.Analyze(content,plugins.Concat(external ?? []),hostSelections,host.Files.Select(f=>File(root,f,host.Id)),manifest.Target,HostMode.Graphical);
        var capabilities=plan.Selected.SelectMany(p=>p.Manifest.Provides).Select(p=>p.Name).ToHashSet(StringComparer.Ordinal);
        foreach(var required in host.RequiredCapabilities)if(!capabilities.Contains(required))throw new InvalidDataException(hostId+": missing native host capability "+required);
        return plan;
    }
    public NativeEntry Entry(string hostId,string title,string version,byte[]? icon=null)
    {var host=manifest.Hosts.Single(h=>h.Id==hostId);return new(host.EntryType,host.EntryMethod,title,version,icon);}
    public static void WriteManifest(string directory,NativeSdkManifest manifest)=>System.IO.File.WriteAllText(Path.Combine(directory,"native.sdk.json"),JsonSerializer.Serialize(manifest,json));
    private static PublicationFile File(string directory,PluginFile file,string owner)
    {ContentPath.Validate(file.Path);return new(Path.Combine(directory,file.Path),Path.GetFileName(file.Path),file.Sha256,file.Role,owner);}
}
