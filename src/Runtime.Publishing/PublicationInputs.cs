using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using BoardGame.AppRuntime;
using BoardGame.Kernel;

namespace BoardGame.Publishing;

public sealed record PreparedPublication(string Directory,string SourceFingerprint,string Registration,string ProjectItems,
    IReadOnlyList<PublicationFile> RuntimeFiles,IReadOnlyList<string> ContentFiles);

public static class PublicationInputs
{
    /// <summary>Copy verified immutable inputs into a new build directory; never modify author files or an existing output.</summary>
    public static async Task<PreparedPublication> Create(PublicationPlan plan,string destination,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        var root=Path.GetFullPath(destination);
        if(Directory.Exists(root) || File.Exists(root))throw new IOException("Build input directory already exists: "+root);
        Directory.CreateDirectory(root);
        try
        {
            var libraries=Path.Combine(root,"runtime");Directory.CreateDirectory(libraries);
            var copied=new List<PublicationFile>();
            foreach(var file in plan.Files)
            {
                cancellation.ThrowIfCancellationRequested();PublicationPlanner.Verify(file);
                BoardGame.Runtime.ContentPath.Validate(file.Name);
                if(file.Name.Contains('/'))throw new InvalidDataException("Runtime output must have a single filename: "+file.Name);
                var output=Path.Combine(libraries,file.Name);
                await using(var input=File.OpenRead(file.Source))
                await using(var target=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
                    await input.CopyToAsync(target,cancellation).ConfigureAwait(false);
                var next=file with {Source=output};PublicationPlanner.Verify(next);copied.Add(next);
            }
            var content=Path.Combine(root,"content");Directory.CreateDirectory(content);
            var snapshot=plan.Content.ExportSnapshot();
            // Only the temporary release copy is pinned. The source fingerprint remains in the build report.
            var startup=JsonSerializer.Deserialize(snapshot.Files[GameRuntime.Entry],PublicationJson.Default.RuntimeStartup)
                ?? throw new InvalidDataException("Missing release entry");
            startup=startup with {Plugins=startup.Plugins.Select(p=>p with {Implementation=plan.Selected.Single(i=>i.Manifest.Id==p.Id).Manifest.Fingerprint}).ToList()};
            snapshot.Files[GameRuntime.Entry]=JsonSerializer.SerializeToUtf8Bytes(startup,PublicationJson.Default.RuntimeStartup);
            foreach(var file in snapshot.Files)
            {
                cancellation.ThrowIfCancellationRequested();BoardGame.Runtime.ContentPath.Validate(file.Key);
                var output=Path.Combine(content,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllBytesAsync(output,file.Value,cancellation).ConfigureAwait(false);
            }
            var registration=Path.Combine(root,"GeneratedCatalog.cs");
            await File.WriteAllTextAsync(registration,plan.Registration.Source,cancellation).ConfigureAwait(false);
            var items=new XElement("ItemGroup",copied.Where(f=>f.Role is "implementation" or "managed").Select(f=>
                new XElement("Reference",new XAttribute("Include",System.Reflection.AssemblyName.GetAssemblyName(f.Source).Name!),
                    new XElement("HintPath","runtime/"+f.Name),new XElement("Private","true"))));
            var report=new InputReport(plan.Target,plan.Mode.ToString(),plan.SourceFingerprint,
                plan.Selected.Select(i=>new InputPlugin(i.Manifest.Id,i.Manifest.Version,i.Manifest.Fingerprint)).ToArray(),
                copied.Select(f=>new InputFile("runtime/"+f.Name,f.Sha256,f.Role,f.Owner)).ToArray(),
                snapshot.Files.Select(p=>new InputFile("content/"+p.Key,Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(p.Value)),"content","game")).ToArray());
            await File.WriteAllTextAsync(Path.Combine(root,"build-inputs.json"),JsonSerializer.Serialize(report,PublicationJson.Default.InputReport),cancellation).ConfigureAwait(false);
            return new(root,plan.SourceFingerprint,registration,items.ToString(),copied.AsReadOnly(),snapshot.Files.Keys.ToArray());
        }
        catch
        {
            // This call created the entire directory and never accepted an existing one.
            Directory.Delete(root,true);throw;
        }
    }
}
internal sealed record InputPlugin(string Id,string Version,string Fingerprint);
internal sealed record InputFile(string Path,string Sha256,string Role,string Owner);
internal sealed record InputReport(string Target,string Mode,string SourceFingerprint,InputPlugin[] Plugins,InputFile[] Runtime,InputFile[] Content);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(RuntimeStartup))]
[JsonSerializable(typeof(InputReport))]
internal partial class PublicationJson:JsonSerializerContext;
