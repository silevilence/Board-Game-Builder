using BoardGame.Kernel;
using CordiSharp.Loading;
using Avalonia.Threading;
using CordiSharp.Registry;

namespace BoardGame.Editor;

public sealed record EditorAssemblyManifest(string Id,int Api,string Entry,string[] Provides,string[] Requires);

public sealed class EditorAssemblies(EditorApplication app,AssemblyLoaderService loader)
{
    private readonly Dictionary<string,AssemblyPluginSet> loaded=[];
    public string Status { get; private set; }="尚未加载扩展程序集";
    public async Task Load(string assemblyPath)
    {
        var manifest=Wire.Read<EditorAssemblyManifest>(File.ReadAllText(Path.ChangeExtension(assemblyPath,".plugin.json")));
        if(loaded.ContainsKey(manifest.Id)) throw new InvalidOperationException("Assembly already loaded: "+manifest.Id);
        var set=loader.LoadAssembly(assemblyPath);
        if(set.IsUnloaded) throw new InvalidDataException("程序集加载失败："+assemblyPath);
        try
        {
            RejectAsyncEntries(set);
            _=set.GetPlugin(manifest.Entry);
            app.Install(new(manifest.Id,manifest.Api,manifest.Provides,manifest.Requires,_=>{})
            {
                InitializeAsync=async owner=>
                {
                    var handle=set.LoadPlugin(manifest.Entry,owner);
                    owner.Context.Effect(()=> (Func<ValueTask>)(()=>handle.DisposeAsync()));
                    await handle;
                }
            });
            loaded.Add(manifest.Id,set);
            await app.Enable(manifest.Id);Status="已加载："+manifest.Id;
        }
        catch
        {
            if(loaded.Remove(manifest.Id)) await app.Uninstall(manifest.Id);
            await set.UnloadAsync(false);throw;
        }
    }
    public async Task<WeakReference> Unload(string id)
    {
        if(!loaded.TryGetValue(id,out var set)) throw new InvalidDataException("Unknown assembly: "+id);
        // Policy checks precede any mutation; active document/session/export leases veto unload.
        await app.Uninstall(id);loaded.Remove(id);
        var weak=Track(set);
        await set.UnloadAsync(false);
        // Let Avalonia finish queued detach/layout/render work before checking real collection.
        // The upstream synchronous verification loop would block that UI-thread cleanup.
        for(var attempt=0;attempt<40;attempt++)
        {
            await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
            await Task.Delay(50);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            if(!weak.IsAlive) {Status="程序集已回收："+id;return weak;}
        }
        Status="功能已注销，但程序集回收未通过：strong references still held: "+id;
        throw new InvalidOperationException(Status);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Track(AssemblyPluginSet set)=>new(set.AssemblyLoadContext!);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RejectAsyncEntries(AssemblyPluginSet set)
    {
        if(set.AssemblyLoadContext!.Assemblies.SelectMany(a=>a.GetTypes()).Any(t=>t.GetInterfaces().Any(i=>i.IsGenericType && i.GetGenericTypeDefinition()==typeof(IAsyncPlugin<>))))
            throw new NotSupportedException("当前程序集扩展仅支持同步 IPlugin<EditorPluginScope> 入口；后台任务应由 Effect 管理取消与结束，不支持 IAsyncPlugin 初始化。");
    }
}
