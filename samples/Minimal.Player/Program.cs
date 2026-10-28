using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Player;
using BoardGame.Rendering;
using Sample.Motion;

var backend=WindowsDxBackend.Plugin();
var graphics=new GraphicsConfiguration(new(1,"graphics","1",[new(backend.Id,backend.Version,backend.Api,JsonSerializer.SerializeToElement(new{}))]),[backend]);
var bundled=Path.Combine(AppContext.BaseDirectory,"game.bgm");
var source=args.FirstOrDefault() ?? (File.Exists(bundled)?bundled:"samples/motion-lab");
return PlayerApplication.Run(new PlayerCatalog([new ApplicationPlayerProvider(new ApplicationCatalog(MotionPlugins.Catalog()),graphics).Contribution()]),new(source));
