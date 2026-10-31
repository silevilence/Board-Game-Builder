using System.Security.Cryptography;
using BoardGame.Plugins;
using BoardGame.Rendering;

if(args.Length!=1)throw new ArgumentException("Usage: GraphicsBackendPack <new-package-directory>");
var destination=Path.GetFullPath(args[0]);
if(Directory.Exists(destination) || File.Exists(destination))throw new IOException("Backend destination must be new");
var parent=Path.GetDirectoryName(destination)!;Directory.CreateDirectory(parent);
var stage=Path.Combine(parent,".backend-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
try
{
    var files=new List<PluginFile>();
    foreach(var (name,role) in new[]{("Rendering.DesktopGL.dll","implementation"),("MonoGame.Framework.dll","managed"),("NVorbis.dll","managed"),("SkiaSharp.dll","managed"),("SDL2.dll","native"),("libSkiaSharp.dll","native")})
    {
        var path=Path.Combine(AppContext.BaseDirectory,name);
        if(!File.Exists(path))path=Path.Combine(AppContext.BaseDirectory,"runtimes/win-x64/native",name);
        File.Copy(path,Path.Combine(stage,name));using var stream=File.OpenRead(path);
        files.Add(new(name,Convert.ToHexStringLower(SHA256.HashData(stream)),role));
    }
    var plugin=DesktopGlBackend.Plugin();
    var manifest=new PluginManifest(1,"runtime",plugin.Id,plugin.Version,plugin.Api,new("Rendering.DesktopGL.dll",typeof(DesktopGlBackend).FullName!,"Create"),["win-x64"],
        new(true,["win-x64"],"Pinned MonoGame source patch; target compilation and execution remain required"),[new(RenderingPlugin.Capability,1)],[],plugin.Execution,files.ToArray());
    File.WriteAllText(Path.Combine(stage,PluginManifest.FileName),manifest.Write());
    var store=new PluginStore(stage,"win-x64",[typeof(RenderBackend).Assembly,typeof(IDraw2D).Assembly,typeof(BoardGame.Resources.AssetSet).Assembly]);
    using(var lease=store.AcquirePackage(stage,plugin.Id))_=lease.Plugin.Bind(null);
    Directory.Move(stage,destination);Console.WriteLine(destination);
}
finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
