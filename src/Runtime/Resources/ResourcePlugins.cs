using BoardGame.Kernel;
using BoardGame.Resources;

namespace BoardGame.Runtime;

// Compatibility attachment of board animation/document metadata to generic assets.
internal sealed class ResourcePoolBuilder : AssetBuilder
{
    private AnimationLibrary? animations;
    private DocumentLibrary? documents;
    public void Animations(AnimationLibrary value) {if(animations!=null)throw new InvalidDataException("resources.animations: duplicate library");animations=value;}
    public void Documents(DocumentLibrary value) {if(documents!=null)throw new InvalidDataException("resources.documents: duplicate library");documents=value;}
    public override ResourcePool Freeze(ResourceInput input)=>new(input.Files,Images,Binaries,Typeface,animations,documents);
}
internal sealed class ResourceRegistry(ResourcePool? existing=null) : AssetRegistry(existing ?? ResourcePool.Empty,()=>new ResourcePoolBuilder())
{
    public new ResourcePool Pool=>(ResourcePool)base.Pool;
    public new ResourcePool Load(ResourceInput input)=>(ResourcePool)base.Load(input);
}
internal static class ResourcePlugins
{
    internal static bool IsResource(string id)=>id is "resources" or "resources.images" or "resources.texturepacker" or "resources.font" or "resources.animations" or "resources.documents";
    public static IEnumerable<RuntimePluginDefinition> Catalog(Func<ResourcePool?> pool)
    {
        foreach(var plugin in AssetPlugins.Catalog(()=>pool(),existing=>new ResourceRegistry((ResourcePool?)existing))) yield return plugin;
        yield return new RuntimePluginDefinition("resources.documents","1",1,["resources.documents"],["resources"],scope=>
        {
            var path=scope.Configuration.GetProperty("path").GetString()!;ResourceSource.CheckPath(path);
            scope.Provide("resources.documents",scope.Own(scope.Service<ResourceRegistry>("resources").Register(new DocumentResourceLoader(),[new(path)])));
        }) {Content=PathContent};
        yield return new RuntimePluginDefinition("resources.animations","1",1,["resources.animations"],["resources"],scope=>
        {
            var path=scope.Configuration.GetProperty("path").GetString()!;ResourceSource.CheckPath(path);
            scope.Provide("resources.animations",scope.Own(scope.Service<ResourceRegistry>("resources").Register(new AnimationResourceLoader(),[new(path)])));
        }) {Content=PathContent};
    }
    private static PluginContentContract PathContent=>new(["path"],(config,startup)=>ContentPath.Validate(config.GetProperty("path").GetString()!),Prepare:true);
}