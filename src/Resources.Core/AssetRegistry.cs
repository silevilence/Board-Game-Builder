using BoardGame.Kernel;
using BoardGame.Runtime;

namespace BoardGame.Resources;

public class AssetBuilder
{
    protected readonly Dictionary<string,ImageReference> Images=new(StringComparer.Ordinal);
    protected readonly Dictionary<string,string> Binaries=new(StringComparer.Ordinal);
    private readonly HashSet<string> names=new(StringComparer.Ordinal);
    protected FontReference? Typeface;
    private void Reserve(string name)
    {ResourceSource.CheckName(name);if(!names.Add(name))throw new InvalidDataException("duplicate/conflicting resource name: "+name);if(names.Count>16384)throw new InvalidDataException("Resource name budget exceeded");}
    public void Image(string name,ImageReference image) {Reserve(name);Images.Add(name,image);}
    public void Binary(string name,string path) {Reserve(name);Binaries.Add(name,path);}
    public void Font(FontReference value) {if(Typeface!=null)throw new InvalidDataException("resources.font: duplicate active font");Typeface=value;}
    public virtual AssetSet Freeze(ResourceInput input)=>new(input.Files,Images,Binaries,Typeface);
}
public class AssetRegistry(AssetSet? existing=null,Func<AssetBuilder>? builderFactory=null)
{
    private readonly List<(IResourceLoader Loader,ResourceSource[] Sources)> registrations=[];
    public AssetSet Pool {get;private set;}=existing ?? AssetSet.Empty;
    public IDisposable Register(IResourceLoader loader,ResourceSource[] sources)
    {
        var entry=(loader,sources.ToArray());registrations.Add(entry);
        return new Registration(()=>registrations.Remove(entry));
    }
    public AssetSet Load(ResourceInput input)
    {
        var builder=builderFactory?.Invoke() ?? new AssetBuilder();
        foreach(var entry in registrations)foreach(var source in entry.Sources)entry.Loader.Load(source,input,builder);
        return Pool=builder.Freeze(input);
    }
    private sealed class Registration(Action release) : IDisposable
    {private bool disposed;public void Dispose() {if(disposed)return;disposed=true;release();}}
}
