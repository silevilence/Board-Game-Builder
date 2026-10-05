using BoardGame.Kernel;
namespace BoardGame.Editor;

public sealed record PluginManifest(string Id,int Api,string[] Provides,string[] Requires);
public enum ExtensionKind { Panel, Inspector, Importer, Menu, Shortcut, Event, Service, ExportStep }
public sealed record EditorExtension(string Plugin,ExtensionKind Kind,string Id,object Value);
public interface IEditorPlugin { void Activate(PluginContext context); }
public sealed record ExportContext(string ModuleDirectory,string Destination);

public sealed class PluginContext : IDisposable
{
    private readonly PluginHost host;
    private readonly string id;
    private readonly List<Action> cleanup=[];
    private bool disposed;
    internal PluginContext(PluginHost host,string id) { this.host=host; this.id=id; }
    public ModuleDocument? Document => host.Document;
    public void Register(ExtensionKind kind,string key,object value)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        var item=new EditorExtension(id,kind,key,value); host.Register(item); cleanup.Add(()=>host.Remove(item));
    }
    public void Own(IDisposable resource) { ObjectDisposedException.ThrowIf(disposed,this); cleanup.Add(resource.Dispose); }
    public void OnDocumentChanged(Action action)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        host.DocumentChanged+=action; cleanup.Add(()=>host.DocumentChanged-=action);
    }
    public object Service(string name) => host.Extensions.Single(e=>e.Kind==ExtensionKind.Service && e.Id==name).Value;
    public void Dispose()
    {
        if(disposed) return; disposed=true;
        foreach(var action in cleanup.AsEnumerable().Reverse())
            try { action(); } catch(Exception e) { host.Diagnostics.Add(id+": cleanup: "+e.Message); }
        cleanup.Clear();
    }
}

public sealed class PluginHost : IDisposable
{
    private readonly Dictionary<string,(PluginManifest Manifest,Func<IEditorPlugin> Factory)> catalog=new(StringComparer.Ordinal);
    private readonly Dictionary<string,PluginContext> active=new(StringComparer.Ordinal);
    private readonly List<EditorExtension> extensions=[];
    public IReadOnlyList<EditorExtension> Extensions => extensions.ToArray();
    public IReadOnlyCollection<string> Active => active.Keys.ToArray();
    public IReadOnlyList<PluginManifest> Catalog => catalog.Values.Select(v=>v.Manifest).ToArray();
    public List<string> Diagnostics { get; }=[];
    public ModuleDocument? Document { get; private set; }
    public event Action? DocumentChanged;
    public event Action? RegistrationsChanged;
    public void SetDocument(ModuleDocument? document)
    {
        if(Document!=null) Document.Changed-=NotifyDocument;
        Document=document; if(document!=null) document.Changed+=NotifyDocument; NotifyDocument();
    }
    private void NotifyDocument()
    {
        foreach(var callback in DocumentChanged?.GetInvocationList() ?? [])
            try { ((Action)callback)(); } catch(Exception e) { Diagnostics.Add("document event: "+e.Message); }
    }
    public void Install(string manifest,Func<IEditorPlugin> factory)
    {
        var value=Wire.Read<PluginManifest>(manifest);
        if(string.IsNullOrWhiteSpace(value.Id) || value.Api!=1 || value.Provides==null || value.Requires==null || value.Provides.Concat(value.Requires).Any(string.IsNullOrWhiteSpace) || value.Provides.Distinct().Count()!=value.Provides.Length) throw new InvalidDataException("Invalid plugin identity/API manifest");
        if(catalog.Values.Any(p=>p.Manifest.Provides.Intersect(value.Provides).Any())) throw new InvalidDataException("Ambiguous capability provider: "+value.Id);
        if(!catalog.TryAdd(value.Id,(value,factory))) throw new InvalidDataException("Duplicate plugin: "+value.Id);
    }
    public void Enable(string id)
    {
        var order=Order([id]); var added=new List<string>();
        try { foreach(var next in order) if(!active.ContainsKey(next)) { Activate(next); added.Add(next); } }
        catch { foreach(var next in added.AsEnumerable().Reverse()) Deactivate(next); throw; }
        finally { RegistrationsChanged?.Invoke(); }
    }
    private void Activate(string id)
    {
        var context=new PluginContext(this,id);
        try { catalog[id].Factory().Activate(context); active.Add(id,context); }
        catch { context.Dispose(); throw; }
    }
    public void Disable(string id)
    {
        var affected=Dependents(id);
        foreach(var next in Order(affected).AsEnumerable().Reverse()) if(affected.Contains(next)) Deactivate(next);
        RegistrationsChanged?.Invoke();
    }
    private void Deactivate(string id) { if(active.Remove(id,out var context)) context.Dispose(); }
    public void Reload(string id)
    {
        var affected=Dependents(id); var order=Order(affected);
        foreach(var next in order.AsEnumerable().Reverse()) if(affected.Contains(next)) Deactivate(next);
        var added=new List<string>();
        try { foreach(var next in order) if(!active.ContainsKey(next)) { Activate(next); added.Add(next); } }
        catch { foreach(var next in added.AsEnumerable().Reverse()) Deactivate(next); throw; }
        finally { RegistrationsChanged?.Invoke(); }
    }
    private HashSet<string> Dependents(string id)
    {
        if(!catalog.ContainsKey(id)) throw new InvalidDataException("Unknown plugin: "+id);
        var affected=new HashSet<string>(StringComparer.Ordinal) {id};
        bool changed;
        do
        {
            changed=false;
            var capabilities=affected.SelectMany(i=>catalog[i].Manifest.Provides).ToHashSet();
            foreach(var next in active.Keys) if(catalog[next].Manifest.Requires.Any(capabilities.Contains)) changed|=affected.Add(next);
        } while(changed);
        return affected;
    }
    private List<string> Order(IEnumerable<string> roots)
    {
        var result=new List<string>(); var visiting=new HashSet<string>(); var done=new HashSet<string>();
        foreach(var root in roots.Order(StringComparer.Ordinal)) Visit(root);
        return result;
        void Visit(string id)
        {
            if(done.Contains(id)) return;
            if(!visiting.Add(id)) throw new InvalidDataException("Plugin dependency cycle: "+string.Join(" -> ",visiting)+" -> "+id);
            if(!catalog.TryGetValue(id,out var plugin)) throw new InvalidDataException("Unknown plugin: "+id);
            foreach(var capability in plugin.Manifest.Requires)
            {
                var provider=catalog.Values.SingleOrDefault(p=>p.Manifest.Provides.Contains(capability));
                if(provider.Manifest==null) throw new InvalidDataException($"{id}: missing capability {capability}");
                Visit(provider.Manifest.Id);
            }
            visiting.Remove(id); done.Add(id); result.Add(id);
        }
    }
    internal void Register(EditorExtension extension)
    {
        if(extensions.Any(e=>e.Kind==extension.Kind && e.Id==extension.Id)) throw new InvalidDataException($"Duplicate {extension.Kind}: {extension.Id}");
        extensions.Add(extension);
    }
    internal void Remove(EditorExtension extension) => extensions.Remove(extension);
    public void Dispose() { foreach(var id in Order(active.Keys).AsEnumerable().Reverse()) Deactivate(id); SetDocument(null); RegistrationsChanged?.Invoke(); }
}
