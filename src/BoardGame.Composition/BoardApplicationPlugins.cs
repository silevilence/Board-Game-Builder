using BoardGame.AppRuntime;
using BoardGame.Rendering;
using BoardGame.Resources;
using BoardGame.View;

namespace BoardGame.Runtime;

/// <summary>The ordinary installed board catalogue. Native builds select individual factories instead.</summary>
public static class BoardApplicationPlugins
{
    public static ApplicationCatalog Catalog()=>new([
        BoardStaticFactories.Definition(),BoardStaticFactories.Authority(),BoardStaticFactories.Rules(),BoardStaticFactories.Recording(),
        BoardStaticFactories.Session(),BoardStaticFactories.CpuSession(),BoardStaticFactories.EffectsSession(),BoardStaticFactories.CpuEffectsSession(),
        AssetStaticFactories.Resources(),AssetStaticFactories.Images(),AssetStaticFactories.Font(),AssetStaticFactories.TexturePacker(),
        BoardRenderPlugin.Renderer(),BoardRenderPlugin.VisualInput(),ViewportTargetPlugin.Create(),WasmStaticFactories.Create()]);
}
