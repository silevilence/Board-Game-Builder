using System.Text.Json;
using System.Text.Json.Nodes;
using BoardGame.AppRuntime;
using BoardGame.Kernel;
using BoardGame.Resources;
using BoardGame.Runtime;

static void Check(bool value,string reason) {if(!value)throw new Exception(reason);}
static void Reject(Action action,string reason)
{
    try {action();}catch(Exception error) when(error.ToString().Contains(reason,StringComparison.OrdinalIgnoreCase)) {return;}
    throw new Exception("Expected rejection: "+reason);
}
var json=new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
var root=Path.Combine(Path.GetTempPath(),"resource-contract-"+Guid.NewGuid());
var source=Path.Combine(root,"source");Directory.CreateDirectory(Path.Combine(source,"assets"));
try
{
    foreach(var assembly in new[]{typeof(AssetSet).Assembly,typeof(AssetRegistry).Assembly,typeof(RuntimeHost).Assembly})
        Check(!assembly.GetReferencedAssemblies().Any(a=>a.Name is "Kernel" or "Kernel.Abstractions" or "Runtime" or "BoardGame.Core" or "MonoGame.Framework"),"Generic resources depend on board or device types");
    var assetPlugins=AssetPlugins.Catalog().ToArray();
    Check(assetPlugins.All(p=>p.Execution.Contributions==null),"Resource providers registered empty frame callbacks");
    File.Copy("samples/poker-uno/assets/cards.png",Path.Combine(source,"assets/atlas.png"));
    File.Copy("samples/poker-uno/assets/cards.png",Path.Combine(source,"assets/single.png"));
    var original=JsonNode.Parse(File.ReadAllText("samples/poker-uno/assets/cards.json"))!;
    var atlas=new JsonObject
    {
        ["frames"]=JsonNode.Parse("""{"tile":{"frame":{"x":2,"y":2,"w":10,"h":20},"rotated":true,"trimmed":true,"spriteSourceSize":{"x":3,"y":4,"w":20,"h":10},"sourceSize":{"w":30,"h":40}}}"""),
        ["meta"]=new JsonObject {["image"]="atlas.png",["size"]=original["meta"]!["size"]!.DeepClone()}
    };
    File.WriteAllText(Path.Combine(source,"assets/atlas.json"),atlas.ToJsonString());
    var font="samples/cursed-investigators/assets/fonts/noto-sans-cjk-sc/";
    File.Copy(font+"NotoSansCJKsc-Regular.otf",Path.Combine(source,"font.otf"));
    File.Copy(font+"LICENSE.txt",Path.Combine(source,"LICENSE.txt"));
    File.WriteAllText(Path.Combine(source,"motion.json"),"{\"speed\":1}");
    RuntimePluginSelection Select(string id,object config)=>new(id,"1",1,JsonSerializer.SerializeToElement(config,json));
    var startup=new RuntimeStartup(1,"motion-assets","1",
    [Select("motion",new{path="motion.json"}),Select("resources",new{}),
     Select("resources.images",new{sources=new[]{new{path="assets/single.png",name="single"}}}),
     Select("resources.texturepacker",new{sources=new[]{new{path="assets",pattern="*.json",prefix="tiles/"}}}),
     Select("resources.font",new{path="font.otf",license="LICENSE.txt"})]);
    void Save(RuntimeStartup value)=>File.WriteAllText(Path.Combine(source,GameRuntime.Entry),JsonSerializer.Serialize(value,json));
    Save(startup);
    var loader=new RuntimePluginDefinition("motion","1",1,["definition"],["resources"],scope=>
        scope.Provide("definition",new MotionAssets(scope.Service<AssetRegistry>("resources"))))
        {Content=new(["path"],(config,_)=>ContentPath.Validate(config.GetProperty("path").GetString()!),Prepare:true,
            Definition:config=>new(config.GetProperty("path").GetString()!,new Dictionary<string,string>()))};
    var catalog=new ContentCatalog(assetPlugins.Append(loader));
    var loaded=GameRuntime.Load(source,catalog);
    Check(loaded.Identity.Files.Count==6,"Wildcard/font dependencies not captured");
    Check(loaded.Payload.Data.GetProperty("image").GetProperty("rotated").GetBoolean(),"Rotated atlas descriptor lost");
    var package=Path.Combine(root,"assets.bgm");GamePackage.Pack(source,package,catalog);
    Check(GameRuntime.Load(package,catalog).Fingerprint==loaded.Fingerprint,"Directory/package resource identity differs");
    var duplicate=startup with {Plugins=startup.Plugins.Select(p=>p.Id=="resources.images"?
        Select(p.Id,new{sources=new[]{new{path="assets/single.png",name="single"},new{path="assets/single.png",name="single"}}}):p).ToList()};
    Save(duplicate);Reject(()=>GameRuntime.Load(source,catalog),"conflicting resource name");
    Save(startup with {Plugins=startup.Plugins.Select(p=>p.Id=="resources.font"?Select(p.Id,new{path="missing.otf",license="LICENSE.txt"}):p).ToList()});
    Reject(()=>GameRuntime.Load(source,catalog),"missing");Save(startup);
    using(var host=RuntimeHost.StartAsync(startup with {Plugins=startup.Plugins.Where(p=>p.Id!="motion").ToList()},assetPlugins,["resources"]).GetAwaiter().GetResult())
    {
        var assets=host.Service<AssetRegistry>("resources").Load(new DirectoryResourceInput(source));
        Check(assets.Font==new FontReference("font.otf","LICENSE.txt"),"Font descriptor differs");
        Check(assets.GetImage("tiles/tile").Destination(10,20,300,400)==(40f,60f,200f,100f),"Trim/rotation placement differs");
        Reject(()=>assets.GetImage("absent"),"unknown image");
    }
    Directory.Delete(source,true);
    Check(GameRuntime.Load(package,catalog).Fingerprint==loaded.Fingerprint,"Package still needs original resource directory");
    Check(GameRuntime.Restore(loaded.ExportSnapshot(),catalog).Fingerprint==loaded.Fingerprint,"Snapshot resource restore differs");
    Console.WriteLine("PASS generic resource graph, atlas/font metadata, discovery, package and snapshot");
}
finally {if(Directory.Exists(root))Directory.Delete(root,true);}

sealed class MotionAssets(AssetRegistry assets) : IContentLoader
{
    public string PayloadFormat=>"test.motion-assets";
    public int PayloadVersion=>1;
    public JsonElement Load(IContentSource source)
    {
        if(source.Enumerate(".").Any(p=>p.Equals(GameRuntime.Entry,StringComparison.OrdinalIgnoreCase))) throw new Exception("Reserved entry exposed as an asset");
        try {source.Enumerate("../outside");throw new Exception("Enumeration escaped content root");}
        catch(InvalidDataException) { }
        using var definition=JsonDocument.Parse(source.Read("motion.json"));
        var pool=assets.Load(new ContentResourceInput(source));
        return JsonSerializer.SerializeToElement(new{speed=definition.RootElement.GetProperty("speed").GetDouble(),image=pool.GetImage("tiles/tile"),font=pool.Font},new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
    }
}
