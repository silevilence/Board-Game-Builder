using BoardGame.AppRuntime;
using BoardGame.Runtime;

namespace BoardGame.Plugins;

public static class PluginRegistration
{
    public static ApprovedApplicationPlugin Bind(PluginManifest manifest,ApprovedApplicationPlugin factory)
    {
        // Freeze caller-owned arrays before registering a long-lived implementation.
        manifest=PluginManifest.Read(manifest.Write());var fingerprint=manifest.Fingerprint;
        RuntimePluginDefinition Create(GameContent? content)
        {
            var definition=factory.Bind(content);
            if(definition.Id!=manifest.Id || definition.Version!=manifest.Version || definition.Api!=manifest.Api ||
                !definition.Provides.Order(StringComparer.Ordinal).SequenceEqual(manifest.Provides.Select(c=>c.Name).Order(StringComparer.Ordinal)) ||
                !definition.Requires.Order(StringComparer.Ordinal).SequenceEqual(manifest.Requires.Select(c=>c.Name).Order(StringComparer.Ordinal)) ||
                definition.Execution.Modes!=manifest.Execution.Modes || definition.Execution.Scope!=manifest.Execution.Scope ||
                manifest.ContentFields!=null && !(definition.Content?.Fields ?? []).SequenceEqual(manifest.ContentFields) ||
                !SameContributions(definition.Execution,manifest.Execution))throw new InvalidDataException(manifest.Id+": factory differs from declared identity/capabilities/execution");
            return definition with {Implementation=fingerprint};
        }
        _=Create(null);return new(Create);
    }
    public static ApprovedApplicationPlugin IdentityOnly(PluginManifest original)
    {
        var manifest=PluginManifest.Read(original.Write());
        if(manifest.Execution.Scope==RuntimeScope.Instance && (manifest.Execution.Modes & HostMode.Headless)!=0)
            throw new InvalidDataException(manifest.Id+": active instance implementation cannot be omitted");
        return new(_=>new(manifest.Id,manifest.Version,manifest.Api,manifest.Provides.Select(c=>c.Name).ToArray(),manifest.Requires.Select(c=>c.Name).ToArray(),
            _=>throw new InvalidOperationException(manifest.Id+": this headless publication does not contain a graphical implementation"))
            {Implementation=manifest.Fingerprint,Execution=manifest.Execution,Content=new(manifest.ContentFields ?? [])});
    }
    private static bool SameContributions(PluginExecution left,PluginExecution right)
    {
        var a=left.Contributions ?? [];var b=right.Contributions ?? [];
        return a.Length==b.Length && a.Zip(b).All(p=>p.First.Id==p.Second.Id && p.First.Phase==p.Second.Phase &&
            (p.First.Before ?? []).SequenceEqual(p.Second.Before ?? []) && (p.First.After ?? []).SequenceEqual(p.Second.After ?? []));
    }
}
