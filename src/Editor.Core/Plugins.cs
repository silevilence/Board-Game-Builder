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
        if(string.IsNullOrWhiteSpace(value.Id) || value.Api!=1 || value.Provides==null || value.Requires==null) throw new InvalidDataException("Invalid plugin identity/API manifest");
        if(!catalog.TryAdd(value.Id,(value,factory))) throw new InvalidDataException("Duplicate plugin: "+value.Id);
    }
    public void Enable(string id)
    {
        if(active.ContainsKey(id)) return;
        var context=new PluginContext(this,id);
        try { catalog[id].Factory().Activate(context); active.Add(id,context); }
        catch { context.Dispose(); throw; }
        finally { RegistrationsChanged?.Invoke(); }
    }
    public void Disable(string id)
    {
        if(active.Remove(id,out var context)) context.Dispose();
        RegistrationsChanged?.Invoke();
    }
    public void Reload(string id) { Disable(id); Enable(id); }
    internal void Register(EditorExtension extension)
    {
        if(extensions.Any(e=>e.Kind==extension.Kind && e.Id==extension.Id)) throw new InvalidDataException($"Duplicate {extension.Kind}: {extension.Id}");
        extensions.Add(extension);
    }
    internal void Remove(EditorExtension extension) => extensions.Remove(extension);
    public void Dispose() { foreach(var id in active.Keys.Reverse().ToArray()) Disable(id); SetDocument(null); }
}
