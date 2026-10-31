using BoardGame.Kernel;
using BoardGame.Rendering;
namespace BoardGame.View;

// Ordinary WindowsDX compatibility constructor; shared behavior is backend-neutral.
public sealed class InteractiveTable : InteractiveBoard
{
    public InteractiveTable(RuntimePluginSelection? renderer=null):base(DefaultGraphics(),renderer) { }
    public InteractiveTable(GraphicsConfiguration graphics,RuntimePluginSelection? renderer=null):base(graphics,renderer) { }
    private static GraphicsConfiguration DefaultGraphics()
    {
        var backend=WindowsDxBackend.Plugin();
        return new(new(1,"desktop.graphics","1",[new(backend.Id,backend.Version,backend.Api,RuntimeConfiguration.Empty)]),[backend]);
    }
}
