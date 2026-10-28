using System.Text.Json;
using BoardGame.Kernel;
using BoardGame.Runtime;
using BoardGame.View;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var target=new Target();
using var host=await BoardInteractionPlugin.StartAsync(target);
var input=host.Service<IBoardInteraction>(BoardInteractionPlugin.Capability);
var first=input.SubmitAsync(1,0,new("action",Action:"take"));
Check(target.Calls==1 && !first.IsCompleted,"Target was not awaited");
Check(!(await input.SubmitAsync(1,0,new("action",Action:"take"))).Accepted,"Duplicate accepted");
Check((await input.SubmitAsync(2,0,new("action",Action:"take"))).Reason=="busy","Busy request queued");
target.Release.TrySetResult();Check((await first).Accepted && target.Calls==1,"Multiple submissions");
Check((await input.SubmitAsync(2,1,new("action",Action:"take"))).Reason=="duplicate-input","Consumed busy input reused");
Check((await input.SubmitAsync(3,0,new("action",Action:"take"))).Reason=="stale-or-inactive","Stale revision accepted");
Check(host.Driver.CompletedTick==0,"Interaction advanced a second simulation");
target.Active=false;
Check(!(await input.SubmitAsync(4,1,new("action",Action:"take"))).Accepted && target.Calls==1,"Read-only view obtained submit");
target.Active=true;
using var secondHost=await BoardInteractionPlugin.StartAsync(target);
var second=secondHost.Service<IBoardInteraction>(BoardInteractionPlugin.Capability);
Check((await second.SubmitAsync(1,1,new("action",Action:"take"))).Accepted,"Second authorized viewport failed");
Check(!(await input.SubmitAsync(5,1,new("action",Action:"take"))).Accepted && target.Calls==2,"Second viewport resubmitted old frame");
var pendingTarget=new Target();var pendingHost=await BoardInteractionPlugin.StartAsync(pendingTarget);
var pending=pendingHost.Service<IBoardInteraction>(BoardInteractionPlugin.Capability).SubmitAsync(1,0,new("action",Action:"take"));
await pendingHost.DisposeAsync();Check((await pending).Reason=="canceled","Closing plugin did not cancel input");
RuntimePluginSelection Select(RuntimePluginDefinition p)=>new(p.Id,p.Version,p.Api,JsonSerializer.SerializeToElement(new{}));
var renderer=new RuntimePluginDefinition("reader","1",1,[],[BoardInteractionPlugin.Target],_=>{});
var bound=BoardInteractionPlugin.Bind(target);
try {RuntimeHost.Validate(new(1,"reader","1",[Select(bound),Select(renderer)]),[bound,renderer],[]);throw new Exception("Renderer acquired submit");}
catch(InvalidDataException error){Check(error.Message.Contains("permission denied"),"Wrong permission failure");}
var plugin=BoardInteractionPlugin.Create();
try {RuntimeHost.Validate(new(1,"observer","1",[Select(plugin)]),[plugin],[]);throw new Exception("Observer interaction loaded without submit target");}
catch(InvalidDataException){}
var gestures=new List<TableGesture>();var pointer=new TableInteraction();pointer.Gesture+=gestures.Add;
pointer.Show(new([new("one","card","visible",new(0,0,40,60)),new("zone","region","target",new(80,0,40,60))],ResourcePool.Empty));
pointer.Pointer("down",10,10);pointer.Pointer("move",90,10);pointer.Pointer("up",90,10);pointer.Pointer("up",90,10);
Check(gestures.Count==1,"Duplicate pointer up resubmitted");
pointer.Pointer("down",10,10);pointer.Enabled=false;pointer.Enabled=true;pointer.Pointer("up",90,10);
Check(gestures.Count==1,"Busy state retained drag");
pointer.Pointer("right-down",10,10);pointer.Pointer("cancel",0,0);Check(pointer.Peeked==null && pointer.Selected==null,"Focus cancel retained private state");
foreach(var dependency in typeof(BoardInteractionPlugin).Assembly.GetReferencedAssemblies())
    Check(dependency.Name is not ("Runtime" or "Lua" or "MonoGame.Framework" or "Rendering.WindowsDX"),"Interaction implementation depends on rule/device");
Console.WriteLine("PASS independent interaction, deduplication, revision, seat-bound permission, cancellation and pointer lifecycle");

sealed class Target : IBoardInteractionTarget
{
    public int Calls;public bool Active=true;public long Revision;
    public TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public InteractionFrame Read()=>new(Revision,[new("take","Take",new(){Kind="take"})],Active);
    public async Task<CommandResult> SubmitAsync(long revision,Command command,CancellationToken cancellation)
    {
        Interlocked.Increment(ref Calls);await Release.Task.WaitAsync(cancellation);
        if(command.Kind!="take" || Interlocked.CompareExchange(ref Revision,revision+1,revision)!=revision)return new(0,false,"stale");
        return new(Revision,true,"");
    }
}
