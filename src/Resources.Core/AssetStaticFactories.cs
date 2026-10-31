using BoardGame.AppRuntime;
namespace BoardGame.Resources;

public static class AssetStaticFactories
{
    public static ApprovedApplicationPlugin Resources()=>new(_=>AssetPlugins.Resources());
    public static ApprovedApplicationPlugin Images()=>new(_=>AssetPlugins.Images());
    public static ApprovedApplicationPlugin TexturePacker()=>new(_=>AssetPlugins.TexturePacker());
    public static ApprovedApplicationPlugin Font()=>new(_=>AssetPlugins.Font());
}
