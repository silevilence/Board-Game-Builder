using System.Text.Json;
using Avalonia.Controls;
using BoardGame.AppRuntime;
using BoardGame.Rendering;
using BoardGame.Runtime;
using BoardGame.Plugins;
using BoardGame.Kernel;

namespace BoardGame.Player;

public sealed class ApplicationPlayerProvider(ApplicationCatalog catalog,GraphicsConfiguration graphics,PluginStore? installation=null,GraphicsBackendPackages? backends=null) : IPlayerProvider
{
    public bool Supports(PlayerSource source)
    {
        var header=source.Header;
        if(source.Snapshot)
        {
            if(!header.TryGetProperty("kind",out var kind) || kind.GetString()!="application-content" || !header.TryGetProperty("identity",out var identity))return false;
            if(!identity.TryGetProperty("startup",out header))return false;
        }
        if(!header.TryGetProperty("plugins",out var plugins) || plugins.ValueKind!=JsonValueKind.Array)return false;
        var definitions=catalog.Content.Plugins.Where(p=>p.Provides.Contains("definition")).Select(p=>p.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var plugin in installation?.Installed() ?? [])if(plugin.Provides.Any(p=>p.Name=="definition"))definitions.Add(plugin.Id);
        return plugins.EnumerateArray().Any(p=>p.TryGetProperty("id",out var id) && definitions.Contains(id.GetString()!));
    }
    public Window Create(PlayerSource source,PlayerLaunch launch)
    {
        if(launch.Actions!=null || launch.Smoke!=null)throw new InvalidDataException("This Player capability does not support board action probes");
        PluginCatalogLease? lease=null;
        GraphicsBackendLease? backendLease=null;
        try
        {
            var approved=GraphicsBackendContract.Extend(catalog);
            var header=source.Snapshot?source.Header.GetProperty("identity").GetProperty("startup"):source.Header;
            var startup=header.Deserialize<RuntimeStartup>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? throw new InvalidDataException("Missing application startup");
            var selectedGraphics=graphics;
            if(startup.Plugins.Any(p=>GraphicsBackendContract.Descriptions.Any(d=>d.Id==p.Id)))
            {
                backendLease=(backends ?? throw new InvalidDataException("This host has no selectable backend distribution")).Open(startup);
                selectedGraphics=new(backendLease.Startup,[backendLease.Definition]);
            }
            if(installation!=null)
            {
                lease=installation.Open(startup,approved);
            }
            var window=new ApplicationWindow(lease?.Catalog ?? approved,selectedGraphics,source.Path);
            window.Closed+=(_,_)=>{lease?.Dispose();backendLease?.Dispose();};return window;
        }
        catch{lease?.Dispose();backendLease?.Dispose();throw;}
    }
    public PlayerContribution Contribution(string id="player.application")=>new(new(id,"1.0.0",1,[id],[],scope=>scope.Provide(id,this)),id);
}
