using BoardGame.Editor;
using BoardGame.Kernel;
namespace Engine.Tests;
public static class PluginTests
{
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
}
