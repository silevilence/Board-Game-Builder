using BoardGame.Plugins;
using BoardGame.Rendering;
using BoardGame.Resources;

namespace BoardGame.Player;

public static class PlayerRuntimePlugins
{
    // Fixed installation location owned by the host distribution, never a path from game content.
    public static PluginStore Open(params System.Reflection.Assembly[] contracts)=>new(PluginPlayerDistribution.InstalledDirectory(AppContext.BaseDirectory),"win-x64",
        new[]{typeof(IApplicationView).Assembly,typeof(IDraw2D).Assembly,typeof(AssetSet).Assembly}.Concat(contracts));
}
