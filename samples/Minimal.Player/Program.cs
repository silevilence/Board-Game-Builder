using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Player;
using BoardGame.Rendering;
using Sample.Motion;

if(args.Length==2 && args[0]=="runtime-host") {BoardGame.Plugins.PluginPlayerDistribution.WriteHostContract(args[1]);return 0;}
if(args.Length==2 && args[0]=="runtime-catalog")
{
    var plugins=GraphicsBackendContract.Extend(new ApplicationCatalog(MotionPlugins.Catalog())).Content.Plugins;
    File.WriteAllText(args[1],JsonSerializer.Serialize(plugins.ToDictionary(p=>p.Id,p=>p.Version)));return 0;
}
var backend=WindowsDxBackend.Plugin();
var graphics=new GraphicsConfiguration(new(1,"graphics","1",[new(backend.Id,backend.Version,backend.Api,JsonSerializer.SerializeToElement(new{}))]),[backend]);
var bundled=Path.Combine(AppContext.BaseDirectory,"game.bgm");
if(args.Length==2 && args[0]=="--verify-standalone")return MinimalPlayerProbe.Run(bundled,args[1],graphics);
if(args.Length==3 && args[0]=="--verify-source")return MinimalPlayerProbe.Run(args[1],args[2],graphics);
var source=args.FirstOrDefault() ?? (File.Exists(bundled)?bundled:"samples/motion-lab");
return PlayerApplication.Run(new PlayerCatalog([new ApplicationPlayerProvider(new ApplicationCatalog(MotionPlugins.Catalog()),graphics,PlayerRuntimePlugins.Open(typeof(BoardGame.Resources.AssetRegistry).Assembly),new(WindowsDxBackend.Plugin,Path.Combine(AppContext.BaseDirectory,"backends","desktopgl"))).Contribution()]),new(source));
