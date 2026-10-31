using BoardGame.AppRuntime;
using BoardGame.Runtime;

namespace BoardGame.Rendering;

/// <summary>Fixed backend identities can be checked without loading a device implementation.</summary>
public static class GraphicsBackendContract
{
    public static IReadOnlyList<RuntimePluginDefinition> Descriptions=>[
        Describe(new("render.windowsdx","1","windows-x64",false)),
        Describe(new("render.desktopgl","1","windows-x64",true))];
    private static RuntimePluginDefinition Describe(RenderBackendDescriptor descriptor)=>RenderingPlugin.Backend(descriptor,
        ()=>throw new InvalidOperationException(descriptor.Id+": graphics implementation must be supplied by the host"));
    public static ApplicationCatalog Extend(ApplicationCatalog catalog)=>catalog.Extend(Descriptions
        .Where(p=>catalog.Content.Plugins.All(c=>c.Id!=p.Id)).Select(p=>new ApprovedApplicationPlugin(_=>p)));
    public static string[] DistributionFiles(BoardGame.Kernel.RuntimeStartup startup)=>startup.Plugins.Any(p=>p.Id=="render.desktopgl")?
        ["Rendering.Hosting.dll","Runtime.Plugins.dll","backends/desktopgl/runtime.plugin.json","backends/desktopgl/Rendering.DesktopGL.dll",
            "backends/desktopgl/MonoGame.Framework.dll","backends/desktopgl/SDL2.dll","backends/desktopgl/SkiaSharp.dll","backends/desktopgl/libSkiaSharp.dll","backends/desktopgl/NVorbis.dll"]:[];
    public static string PlatformRequirements(BoardGame.Kernel.RuntimeStartup startup)=>startup.Plugins.Any(p=>p.Id=="render.desktopgl")?
        "需要支持 OpenGL 2.1 的 Windows x64 图形设备。":"需要 Direct3D 11 图形设备。";
}
