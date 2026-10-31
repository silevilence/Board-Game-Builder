using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Xml.Linq;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Plugins;
using BoardGame.Runtime;

namespace BoardGame.Publishing;

public sealed record AvailablePlugin(PluginBuildInput Input,bool Builtin=false);
public sealed record PublicationFile(string Source,string Name,string Sha256,string Role,string Owner);
public sealed record PublicationPlan(string Target,HostMode Mode,string SourceFingerprint,GameContent Content,
    IReadOnlyList<PluginBuildInput> Selected,IReadOnlyList<PublicationFile> Files,StaticRegistrationResult Registration);

public static class PublicationPlanner
{
    /// <summary>Content is the complete validated snapshot, including every retained definition preset.
    /// Selection comes from declarations, never from update/render execution or a recorded playthrough.</summary>
    public static PublicationPlan Analyze(GameContent content,IEnumerable<AvailablePlugin> available,
        IEnumerable<RuntimePluginSelection> hostSelections,IEnumerable<PublicationFile> sharedRuntime,string target,HostMode mode)
    {
        if(mode is not (HostMode.Graphical or HostMode.Headless))throw new ArgumentOutOfRangeException(nameof(mode));
        var installed=available.ToArray();var selected=new List<PluginBuildInput>();
        var selections=content.Identity.Startup.Plugins.Concat(hostSelections).ToArray();
        if(selections.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()!=selections.Length)
            throw new InvalidDataException("publication.identity: duplicate/conflicting game and host selection");
        foreach(var selection in selections)
        {
            var matches=installed.Where(p=>p.Input.Manifest.Id==selection.Id && p.Input.Manifest.Version==selection.Version && p.Input.Manifest.Api==selection.Api).ToArray();
            if(matches.Length!=1)throw new InvalidDataException(selection.Id+": missing or ambiguous exact implementation "+selection.Version);
            var match=matches[0];var manifest=PluginManifest.Read(match.Input.Manifest.Write());
            if(selection.Implementation!=manifest.Fingerprint && !(match.Builtin && selection.Implementation==null))
                throw new InvalidDataException(selection.Id+": implementation content identity mismatch");
            if(match.Builtin && manifest.Id is "wasm" or "lua.rules" or "lua.cpu" or "flow")
            {
                var supported=BuiltinScriptSupport.Declaration(manifest.Id,manifest.Version,manifest.Api);
                if(supported!=manifest.Aot && (manifest.Aot?.Compatible!=supported.Compatible || !(manifest.Aot?.Targets ?? []).SequenceEqual(supported.Targets)))
                    throw new InvalidDataException(manifest.Id+": declared AOT support differs from built-in evidence: "+supported.Reason);
                if(supported.Compatible)
                    foreach(var native in BuiltinScriptSupport.NativeFiles(manifest.Id,manifest.Version,manifest.Api,target,match.Input.Directory))
                        if(!manifest.Files.Contains(native))throw new InvalidDataException(manifest.Id+": native execution dependency missing from manifest");
            }
            selected.Add(new(manifest,Path.GetFullPath(match.Input.Directory)));
        }
        var compatibility=PluginCompatibility.Check(selected.Select(p=>p.Manifest),target,true,mode);
        if(!compatibility.Success)throw new InvalidDataException(string.Join(Environment.NewLine,compatibility.Diagnostics.Select(d=>d.Code+": "+string.Join(" -> ",d.Chain)+": "+d.Message)));
        if(mode==HostMode.Graphical && compatibility.Ordered.Count(m=>m.Provides.Any(c=>c.Name=="render.backend"))!=1)
            throw new InvalidDataException("publication.backend: graphical release requires exactly one render.backend provider");
        // Validate even inactive selections' identity/files; their implementation is not compiled in headless output.
        foreach(var input in selected)
            foreach(var file in input.Manifest.Files)Verify(new(Inside(input.Directory,file.Path),file.Path,file.Sha256,file.Role,input.Manifest.Id));
        var active=compatibility.Ordered.Select(m=>m.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var selection in selections.Where(s=>!active.Contains(s.Id)))
        {
            var manifest=selected.Single(p=>p.Manifest.Id==selection.Id).Manifest;
            if(selection.Configuration.EnumerateObject().Any(p=>!(manifest.ContentFields ?? []).Contains(p.Name,StringComparer.Ordinal)))
                throw new InvalidDataException(selection.Id+": inactive graphical configuration requires manifest contentFields metadata");
        }
        var files=sharedRuntime.Concat(selected.Where(p=>active.Contains(p.Manifest.Id)).SelectMany(p=>p.Manifest.Files
            .Where(f=>f.Role!="reference").Select(f=>new PublicationFile(Inside(p.Directory,f.Path),Path.GetFileName(f.Path),f.Sha256,f.Role,p.Manifest.Id)))).ToArray();
        if(files.Any(f=>f.Role=="reference"))throw new InvalidDataException("Reference assemblies must not be distributed as runtime implementations");
        foreach(var file in files)Verify(file);
        var output=new Dictionary<string,PublicationFile>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in files)
        {
            ContentPath.Validate(file.Name);
            if(file.Name.Contains('/'))throw new InvalidDataException("Runtime output file must have a single filename: "+file.Name);
            if(output.TryGetValue(file.Name,out var other) && (other.Sha256!=file.Sha256 || other.Role!=file.Role && (other.Role=="native" || file.Role=="native")))
                throw new InvalidDataException(file.Owner+": runtime output conflicts with "+other.Owner+": "+file.Name);
            output.TryAdd(file.Name,file);
        }
        ValidateClosure(output.Values,target);
        var registration=StaticRegistration.Generate(selected,target,mode);
        var references=output.Values.Where(f=>f.Role is "implementation" or "managed").Select(f=>f.Source).Order(StringComparer.Ordinal).ToArray();
        var items=new XElement("ItemGroup",references.Select(path=>new XElement("Reference",new XAttribute("Include",AssemblyName.GetAssemblyName(path).Name!),new XElement("HintPath",path),new XElement("Private","true"))));
        registration=registration with {References=references,NativeFiles=output.Values.Where(f=>f.Role=="native").Select(f=>f.Source).ToArray(),ProjectItems=items.ToString()};
        return new(target,mode,content.Fingerprint,content,selected.AsReadOnly(),output.Values.OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray(),registration);
    }
    public static void Verify(PublicationFile file)
    {
        if(file.Role is not ("implementation" or "managed" or "native" or "content" or "reference"))throw new InvalidDataException(file.Owner+": unknown runtime input role "+file.Role);
        var path=Path.GetFullPath(file.Source);
        try
        {
        for(var part=path;!string.IsNullOrEmpty(part);part=Path.GetDirectoryName(part))
            if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException(file.Owner+": runtime input contains a filesystem link: "+path);
        using var stream=File.OpenRead(path);
        if(Convert.ToHexStringLower(SHA256.HashData(stream))!=file.Sha256)throw new InvalidDataException(file.Owner+": runtime input content changed: "+file.Name);
        }
        catch(Exception error) when(error is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {throw new InvalidDataException(file.Owner+": missing/unreadable runtime input: "+file.Name,error);}
    }
    private static string Inside(string root,string relative)
    {
        ContentPath.Validate(relative);return Path.Combine(Path.GetFullPath(root),relative);
    }
    private static void ValidateClosure(IEnumerable<PublicationFile> files,string target)
    {
        var managed=new Dictionary<string,(AssemblyName Name,PublicationFile File)>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in files.Where(f=>f.Role is "implementation" or "managed" or "native"))
        {
            using var stream=File.OpenRead(file.Source);using var pe=new PEReader(stream);
            if(target=="win-x64" && (file.Role=="native"?pe.PEHeaders.CoffHeader.Machine!=Machine.Amd64:
                pe.PEHeaders.CoffHeader.Machine is not (Machine.Amd64 or Machine.I386) || pe.PEHeaders.CorHeader?.Flags.HasFlag(CorFlags.Requires32Bit)==true))
                throw new InvalidDataException(file.Owner+": wrong target architecture: "+file.Name);
            if(file.Role=="native")continue;
            if(!pe.HasMetadata)throw new InvalidDataException(file.Owner+": missing managed metadata: "+file.Name);
            var metadata=pe.GetMetadataReader();
            if(metadata.CustomAttributes.Any(handle=>
            {
                var attribute=metadata.GetCustomAttribute(handle);
                if(attribute.Constructor.Kind!=HandleKind.MemberReference)return false;
                var member=metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if(member.Parent.Kind!=HandleKind.TypeReference)return false;
                var type=metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                return metadata.GetString(type.Namespace)=="System.Runtime.CompilerServices" && metadata.GetString(type.Name)=="ReferenceAssemblyAttribute";
            }))throw new InvalidDataException(file.Owner+": reference assembly cannot be a runtime implementation: "+file.Name);
            var name=AssemblyName.GetAssemblyName(file.Source);
            if(Forbidden(name.Name!))throw new InvalidDataException(file.Owner+": author/editor implementation in runtime: "+name.Name);
            if(!managed.TryAdd(name.Name!,(name,file)))throw new InvalidDataException("Conflicting assembly identity: "+name.Name);
        }
        var framework=((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries)
            .Where(p=>string.Equals(Path.GetDirectoryName(p),Path.TrimEndingDirectorySeparator(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()),StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().Where(n=>n is "netstandard" or "mscorlib" or "Microsoft.CSharp" || n.StartsWith("System.",StringComparison.Ordinal)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var item in managed.Values)
        {
            using var stream=File.OpenRead(item.File.Source);using var pe=new PEReader(stream);var metadata=pe.GetMetadataReader();
            foreach(var reference in metadata.AssemblyReferences)
            {
                var identity=metadata.GetAssemblyReference(reference);var name=metadata.GetString(identity.Name);
                if(Forbidden(name))throw new InvalidDataException(item.File.Owner+": forbidden runtime reference "+name);
                if(framework.Contains(name))continue;
                if(!managed.TryGetValue(name,out var dependency) || dependency.Name.Version!=identity.Version)
                    throw new InvalidDataException(item.File.Owner+" -> "+item.Name.Name+" -> "+name+": missing/incompatible managed dependency "+identity.Version);
            }
        }
    }
    private static bool Forbidden(string name)=>name is "Editor" or "Editor.Core" or "Authoring" or "Runtime.Publishing" || name.StartsWith("CordiSharp.Plugins.Avalonia",StringComparison.Ordinal);
}
