using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using BoardGame.AppRuntime;
using BoardGame.Runtime;

namespace BoardGame.Plugins;

/// <summary>An explicitly managed installation, separate from game content and enablement.
/// Import is an ordinary .NET author/host operation. Native games use generated static calls.</summary>
public sealed class PluginStore
{
    private readonly string root,target;
    private readonly object gate=new();
    private readonly Dictionary<string,int> active=new(StringComparer.Ordinal);
    private readonly Assembly[] shared;
    private readonly PluginManifest[] builtins;
    private static readonly HashSet<string> platformAssemblies=((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
        .Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries)
        .Where(p=>string.Equals(Path.GetDirectoryName(p),Path.TrimEndingDirectorySeparator(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()),StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
    public PluginStore(string directory,string target,IEnumerable<Assembly>? sharedContracts=null,IEnumerable<PluginManifest>? builtinManifests=null)
    {
        root=Path.GetFullPath(directory);this.target=target;
        Directory.CreateDirectory(root);NoLinks(root);
        shared=new[]{typeof(ApprovedApplicationPlugin).Assembly,typeof(PluginExecution).Assembly,typeof(CordiSharp.Context).Assembly}
            .Concat(sharedContracts ?? []).Distinct().ToArray();
        builtins=(builtinManifests ?? []).Select(m=>PluginManifest.Read(m.Write())).ToArray();
    }
    public IReadOnlyList<PluginManifest> Installed()
    {
        lock(gate)return Directory.GetDirectories(root).Where(d=>!Path.GetFileName(d).StartsWith('.'))
            .Select(d=>ReadVerified(d)).OrderBy(m=>m.Id,StringComparer.Ordinal).ThenBy(m=>m.Version,StringComparer.Ordinal).ToArray();
    }
    [RequiresUnreferencedCode("Ordinary plugin import inspects a publisher's explicitly selected assembly.")]
    [RequiresDynamicCode("Unknown managed assemblies cannot be imported into a NativeAOT process.")]
    public PluginManifest Import(string packageDirectory,bool replace=false)
    {
        lock(gate)
        {
            var source=Path.GetFullPath(packageDirectory);var manifest=ReadVerified(source);
            if(!manifest.Targets.Contains(target,StringComparer.Ordinal))throw new InvalidDataException(manifest.Id+": unsupported target "+target);
            if(builtins.Any(b=>b.Id==manifest.Id))throw new InvalidDataException(manifest.Id+": conflicts with a built-in implementation");
            var available=builtins.Concat(Installed().Where(m=>m.Id!=manifest.Id || m.Version!=manifest.Version)).Append(manifest).ToArray();
            foreach(var dependency in manifest.Requires)
                if(!available.Any(m=>m.Execution.Scope<=manifest.Execution.Scope && m.Provides.Any(p=>p.Name==dependency.Name && p.Api==dependency.Api)))
                    throw new InvalidDataException(manifest.Id+": missing compatible installed capability "+dependency.Name);
            var key=Key(manifest.Id,manifest.Version);var destination=Path.Combine(root,key);
            if(active.GetValueOrDefault(key)>0)throw new InvalidOperationException(manifest.Id+": implementation is in use");
            if(Directory.Exists(destination) && !replace)throw new InvalidDataException(manifest.Id+": implementation already installed; replacement must be explicit");
            var stage=Path.Combine(root,".import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            var backup=Path.Combine(root,".previous-"+Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(Path.Combine(stage,PluginManifest.FileName),manifest.Write());
                foreach(var item in manifest.Files)
                {
                    var output=Inside(stage,item.Path);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    using var input=File.OpenRead(Inside(source,item.Path));using var copy=File.Create(output);input.CopyTo(copy);
                }
                // Verify the copied bytes, so a changing import source cannot bypass hashes.
                _=ReadVerified(stage);
                using(var candidate=Load(stage,manifest,()=>{}))_=candidate.Plugin.Bind(null);
                if(Directory.Exists(destination))Directory.Move(destination,backup);
                try{Directory.Move(stage,destination);}
                catch{if(Directory.Exists(backup))Directory.Move(backup,destination);throw;}
                if(Directory.Exists(backup))DeleteOwned(backup);
                return manifest;
            }
            finally{if(Directory.Exists(stage))DeleteOwned(stage);}
        }
    }
    [RequiresUnreferencedCode("Only the explicitly installed manifest entry is reflected.")]
    [RequiresDynamicCode("Use static registration in NativeAOT games.")]
    public PluginLease Acquire(string id,string version)
    {
        lock(gate)
        {
            var key=Key(id,version);var directory=Path.Combine(root,key);var manifest=ReadVerified(directory);
            if(manifest.Id!=id || manifest.Version!=version)throw new InvalidDataException("Installed plugin identity mismatch");
            if(!manifest.Targets.Contains(target,StringComparer.Ordinal))throw new InvalidDataException(id+": unsupported target "+target);
            var lease=Load(directory,manifest,()=>{lock(gate){if(--active[key]==0)active.Remove(key);}});
            active[key]=active.GetValueOrDefault(key)+1;return lease;
        }
    }
    public void Remove(string id,string version)
    {
        lock(gate)
        {
            var key=Key(id,version);
            if(active.GetValueOrDefault(key)>0)throw new InvalidOperationException(id+": implementation is in use");
            var directory=Path.Combine(root,key);var manifest=ReadVerified(directory);
            if(manifest.Id!=id || manifest.Version!=version)throw new InvalidDataException("Installed plugin identity mismatch");
            DeleteOwned(directory);
        }
    }
    [RequiresUnreferencedCode("Explicit factory entry from a verified package.")]
    [RequiresDynamicCode("Ordinary .NET controlled assembly loading.")]
    private PluginLease Load(string directory,PluginManifest manifest,Action released)
    {
        var context=new PackageLoadContext(directory,manifest,shared);
        try
        {
            var assembly=context.LoadManaged(Inside(directory,manifest.Entry.Assembly));
            var type=assembly.GetType(manifest.Entry.Type,true)!;
            var method=type.GetMethod(manifest.Entry.Method,BindingFlags.Public|BindingFlags.Static,null,Type.EmptyTypes,null);
            if(method==null || method.ContainsGenericParameters || method.ReturnType!=typeof(ApprovedApplicationPlugin))
                throw new InvalidDataException(manifest.Id+": factory must be public static ApprovedApplicationPlugin with no arguments");
            var factory=(ApprovedApplicationPlugin?)method.Invoke(null,null) ?? throw new InvalidDataException("Empty plugin factory");
            return new(manifest,PluginRegistration.Bind(manifest,factory),context,released);
        }
        catch{context.Unload();throw;}
    }
    private PluginManifest ReadVerified(string directory)
    {
        NoLinks(directory);
        var manifestPath=Path.Combine(directory,PluginManifest.FileName);NoLinks(manifestPath);
        if(new FileInfo(manifestPath).Length>1024*1024)throw new InvalidDataException("Plugin manifest exceeds size budget");
        var manifest=PluginManifest.Read(File.ReadAllText(manifestPath));
        var expected=manifest.Files.Select(f=>f.Path).Append(PluginManifest.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files=EnumerateSafe(directory).ToArray();
        if(files.Length!=expected.Count)throw new InvalidDataException(manifest.Id+": unlisted or missing files");
        if(files.Any(f=>new FileInfo(f).Length>256L*1024*1024) || files.Sum(f=>new FileInfo(f).Length)>1024L*1024*1024)
            throw new InvalidDataException(manifest.Id+": plugin size budget exceeded");
        foreach(var path in files)
        {
            NoLinks(path);
            if(!expected.Contains(Path.GetRelativePath(directory,path).Replace('\\','/')))throw new InvalidDataException("Unlisted plugin content: "+path);
        }
        var managed=new Dictionary<string,(AssemblyName Name,PluginFile File)>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest.Files)
        {
            var path=Inside(directory,file.Path);
            using(var input=File.OpenRead(path))
                if(Convert.ToHexStringLower(SHA256.HashData(input))!=file.Sha256)throw new InvalidDataException(manifest.Id+": content hash mismatch: "+file.Path);
            if(file.Role=="native" && target=="win-x64")
            {
                using var nativeStream=File.OpenRead(path);using var nativePe=new PEReader(nativeStream);
                if(nativePe.PEHeaders.CoffHeader.Machine!=Machine.Amd64)throw new InvalidDataException(file.Path+": incompatible native architecture");
            }
            if(file.Role is not ("implementation" or "reference" or "managed"))continue;
            using var stream=File.OpenRead(path);using var pe=new PEReader(stream);
            if(!pe.HasMetadata)throw new InvalidDataException(file.Path+": expected managed assembly");
            var metadata=pe.GetMetadataReader();
            if(target=="win-x64" && (pe.PEHeaders.CorHeader!.Flags.HasFlag(CorFlags.Requires32Bit) ||
                pe.PEHeaders.CoffHeader.Machine is not (Machine.I386 or Machine.Amd64)))throw new InvalidDataException(file.Path+": incompatible managed architecture");
            var name=AssemblyName.GetAssemblyName(path);
            if(EditorOnly(name.Name!) || shared.Any(s=>s.GetName().Name==name.Name))throw new InvalidDataException(file.Path+": editor/shared contract implementation must not be bundled");
            if(file.Role!="reference" && !managed.TryAdd(name.Name!,(name,file)))throw new InvalidDataException(file.Path+": duplicate managed assembly identity");
            foreach(var reference in metadata.AssemblyReferences)
                if(EditorOnly(metadata.GetString(metadata.GetAssemblyReference(reference).Name)))throw new InvalidDataException(file.Path+": Editor-specific reference is forbidden");
            if(file.Role!="reference" && IsReferenceAssembly(metadata))throw new InvalidDataException(file.Path+": reference assembly cannot be used as implementation");
        }
        foreach(var item in managed.Values)
        {
            using var stream=File.OpenRead(Inside(directory,item.File.Path));using var pe=new PEReader(stream);
            var metadata=pe.GetMetadataReader();
            foreach(var handle in metadata.AssemblyReferences)
            {
                var reference=metadata.GetAssemblyReference(handle);var name=metadata.GetString(reference.Name);
                if(Framework(name))continue;
                var installed=shared.Select(a=>a.GetName()).FirstOrDefault(a=>a.Name==name);
                if(installed==null && managed.TryGetValue(name,out var dependency))installed=dependency.Name;
                if(installed==null || installed.Version!=reference.Version)throw new InvalidDataException(item.File.Path+": missing/incompatible managed dependency "+name+" "+reference.Version);
            }
        }
        return manifest;
    }
    private static bool IsReferenceAssembly(MetadataReader metadata)=>metadata.CustomAttributes.Any(h=>
    {
        var attribute=metadata.GetCustomAttribute(h);
        if(attribute.Constructor.Kind!=HandleKind.MemberReference)return false;
        var member=metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if(member.Parent.Kind!=HandleKind.TypeReference)return false;
        var type=metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        return metadata.GetString(type.Namespace)=="System.Runtime.CompilerServices" && metadata.GetString(type.Name)=="ReferenceAssemblyAttribute";
    });
    private static bool EditorOnly(string name)=>name is "Editor" or "Editor.Core" || name.StartsWith("CordiSharp.Plugins.Avalonia",StringComparison.Ordinal);
    internal static bool Framework(string name)=>platformAssemblies.Contains(name) &&
        (name is "netstandard" or "mscorlib" or "System" or "Microsoft.CSharp" || name.StartsWith("System.",StringComparison.Ordinal));
    private static IEnumerable<string> EnumerateSafe(string directory)
    {
        foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            NoLinks(entry);
            if(Directory.Exists(entry)){foreach(var file in EnumerateSafe(entry))yield return file;}
            else yield return entry;
        }
    }
    internal static string Inside(string directory,string relative)
    {
        ContentPath.Validate(relative);var path=Path.GetFullPath(Path.Combine(directory,relative));
        if(!path.StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Plugin path escaped package");
        return path;
    }
    private static void NoLinks(string path)
    {
        for(var current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Plugin path must not contain links: "+path);
    }
    private static string Key(string id,string version)=>Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id+"\0"+version)));
    private void DeleteOwned(string directory)
    {
        if(Path.GetDirectoryName(Path.GetFullPath(directory))!=root)throw new InvalidOperationException("Cleanup outside plugin store");
        NoLinks(directory);Directory.Delete(directory,true);
    }
}

public sealed class PluginLease : IDisposable
{
    private PackageLoadContext? context;
    private Action? released;
    private readonly ApprovedApplicationPlugin plugin;
    public PluginManifest Manifest {get;}
    public ApprovedApplicationPlugin Plugin=>context!=null?plugin:throw new ObjectDisposedException(nameof(PluginLease));
    internal PluginLease(PluginManifest manifest,ApprovedApplicationPlugin plugin,PackageLoadContext context,Action released)
    {Manifest=manifest;this.plugin=plugin;this.context=context;this.released=released;}
    public void Dispose(){var owner=Interlocked.Exchange(ref context,null);if(owner==null)return;owner.Unload();Interlocked.Exchange(ref released,null)?.Invoke();}
}

internal sealed class PackageLoadContext(string directory,PluginManifest manifest,Assembly[] shared) : AssemblyLoadContext(isCollectible:true)
{
    // Loading immutable, verified bytes avoids keeping installation files mapped
    // after a lease ends. ALC reclamation itself remains GC-controlled.
    internal Assembly LoadManaged(string path){using var input=File.OpenRead(path);return LoadFromStream(input);}
    protected override Assembly? Load(AssemblyName name)
    {
        var contract=shared.SingleOrDefault(a=>a.GetName().Name==name.Name);
        if(contract!=null)return contract;
        if(PluginStore.Framework(name.Name!))return null;
        var file=manifest.Files.Where(f=>f.Role is "managed" or "implementation")
            .SingleOrDefault(f=>AssemblyName.GetAssemblyName(PluginStore.Inside(directory,f.Path)).Name==name.Name)
            ?? throw new FileNotFoundException("Unapproved plugin assembly: "+name.Name);
        return LoadManaged(PluginStore.Inside(directory,file.Path));
    }
    protected override nint LoadUnmanagedDll(string name)
    {
        var file=manifest.Files.Where(f=>f.Role=="native").SingleOrDefault(f=>Path.GetFileName(f.Path)==name || Path.GetFileNameWithoutExtension(f.Path)==name);
        if(file==null)throw new DllNotFoundException("Unapproved plugin native dependency: "+name);
        return LoadUnmanagedDllFromPath(PluginStore.Inside(directory,file.Path));
    }
}
