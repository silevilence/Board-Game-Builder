using BoardGame.Runtime;

namespace BoardGame.Plugins;

public sealed record PluginDiagnostic(string Code,string Plugin,string[] Chain,string Message);
public sealed record PluginCompatibilityResult(IReadOnlyList<PluginManifest> Ordered,IReadOnlyList<PluginDiagnostic> Diagnostics)
{
    public bool Success=>Diagnostics.Count==0;
}

public static class PluginCompatibility
{
    // The caller supplies the complete selected set, not the installed catalogue.
    // Dependencies may be provided by a different ID with the same capability API.
    public static PluginCompatibilityResult Check(IEnumerable<PluginManifest> selected,string target,bool nativeAot,HostMode mode)
    {
        var manifests=selected.ToArray();var errors=new List<PluginDiagnostic>();
        void Error(string code,PluginManifest m,IEnumerable<string> chain,string message)=>errors.Add(new(code,m.Id,chain.ToArray(),message));
        foreach(var manifest in manifests)
        {
            try{manifest.Validate();}
            catch(InvalidDataException e){Error("manifest.invalid",manifest,[manifest.Id],e.Message);}
        }
        if(errors.Count>0)return new([],errors);
        if(manifests.Select(m=>m.Id).Distinct(StringComparer.Ordinal).Count()!=manifests.Length)
            return new([],[new("identity.conflict","",[],"Only one implementation version of a plugin may be selected")]);
        var active=manifests.Where(m=>(m.Execution.Modes & mode)!=0 && (mode!=HostMode.Headless || m.Execution.Scope==RuntimeScope.Instance)).ToArray();
        var providers=new Dictionary<string,PluginManifest>(StringComparer.Ordinal);
        foreach(var manifest in active)
            foreach(var capability in manifest.Provides)
                if(!providers.TryAdd(capability.Name,manifest))Error("capability.conflict",manifest,[manifest.Id],"Multiple providers: "+capability.Name);
        if(errors.Count>0)return new([],errors);
        // Check every selected identity even when headless omits its device activation.
        foreach(var root in manifests)Visit(root,[root.Id],new HashSet<string>(StringComparer.Ordinal));
        void Visit(PluginManifest manifest,string[] chain,HashSet<string> visited)
        {
            if(!visited.Add(manifest.Id))return;
            if(!manifest.Targets.Contains(target,StringComparer.Ordinal))Error("target.unsupported",manifest,chain,"Unsupported target: "+target);
            if(!active.Contains(manifest))return;
            if(nativeAot && (manifest.Aot?.Compatible!=true || !manifest.Aot.Targets.Contains(target,StringComparer.Ordinal)))
                Error("aot.undeclared-or-incompatible",manifest,chain,manifest.Aot?.Reason ?? "No explicit AOT declaration");
            foreach(var dependency in manifest.Requires)
            {
                if(!providers.TryGetValue(dependency.Name,out var provider)){Error("capability.missing",manifest,[..chain,dependency.Name],"Missing capability: "+dependency.Name);continue;}
                var provided=provider.Provides.Single(c=>c.Name==dependency.Name);
                if(provided.Api!=dependency.Api)Error("capability.version",manifest,[..chain,provider.Id],"Incompatible capability API: "+dependency.Name);
                Visit(provider,[..chain,provider.Id],visited);
            }
        }
        if(errors.Count>0)return new([],errors.DistinctBy(e=>e.Code+"/"+string.Join('/',e.Chain)+"/"+e.Message).ToArray());
        try
        {
            var definitions=manifests.Select(m=>m.Definition() with {Requires=m.Requires.Select(c=>c.Name).ToArray()}).ToArray();
            var ordered=RuntimeHost.Validate(PluginManifest.Startup(manifests),definitions,[],mode);
            return new(ordered.Select(d=>manifests.Single(m=>m.Id==d.Id)).ToArray(),[]);
        }
        catch(InvalidDataException error){return new([],[new("graph.invalid","",[],error.Message)]);}
    }
}
