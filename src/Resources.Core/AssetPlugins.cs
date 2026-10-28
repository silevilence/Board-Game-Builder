using BoardGame.Runtime;

namespace BoardGame.Resources;

public static class AssetPlugins
{
    public static IEnumerable<RuntimePluginDefinition> Catalog(Func<AssetSet?>? pool=null,Func<AssetSet?,AssetRegistry>? registryFactory=null)
    {
        yield return new RuntimePluginDefinition("resources","1",1,["resources"],[],scope=>
        {
            var existing=pool?.Invoke();
            var registry=registryFactory==null?new AssetRegistry(existing):registryFactory(existing) ?? throw new InvalidDataException("Resource registry factory returned no service");
            scope.Provide("resources",registry);
            scope.Own(registry.Register(new BinaryResourceLoader(),ResourceSource.Read(scope.Configuration,"resources")));
        }) {Content=Sources("resources")};
        yield return Loader("resources.images",new ImageResourceLoader());
        yield return Loader("resources.texturepacker",new TexturePackerResourceLoader());
        yield return new RuntimePluginDefinition("resources.font","1",1,["resources.font"],["resources"],scope=>
        {
            var font=FontResourceLoader.Read(scope.Configuration);
            scope.Provide("resources.font",scope.Own(scope.Service<AssetRegistry>("resources").Register(new FontResourceLoader(font),[new(font.Resource)])));
        }) {Content=new(["path","license"],(config,startup)=>_=FontResourceLoader.Read(config),Prepare:true)};
    }
    private static PluginContentContract Sources(string id)=>new(["sources"],(config,startup)=>_=ResourceSource.Read(config,id),Prepare:true);
    private static RuntimePluginDefinition Loader(string id,IResourceLoader loader)=>new(id,"1",1,[id],["resources"],scope=>
    {
        var registration=scope.Service<AssetRegistry>("resources").Register(loader,ResourceSource.Read(scope.Configuration,id));
        scope.Provide(id,scope.Own(registration));
    }) {Content=Sources(id)};
}
