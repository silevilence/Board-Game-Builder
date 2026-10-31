using System.Text.Json;
using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.Plugins;
using BoardGame.Rendering;
using BoardGame.Runtime;

namespace BoardGame.Player;

public sealed class BoardApplicationPlayerProvider(GraphicsBackendPackages backends):IPlayerProvider
{
    public bool Supports(PlayerSource source)
    {
        var header=source.Header;
        if(source.Snapshot)
        {
            if(!header.TryGetProperty("identity",out var identity) || !identity.TryGetProperty("startup",out header))return false;
        }
        return header.TryGetProperty("plugins",out var plugins) && plugins.ValueKind==JsonValueKind.Array &&
            plugins.EnumerateArray().Any(p=>p.TryGetProperty("id",out var id) && id.GetString()=="definition.board-state");
    }
    public Window Create(PlayerSource source,PlayerLaunch launch)
    {
        PluginCatalogLease? lease=null;GraphicsBackendLease? graphics=null;
        try
        {
            var header=source.Snapshot?source.Header.GetProperty("identity").GetProperty("startup"):source.Header;
            var startup=header.Deserialize<RuntimeStartup>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Missing board startup");
            var store=PlayerRuntimePlugins.Open(typeof(GameState).Assembly,typeof(ICpuDecisionProvider).Assembly,typeof(IGameSession).Assembly,typeof(BoardGame.Resources.AssetRegistry).Assembly);
            lease=store.Open(startup,GraphicsBackendContract.Extend(BoardApplicationPlugins.Catalog()));
            var content=lease.Catalog.Load(source.Path);graphics=backends.Open(startup);
            var window=new NativeBoardWindow(lease.Catalog,content,new(graphics.Startup,[graphics.Definition]),startup.Id);
            window.Closed+=(_,_)=>{lease.Dispose();graphics.Dispose();};return window;
        }
        catch{lease?.Dispose();graphics?.Dispose();throw;}
    }
    public PlayerContribution Contribution()=>new(new("player.board-application","1",1,["player.board-application"],[],scope=>scope.Provide("player.board-application",this)),"player.board-application");
}
