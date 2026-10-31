using BoardGame.Runtime;

if(args.Contains("dispatch-isolation")) {await DispatchIsolationTests.Run();return;}
if(args.Contains("preparation-isolation")) {await PreparationIsolationTests.Run();return;}

static void Assert(bool condition,string message) {if(!condition) throw new Exception(message);}
static void Reject(Action action,string reason)
{
    try {action();} catch(InvalidDataException e) when(e.Message.Contains(reason,StringComparison.OrdinalIgnoreCase)) {return;}
    throw new Exception("Expected rejection: "+reason);
}
ScheduledContribution Update(string plugin,string[]? before=null,string[]? after=null)=>new(plugin,RuntimeScope.Instance,new("step",ApplicationPhase.Update,before,after));
var declarations=new[]{Update("z",["a/step"]),Update("a"),Update("m",after:["a/step"])};
Assert(string.Join(',',PhaseOrder.Sort(declarations).Select(p=>p.Plugin))=="z,a,m","Explicit update order");
Assert(PhaseOrder.Sort(declarations.Reverse()).Select(p=>p.Key).SequenceEqual(PhaseOrder.Sort(declarations).Select(p=>p.Key)),"Order depends on registration");
Reject(()=>PhaseOrder.Sort([Update("a",["b/step"]),Update("b",["a/step"])]),"cycle");
Reject(()=>PhaseOrder.Sort([Update("a",["absent/step"])]),"missing");
Reject(()=>PhaseOrder.Sort([Update("a"),Update("a")]),"duplicate");
Reject(()=>PhaseOrder.Sort([new("a",RuntimeScope.Viewport,new("step",ApplicationPhase.Update))]),"scope");
Reject(()=>PhaseOrder.Sort([Update("a",["b/draw"]),new("b",RuntimeScope.Viewport,new("draw",ApplicationPhase.Render))]),"cross-phase");
Assert(PhaseOrder.Sort([]).Count==0,"Interface-only plugins require no callbacks");
foreach(var assembly in new[]{typeof(PhaseOrder).Assembly,typeof(UpdateFrame).Assembly})
    Assert(!assembly.GetReferencedAssemblies().Any(a=>a.Name is "Kernel" or "Kernel.Abstractions" or "Editor" or "Editor.Core" or "MonoGame.Framework"),"Generic assembly references board/UI types");
new ApplicationClockOptions().Validate();
Console.WriteLine("PASS application contracts and phase ordering");
await DriverTests.Run();
ContentTests.Run();
await DispatchIsolationTests.Run();
await PreparationIsolationTests.Run();
