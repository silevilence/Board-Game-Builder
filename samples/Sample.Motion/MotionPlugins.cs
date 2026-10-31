using System.Text.Json;
using BoardGame.AppRuntime;
using BoardGame.Rendering;
using BoardGame.Resources;
using BoardGame.Runtime;

namespace Sample.Motion;

public sealed record MotionSettings(string Title,string Instructions,string Success,float Width,float Height,float Speed,float StartX,float StartY,float TargetX,float TargetY);
public sealed record MotionSnapshot(float X,float Y,bool Reached,long Tick);
internal sealed record MotionPayload(MotionSettings Settings,AssetSet Assets);
internal sealed record MotionKey(string Key,bool Pressed,string Source);

public static class MotionPlugins
{
    internal static readonly JsonSerializerOptions Json=MotionJson.Default.Options;
    public static IEnumerable<ApprovedApplicationPlugin> Catalog()
    {
        yield return Definition();
        foreach(var plugin in AssetPlugins.Catalog())yield return new(_=>plugin);
        yield return Loop();yield return ViewFactory();
    }
    public static ApprovedApplicationPlugin Definition()=>new(_=>new("motion.definition","1",1,["definition"],["resources"],s=>s.Provide("definition",new Loader(s.Configuration.GetProperty("path").GetString()!,s.Service<AssetRegistry>("resources"))))
        {Content=new(["path"],RequiredCapabilities:["motion.state"],Prepare:true,Definition:c=>new(c.GetProperty("path").GetString()!,new Dictionary<string,string>()))});
    public static ApprovedApplicationPlugin Loop()=>new(content=>new("motion.loop","1",1,["motion.state"],[],s=>
        {
            var payload=content!.Payload.Data.Deserialize(MotionJson.Default.MotionPayload) ?? throw new InvalidDataException("Missing motion payload");
            var motion=new MotionState(payload);s.Provide("motion.state",motion);s.Update("advance",motion.Update);
        }){Execution=new(Contributions:[new("advance",ApplicationPhase.Update)])});
    public static ApprovedApplicationPlugin ViewFactory()=>new(_=>new("motion.view","1",1,[ApplicationViewPlugin.Capability],["motion.state"],s=>s.Provide(ApplicationViewPlugin.Capability,new View(s.Service<MotionState>("motion.state")))));
    private sealed class Loader(string path,AssetRegistry registry) : IContentLoader
    {
        public string PayloadFormat=>"sample.motion";
        public int PayloadVersion=>1;
        public JsonElement Load(IContentSource source)
        {
            var config=JsonSerializer.Deserialize(source.Read(path).Span,MotionJson.Default.MotionSettings) ?? throw new InvalidDataException("Missing motion settings");
            if(string.IsNullOrWhiteSpace(config.Title) || config.Title.Length>80 || config.Instructions==null || config.Instructions.Length>256 || config.Success==null || config.Success.Length>256
                || new[]{config.Width,config.Height,config.Speed,config.StartX,config.StartY,config.TargetX,config.TargetY}.Any(v=>!float.IsFinite(v))
                || config.Width is <200 or >2048 || config.Height is <200 or >2048 || config.Speed is <10 or >800
                || config.StartX<16 || config.StartX>config.Width-16 || config.StartY<64 || config.StartY>config.Height-48
                || config.TargetX<16 || config.TargetX>config.Width-16 || config.TargetY<64 || config.TargetY>config.Height-48)
                throw new InvalidDataException("Invalid motion settings");
            var assets=registry.Load(new ContentResourceInput(source));_=assets.GetImage("marker");
            return JsonSerializer.SerializeToElement(new MotionPayload(config,assets),MotionJson.Default.MotionPayload);
        }
    }
    private sealed class View(MotionState state) : IApplicationView
    {
        public ApplicationViewDescription Description=>new(state.Settings.Title,state.Settings.Width,state.Settings.Height,new(14,27,37));
        public ViewInput? Translate(ViewportInput input)
        {
            if(input.Kind=="cancel")return new("motion.cancel",JsonSerializer.SerializeToUtf8Bytes(input.Source ?? "",MotionJson.Default.String));
            if(input.Kind is not ("key-down" or "key-up") || input.Key is not ("left" or "right" or "up" or "down" or "space"))return null;
            return new("motion.key",JsonSerializer.SerializeToUtf8Bytes(new MotionKey(input.Key,input.Kind=="key-down",input.Source ?? ""),MotionJson.Default.MotionKey));
        }
        public void Draw(IDraw2D draw,RenderFrame frame)
        {
            var settings=state.Settings;var value=state.Snapshot;draw.ConfigureResources(state.Assets);
            for(var x=32;x<settings.Width;x+=32)draw.Line(new(x,64),new(x,settings.Height-48),new(26,43,54));
            for(var y=64;y<settings.Height-48;y+=32)draw.Line(new(16,y),new(settings.Width-16,y),new(26,43,54));
            draw.Text(settings.Title,24,20,new(220,236,240),22);
            draw.Box(new((int)settings.TargetX-20,(int)settings.TargetY-20,40,40),value.Reached?new(103,205,153):new(76,116,118));
            draw.Image("marker",state.Assets,new((int)value.X-14,(int)value.Y-14,28,28));
            draw.Text(value.Reached?settings.Success:settings.Instructions,24,(int)settings.Height-30,new(192,215,222),14);
        }
    }
}

public sealed class MotionState
{
    private MotionSnapshot snapshot;
    private Dictionary<string,HashSet<string>> held=[];
    internal MotionSettings Settings {get;}
    internal AssetSet Assets {get;}
    public MotionSnapshot Snapshot=>Volatile.Read(ref snapshot);
    internal MotionState(MotionPayload content)
    {Settings=content.Settings;Assets=content.Assets;snapshot=new(Settings.StartX,Settings.StartY,false,0);}
    internal ValueTask<Action?> Update(UpdateFrame frame,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();var sources=held.ToDictionary(p=>p.Key,p=>new HashSet<string>(p.Value));var current=Snapshot;
        foreach(var input in frame.Inputs)
        {
            if(input.Kind=="motion.cancel"){sources.Remove(JsonSerializer.Deserialize(input.Data.Span,MotionJson.Default.String) ?? "");continue;}
            if(input.Kind!="motion.key")continue;
            var key=JsonSerializer.Deserialize(input.Data.Span,MotionJson.Default.MotionKey) ?? throw new InvalidDataException("Invalid motion input");
            if(key.Key=="space" && key.Pressed){current=new(Settings.StartX,Settings.StartY,false,current.Tick);sources.Clear();}
            else if(key.Key is "left" or "right" or "up" or "down")
            {
                if(!sources.TryGetValue(key.Source,out var pressed))sources[key.Source]=pressed=[];
                if(key.Pressed)pressed.Add(key.Key);else pressed.Remove(key.Key);
                if(pressed.Count==0)sources.Remove(key.Source);
            }
        }
        var keys=sources.Values.SelectMany(v=>v).ToHashSet();
        var dx=(keys.Contains("right")?1:0)-(keys.Contains("left")?1:0);var dy=(keys.Contains("down")?1:0)-(keys.Contains("up")?1:0);
        var distance=(float)(Settings.Speed*frame.Delta.TotalSeconds)/(dx!=0 && dy!=0?MathF.Sqrt(2):1);
        var x=Math.Clamp(current.X+dx*distance,16,Settings.Width-16);var y=Math.Clamp(current.Y+dy*distance,64,Settings.Height-48);
        var reached=current.Reached || MathF.Abs(x-Settings.TargetX)<18 && MathF.Abs(y-Settings.TargetY)<18;
        var next=new MotionSnapshot(x,y,reached,frame.Tick);
        return ValueTask.FromResult<Action?>(()=>{held=sources;Volatile.Write(ref snapshot,next);});
    }
}
