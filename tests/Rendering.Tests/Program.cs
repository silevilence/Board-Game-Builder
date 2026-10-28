using System.Collections.Concurrent;
using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Resources;
using BoardGame.Rendering;
using BoardGame.Runtime;

static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
static void Reject(Action action,string message)
{try {action();}catch(Exception error) when(error.ToString().Contains(message,StringComparison.OrdinalIgnoreCase)) {return;}throw new Exception("Expected: "+message);}
static RuntimeStartup Startup(params RuntimePluginDefinition[] plugins)=>new(1,"drawing-test","1",
    plugins.Select(p=>new RuntimePluginSelection(p.Id,p.Version,p.Api,JsonSerializer.SerializeToElement(new{}))).ToList());
foreach(var assembly in new[]{typeof(IDraw2D).Assembly,typeof(RenderingHost).Assembly})
    Check(!assembly.GetReferencedAssemblies().Any(a=>a.Name is "Kernel.Abstractions" or "Kernel" or "Runtime" or "MonoGame.Framework" or "Avalonia.Base"),"Rendering contracts depend on board/platform implementation");
using var thread=new GraphicsThread();
var provider=new Backend();
var plugin=RenderingPlugin.Backend(provider.Descriptor,()=>provider);
var alternative=RenderingPlugin.Backend(provider.Descriptor with {Id="other"},()=>new Backend());
Reject(()=>RenderingHost.StartAsync(Startup(),[],thread.Invoke).GetAwaiter().GetResult(),"Missing required");
Reject(()=>RenderingHost.StartAsync(Startup(plugin,alternative),[plugin,alternative],thread.Invoke).GetAwaiter().GetResult(),"duplicate");
var wrong=plugin with {Execution=new(HostMode.Graphical,RuntimeScope.Instance)};
Reject(()=>RenderingHost.StartAsync(Startup(wrong),[wrong],thread.Invoke).GetAwaiter().GetResult(),"host scope");
using(var headless=await RuntimeHost.StartAsync(Startup(plugin),[plugin],[]))
    await headless.Driver.AdvanceAsync(TimeSpan.FromSeconds(1));
Check(provider.Created==0,"Headless started graphics");
var host=await RenderingHost.StartAsync(Startup(plugin),[plugin],thread.Invoke);
var backend=host.Backend;
Reject(()=>backend.CreateViewport(new(1,100,100)),"graphics thread");
RenderViewport first=null!,second=null!;IDraw2D? stale=null;
var frame=new DrawFrame(new(0,0,0),new(1));
thread.Invoke(()=>
{
    Reject(()=>backend.CreateViewport(new(0,100,100)),"ViewportTarget");
    first=backend.CreateViewport(new(1,100,100));second=backend.CreateViewport(new(2,200,100,2));
    first.Render(frame,draw=>
    {
        stale=draw;draw.ConfigureResources(AssetSet.Empty);draw.Box(new(1,2,3,4),new(255,0,0));
        Reject(()=>first.Render(frame,_=>{}),"reentry");
        Reject(()=>first.Dispose(),"during a frame");
        Reject(()=>backend.Dispose(),"during a frame");
    });
    Reject(()=>stale!.Text("late",0,0,new()),"expired");
    Reject(()=>first.Render(frame,_=>throw new InvalidDataException("failed frame")),"failed frame");
    second.Render(frame,draw=>draw.Text("中文",0,0,new(255,255,255)));
    using var png=new MemoryStream();first.Capture(frame,_=>{},png);Check(png.Length==1,"Capture not dispatched");
    Check(first.Metrics.Labels==0 && second.Metrics.Labels==1,"Viewport caches shared");
    first.Dispose();first.Dispose();
    Reject(()=>first.Render(frame,_=>{}),"disposed");
    provider.FailNext=true;Reject(()=>backend.CreateViewport(new(3,100,100)),"device failure");
    Check(!provider.Disposed && provider.Live==1,"Failed device affected existing viewport");
});
// A gameplay instance may close while its graphics host and other surfaces remain alive.
var drawing=new RuntimePluginDefinition("display","1",1,["display"],[RenderingPlugin.Capability],scope=>
{
    var surface=scope.Own(scope.Service<RenderBackend>(RenderingPlugin.Capability).CreateViewport(new(4,100,100)));
    scope.Provide("display",surface);scope.Render("draw",_=>surface.Render(frame,draw=>draw.Box(new(0,0,10,10),new(1,2,3))));
}) {Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
var full=Startup(plugin,drawing);RuntimePluginDefinition[] catalog=[plugin,drawing];
var instance=await RuntimeHost.StartScopedAsync(full,catalog,[],HostMode.Graphical,RuntimeScope.Instance,host.Runtime);
var viewport=await RuntimeHost.StartScopedAsync(full,catalog,[],HostMode.Graphical,RuntimeScope.Viewport,instance,dispatch:thread.Invoke);
thread.Invoke(()=>viewport.Render(TimeSpan.FromMilliseconds(16)));
await instance.DisposeAsync();
thread.Invoke(()=>Check(provider.Live==1 && !provider.Disposed,"Instance closed shared graphics host"));
await host.DisposeAsync();await host.DisposeAsync();
Check(provider.Live==0 && provider.Disposed,"Graphics shutdown leaked surfaces");
Check(provider.Events.Last()=="backend","Backend released before viewports");
thread.Invoke(()=>Reject(()=>second.Render(frame,_=>{}),"disposed"));
Console.WriteLine("PASS rendering capability selection, scope/thread checks, isolated surfaces and safe teardown");

sealed class GraphicsThread : IDisposable
{
    private readonly BlockingCollection<Action> work=new();
    private readonly Thread thread;
    public GraphicsThread() {thread=new(()=>{foreach(var action in work.GetConsumingEnumerable())action();});thread.Start();}
    public void Invoke(Action action)
    {
        if(Environment.CurrentManagedThreadId==thread.ManagedThreadId) {action();return;}
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Add(()=>{try {action();done.SetResult();}catch(Exception error) {done.SetException(error);}});
        done.Task.GetAwaiter().GetResult();
    }
    public void Dispose() {work.CompleteAdding();thread.Join();work.Dispose();}
}
sealed class Backend : IRenderBackend
{
    public RenderBackendDescriptor Descriptor=>new("test.backend","1","test",false);
    public int Created,Live;public bool Disposed,FailNext;public List<string> Events=[];
    public IRenderSurface CreateViewport(ViewportTarget target)
    {if(FailNext) {FailNext=false;throw new InvalidOperationException("device failure");}Created++;Live++;return new Surface(this);}
    public void Dispose() {Disposed=true;Events.Add("backend");}
    private sealed class Surface(Backend owner) : IRenderSurface,IDraw2D
    {
        private int labels;
        public RenderMetrics Metrics=>new(0,labels,labels);
        public void Render(DrawFrame frame,Action<IDraw2D> draw)=>draw(this);
        public void Capture(DrawFrame frame,Action<IDraw2D> draw,Stream png) {draw(this);png.WriteByte(1);}
        public void Dispose() {owner.Live--;owner.Events.Add("surface");}
        public void ConfigureResources(AssetSet resources) { }
        public void Box(DrawRect bounds,DrawColor color) { }
        public void Line(DrawPoint from,DrawPoint to,DrawColor color) { }
        public void Text(string text,int x,int y,DrawColor color,int size=13)=>labels++;
        public void Image(string name,AssetSet resources,DrawRect bounds,bool trimTransparent=false,float rotation=0) { }
    }
}
