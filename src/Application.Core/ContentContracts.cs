using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.AppRuntime;

/// <summary>A loader only reads bounded content. It never starts a simulation or creates a device.</summary>
public interface IContentLoader
{
    string PayloadFormat {get;}
    int PayloadVersion {get;}
    JsonElement Load(IContentSource source);
    JsonElement Load(IContentSource source,RuntimeStartup startup)=>Load(source);
}
public interface IContentSource
{
    ReadOnlyMemory<byte> Read(string path);
    IReadOnlyList<string> Enumerate(string path);
}
public sealed record ContentPayload(string Format,int Version,JsonElement Data);
public sealed record ContentIdentity(int Format,RuntimeStartup Startup,string DefinitionSource,SortedDictionary<string,string> Files);
public sealed record ContentSnapshot(string Kind,int Format,string? Preset,ContentIdentity Identity,ContentPayload Payload,
    SortedDictionary<string,byte[]> Files,string Fingerprint);

/// <summary>A trusted installation catalog, never supplied by game content.</summary>
public sealed class ContentCatalog(IEnumerable<RuntimePluginDefinition> plugins)
{
    public IReadOnlyList<RuntimePluginDefinition> Plugins {get;}=Array.AsReadOnly(plugins.ToArray());
}

public sealed class GameContent
{
    private readonly ContentSnapshot snapshot;
    internal GameContent(ContentSnapshot snapshot)=>this.snapshot=ContentJson.Copy(snapshot);
    public string Fingerprint=>snapshot.Fingerprint;
    public ContentIdentity Identity=>ContentJson.Copy(snapshot.Identity);
    public ContentPayload Payload=>snapshot.Payload with {Data=snapshot.Payload.Data.Clone()};
    public ReadOnlyMemory<byte> File(string path)=>snapshot.Files.TryGetValue(path,out var data)?data.ToArray():throw new InvalidDataException(path+": undeclared content");
    public ContentSnapshot ExportSnapshot()=>ContentJson.Copy(snapshot);
    public void SaveSnapshot(string destination)=>System.IO.File.WriteAllText(destination,ContentJson.Write(snapshot));
    internal ContentSnapshot Snapshot=>snapshot;
}
