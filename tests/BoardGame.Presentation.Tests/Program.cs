using System.Text;
using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Rendering;
using BoardGame.Resources;
using BoardGame.Runtime;
using BoardGame.View;

static void Check(bool result,string message) {if(!result)throw new Exception(message);}
foreach(var name in typeof(BoardRenderPlugin).Assembly.GetReferencedAssemblies())
    Check(name.Name is not ("Runtime" or "Lua" or "Lua.Execution" or "Wasm.Execution" or "MonoGame.Framework" or "Avalonia.Controls"),"Presentation depends on execution or graphics implementation");
var state=new GameState {Seats=[new("p1","a"),new("p2","b")],Phases=["play"],Phase="play",Entities=new(StringComparer.Ordinal)
{
    ["deck"]=new(){Id="deck",IsContainer=true,Ordered=true,Capacity=9,Visibility="hidden",Contents=["card"]},
    ["hand1"]=new(){Id="hand1",IsContainer=true,Capacity=9,Visibility="owner",Owner="p1"},
    ["hand2"]=new(){Id="hand2",IsContainer=true,Capacity=9,Visibility="owner",Owner="p2"},
    ["card"]=new(){Id="card",Container="deck",Visibility="hidden",Faces=["Secret ace"]}
}};
var module=new LoadedModule("render-test","1","render-fingerprint",state,"","","rules","cpu");
var engine=new GameEngine(state,42,module.Fingerprint);
Check(engine.Submit("p1",new(){Kind="draw",Entity="deck",Target="hand1"}).Accepted,"Fixture draw failed");
var replay=GameEngine.Replay(module,engine.ExportLog());
var captions=Encoding.UTF8.GetBytes("""{"format":1,"captions":[{"text":"Private caption","anchor":"card","x":0,"y":0},{"text":"Authored heading","anchor":"hand1","x":0,"y":-20}]}""");
var resources=new ResourcePool(new Dictionary<string,ReadOnlyMemory<byte>>{{"board.json",captions}},new Dictionary<string,ImageReference>(),new Dictionary<string,string>{{"board.presentation","board.json"}});
var before=Wire.Json(engine.ExportLog());string? expected=null;
foreach(var source in new ISeatProjection[]{new Projection(engine,"p2"),new Projection(replay,"p2"),new Remote(Wire.Copy(engine.Project("p2")))})
{
    var draw=new RecordingDraw();var target=new DrawingTarget();
    var input=BoardRenderPlugin.Input(new ProjectedBoardVisual(source,resources));var renderer=BoardRenderPlugin.Create();
    Check(renderer.Requires.SequenceEqual(new[]{BoardRenderPlugin.Source,"render.target"}) && renderer.Permissions.Length==0,"Renderer requests privileged services");
    var surface=new RuntimePluginDefinition("target","1",1,["render.target"],[],s=>s.Provide("render.target",target)) {Execution=new(HostMode.Graphical,RuntimeScope.Viewport)};
    RuntimePluginDefinition[] catalog=[input,renderer,surface];
    var startup=new RuntimeStartup(1,"view","1",catalog.Select(p=>new RuntimePluginSelection(p.Id,p.Version,p.Api,JsonSerializer.SerializeToElement(new{}))).ToList());
    using var instance=await RuntimeHost.StartScopedAsync(startup,catalog,[],HostMode.Graphical);
    using var view=await RuntimeHost.StartScopedAsync(startup,catalog,[],HostMode.Graphical,RuntimeScope.Viewport,instance);
    for(var frame=0;frame<5;frame++)
    {
        draw.Calls.Clear();target.Present(draw,()=>view.Render(TimeSpan.FromMilliseconds(16)));
        var result=string.Join('\n',draw.Calls);expected ??=result;
        Check(result==expected,"Live/replay/remote projection renders differ");
        Check(!result.Contains("Secret ace") && !result.Contains("Private caption") && result.Contains("Authored heading"),"Hidden state or missing authorized caption");
    }
    Check(instance.Driver.CompletedTick==0 && Wire.Json(engine.ExportLog())==before,"Render advanced authority/update");
    var alternative=new RuntimePluginDefinition("alternative.view","1",1,["board.render"],["render.target",BoardRenderPlugin.Source],s=>
    {var output=s.Service<IDrawingTarget>("render.target");s.Provide("board.render",new object());s.Render("draw",_=>output.Draw(d=>d.Text("alternate",0,0,new())));})
        {Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
    var switched=startup with {Plugins=startup.Plugins.Where(p=>p.Id!=renderer.Id).Append(new(alternative.Id,"1",1,JsonSerializer.SerializeToElement(new{}))).ToList()};
    using(var alternate=await RuntimeHost.StartScopedAsync(switched,[input,alternative,surface],[],HostMode.Graphical,RuntimeScope.Viewport,instance))
    {draw.Calls.Clear();target.Present(draw,()=>alternate.Render(TimeSpan.Zero));Check(draw.Calls.Single().Contains("alternate"),"Alternate provider was not used");}
    using var headless=await RuntimeHost.StartAsync(startup,catalog,[]);
    await headless.Driver.AdvanceAsync(TimeSpan.FromSeconds(1));
}
var own=new ProjectedBoardVisual(new Projection(engine,"p1"),resources).Read(new("own",TimeSpan.Zero,TimeSpan.Zero,0,0));
Check(own.Scene.Items.Any(i=>i.Label=="Private caption"),"Authorized anchor missing");
var style=TableScene.From(engine.Project("p1")) with {ClassicCards=true};var neutral=new RecordingDraw();ScenePainter.Draw(neutral,style);
Check(!neutral.Calls.Any(c=>c.Contains("FREECELL") || c.Contains("空 当") || c.Contains("收 牌")),"Theme selected game-specific labels");
var malformed=new ResourcePool(new Dictionary<string,ReadOnlyMemory<byte>>{{"bad",Encoding.UTF8.GetBytes("{\"format\":2,\"captions\":[]}")}},new Dictionary<string,ImageReference>(),new Dictionary<string,string>{{"board.presentation","bad"}});
try {TableScene.From(engine.Project("p1"),malformed);throw new Exception("Unknown caption version accepted");}catch(InvalidDataException) { }
Console.WriteLine("PASS board render plugin, live/replay/remote projections, replacement, hidden bindings and headless isolation");

sealed class Projection(GameEngine engine,string seat) : ISeatProjection {public SeatView View()=>engine.Project(seat);}
sealed class Remote(SeatView snapshot) : ISeatProjection {public SeatView View()=>Wire.Copy(snapshot);}
sealed class RecordingDraw : IDraw2D
{
    public List<string> Calls=[];
    public void ConfigureResources(AssetSet resources)=>Calls.Add("resources:"+resources.Images.Count);
    public void Box(DrawRect bounds,DrawColor color)=>Calls.Add("box:"+bounds+color);
    public void Line(DrawPoint from,DrawPoint to,DrawColor color)=>Calls.Add("line:"+from+to+color);
    public void Text(string text,int x,int y,DrawColor color,int size=13)=>Calls.Add($"text:{text}:{x}:{y}:{color}:{size}");
    public void Image(string name,AssetSet resources,DrawRect bounds,bool trimTransparent=false,float rotation=0)=>Calls.Add($"image:{name}:{bounds}:{trimTransparent}:{rotation}");
}
