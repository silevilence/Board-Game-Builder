using System.Runtime.CompilerServices;
using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.View;
using CordiSharp.Events;

namespace BoardGame.Editor;

internal static class AssemblyVerification
{
    public static Window Create(string path,string output)
    {
        var host=new EditorHost(baseOnly:true);
        host.Window.Opened+=async (_,_)=>
        {
            var errors=new List<string>();
            try
            {
                await host.Ready;await host.Application.Enable("author.runtime.board");await host.Application.Enable("author.module");var app=host.Application;var assemblies=app.Service<EditorAssemblies>("assemblies");
                try
                {
                    await assemblies.Load(EditorPaths.InWorkspace("tests/Editor.AsyncProbe/bin/Release/net10.0-windows/Editor.AsyncProbe.dll"));
                    throw new Exception("Unsafe async assembly entry was accepted");
                }
                catch(NotSupportedException e) when(e.Message.Contains("IAsyncPlugin")) { }
                if(app.Catalog.Any(p=>p.Id=="probe.async")) throw new Exception("Rejected assembly left registrations");
                for(var cycle=0;cycle<3;cycle++)
                {
                    await assemblies.Load(path);SessionVerification.Select(app,"sample.panel");await Task.Delay(200);
                    app.Root.Emit(EventKey.Create<int>("example.tick"),1);
                    app.Service<DocumentWorkspace>("documents").Current!.EditText("rules.lua","-- changed\n");
                    using(var held=app.Retain("sample.panel","活动文档或 Undo 引用扩展"))
                    {
                        try {await assemblies.Unload("sample.panel");throw new Exception("Retained assembly unloaded");}
                        catch(InvalidOperationException e) when(e.Message.Contains("Undo")) { }
                    }
                    SessionVerification.Select(app,"base.settings");
                    var weak=await assemblies.Unload("sample.panel");
                    if(weak.IsAlive || DesktopViewport.LiveDevices!=0 || DesktopViewport.LiveNativeWindows!=0) throw new Exception("Assembly or graphics resources survived");
                }
                await RetainedReference(app,assemblies,path);
                await app.DisposeAsync();
            }
            catch(Exception error) {errors.Add(error.ToString());}
            finally
            {
                File.WriteAllText(Path.GetFullPath(output),Wire.Json(new {success=errors.Count==0,errors,devices=DesktopViewport.LiveDevices}));
                await host.Application.DisposeAsync();host.Window.Close();
            }
        };
        return host.Window;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task RetainedReference(EditorApplication app,EditorAssemblies assemblies,string path)
    {
        await assemblies.Load(path);
        var held=app.Root.Get("sample.data") ?? throw new Exception("Missing plugin data");
        var diagnosed=false;
        try {await assemblies.Unload("sample.panel");}
        catch(Exception e) when(e.Message.Contains("strong references")) {diagnosed=true;}
        GC.KeepAlive(held);
        if(!diagnosed) throw new Exception("Retained plugin object was falsely reported collected");
    }
}
