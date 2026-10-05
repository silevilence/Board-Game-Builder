using BoardGame.Editor;
using BoardGame.Kernel;
namespace Engine.Tests;
public static class PluginTests
{
    private sealed class Release(Action action) : IDisposable { public void Dispose()=>action(); }
    private sealed class Plugin(Action<PluginContext> activate) : IEditorPlugin { public void Activate(PluginContext c)=>activate(c); }
    public static void Lifecycle()
    {
        using var host=new PluginHost(); var calls=0;
        host.Install(Wire.Json(new PluginManifest("a",1,["a"],[])),()=>new Plugin(c=> {
            foreach(var kind in Enum.GetValues<ExtensionKind>()) c.Register(kind,"key",new object());
            c.OnDocumentChanged(()=>calls++);
        }));
        for(var i=0;i<5;i++)
        {
            host.Enable("a"); Check.That(host.Extensions.Count==8); host.SetDocument(ModuleDocument.Create());
            Check.That(calls==i+1); host.Disable("a"); Check.That(host.Extensions.Count==0); host.SetDocument(null); Check.That(calls==i+1);
        }
        host.Install(Wire.Json(new PluginManifest("bad",1,[],[])),()=>new Plugin(c=> { c.Register(ExtensionKind.Panel,"orphan",new object()); c.OnDocumentChanged(()=>calls++); throw new Exception("activation failed"); }));
        Check.Throws(()=>host.Enable("bad"),"activation failed"); host.SetDocument(null);
        Check.That(host.Extensions.Count==0 && host.Active.Count==0 && calls==5);
    }
    public static void Dependencies()
    {
        using var host=new PluginHost(); var events=new List<string>(); var fail=false;
        foreach(var (id,requires) in new[] {("a",Array.Empty<string>()),("b",new[]{"a"}),("c",new[]{"b"}),("unrelated",Array.Empty<string>())})
            host.Install(Wire.Json(new PluginManifest(id,1,[id],requires)),()=>new Plugin(ctx=> {
                events.Add("+"+id); ctx.Own(new Release(()=>events.Add("-"+id))); ctx.Register(ExtensionKind.Service,id,new object());
                if(fail && id=="b") throw new Exception("broken b");
            }));
        host.Enable("unrelated"); host.Enable("c"); Check.That(string.Join(",",events)=="+unrelated,+a,+b,+c");
        events.Clear(); host.Reload("a"); Check.That(string.Join(",",events)=="-c,-b,-a,+a,+b,+c");
        events.Clear(); fail=true; Check.Throws(()=>host.Reload("a"),"broken b");
        Check.That(host.Active.SequenceEqual(["unrelated"]) && host.Extensions.Count==1,"Half enabled chain survived");
        fail=false; host.Enable("c"); host.Disable("a"); Check.That(host.Active.SequenceEqual(["unrelated"]));
        host.Install(Wire.Json(new PluginManifest("missing",1,[],["absent"])),()=>new Plugin(_=>{}));
        Check.Throws(()=>host.Enable("missing"),"missing capability");
        host.Install(Wire.Json(new PluginManifest("x",1,["x"],["y"])),()=>new Plugin(_=>{}));
        host.Install(Wire.Json(new PluginManifest("y",1,["y"],["x"])),()=>new Plugin(_=>{}));
        Check.Throws(()=>host.Enable("x"),"cycle");
        Check.Throws(()=>host.Install(Wire.Json(new PluginManifest("bad-api",2,[],[])),()=>new Plugin(_=>{})),"API");
    }
}
