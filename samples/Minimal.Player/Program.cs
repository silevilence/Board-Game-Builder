using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Player;
using BoardGame.Rendering;
using Sample.Motion;

if(args.Length==2 && args[0]=="runtime-catalog")
{
    var plugins=new ApplicationCatalog(MotionPlugins.Catalog()).Content.Plugins;
    File.WriteAllText(args[1],JsonSerializer.Serialize(plugins.ToDictionary(p=>p.Id,p=>p.Version)));return 0;
}
var backend=WindowsDxBackend.Plugin();
var graphics=new GraphicsConfiguration(new(1,"graphics","1",[new(backend.Id,backend.Version,backend.Api,JsonSerializer.SerializeToElement(new{}))]),[backend]);
var bundled=Path.Combine(AppContext.BaseDirectory,"game.bgm");
if(args.Length==2 && args[0]=="--verify-standalone")return MinimalPlayerProbe.Run(bundled,args[1],graphics);
var source=args.FirstOrDefault() ?? (File.Exists(bundled)?bundled:"samples/motion-lab");
return PlayerApplication.Run(new PlayerCatalog([new ApplicationPlayerProvider(new ApplicationCatalog(MotionPlugins.Catalog()),graphics).Contribution()]),new(source));
