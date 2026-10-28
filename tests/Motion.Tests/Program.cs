using BoardGame.AppRuntime;
using BoardGame.Rendering;
using BoardGame.Runtime;
using Sample.Motion;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var catalog=new ApplicationCatalog(MotionPlugins.Catalog());var content=catalog.Load("samples/motion-lab");
var package=Path.GetFullPath(".scratch/application-loop/motion-"+Guid.NewGuid().ToString("N")+".bgm");
GamePackage.Pack("samples/motion-lab",package,catalog.Content);Check(catalog.Load(package).Fingerprint==content.Fingerprint,"Motion package differs");
var options=new ApplicationClockOptions(TimeSpan.FromMilliseconds(10),100,TimeSpan.FromSeconds(1));MotionSnapshot? expected=null;
foreach(var cadence in new[]{10,20,50,100})
{
    await using var host=await catalog.StartAsync(content,clock:options);var motion=host.Service<MotionState>("motion.state");var view=host.Service<IApplicationView>(ApplicationViewPlugin.Capability);
    var key=view.Translate(new("key-down",Key:"right"))!;
    Check(host.Driver.Enqueue(new(1,key.Kind,key.Data)) && !host.Driver.Enqueue(new(1,key.Kind,key.Data)),"Input sequence consumed twice");
    for(var time=0;time<1000;time+=cadence)await host.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(cadence));
    expected ??=motion.Snapshot;Check(motion.Snapshot==expected && Math.Abs(motion.Snapshot.X-200)<.01,"Cadence changes motion");
    var before=motion.Snapshot;
    var draw=new Draw();for(var frame=0;frame<120;frame++)view.Draw(draw,new("test",TimeSpan.FromMilliseconds(frame*7),TimeSpan.FromMilliseconds(7),host.Driver.CompletedTick,0));
    Check(motion.Snapshot==before && draw.Images==120,"Rendering advanced simulation or skipped assets");
    host.Driver.Paused=true;await host.Driver.AdvanceAsync(TimeSpan.FromSeconds(1));Check(motion.Snapshot==before,"Pause moved state");
    await host.Driver.StepAsync();Check(motion.Snapshot.Tick==before.Tick+1 && motion.Snapshot.X>before.X,"Step is not one update");
    var cancel=view.Translate(new("cancel"))!;host.Driver.Enqueue(cancel.Kind,cancel.Data);await host.Driver.StepAsync();var stopped=motion.Snapshot;
    await host.Driver.StepAsync();Check(motion.Snapshot.X==stopped.X,"Focus loss retained movement");
    host.Driver.Enqueue(key.Kind,key.Data);host.Driver.Paused=false;
    for(var time=0;time<3000;time+=100)await host.Driver.AdvanceAsync(TimeSpan.FromMilliseconds(100));
    Check(motion.Snapshot.Reached,"Target feedback never reached");
}
await using(var host=await catalog.StartAsync(content,clock:options))
{
    await using var pump=new ApplicationPump(host.Driver);
    for(var wait=0;host.Driver.CompletedTick==0 && wait<100;wait++)await Task.Delay(10);
    Check(host.Driver.CompletedTick>0,"Continuous pump did not update");await pump.SetPausedAsync(true);
    var tick=host.Driver.CompletedTick;await Task.Delay(40);Check(host.Driver.CompletedTick==tick,"Pump paused boundary changed");
    await pump.StepAsync();Check(host.Driver.CompletedTick==tick+1,"Pump step raced wall clock");
}
var deps=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Motion.Tests.deps.json"));
foreach(var omitted in new[]{"Kernel.Abstractions/","BoardGame.Core/","BoardGame.Session/","View.Abstractions/","MonoGame.Framework/","Lua/","Wasmtime/"})
    Check(!deps.Contains(omitted),"Motion linked "+omitted);
Console.WriteLine("PASS real-time content, fixed-time/input determinism, render independence, pause/step, pump, targets and no-board dependencies");

sealed class Draw : IDraw2D
{
    public int Images;
    public void ConfigureResources(BoardGame.Resources.AssetSet resources){}
    public void Box(DrawRect bounds,DrawColor color){}
    public void Line(DrawPoint from,DrawPoint to,DrawColor color){}
    public void Text(string text,int x,int y,DrawColor color,int size=13){}
    public void Image(string name,BoardGame.Resources.AssetSet resources,DrawRect bounds,bool trimTransparent=false,float rotation=0){_=resources.GetImage(name);Images++;}
}
