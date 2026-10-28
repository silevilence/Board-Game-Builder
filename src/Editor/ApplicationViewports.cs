using System.Text.Json;
using BoardGame.Rendering;
using BoardGame.Runtime;

namespace BoardGame.Editor;

public sealed class EditorViewports
{
    private readonly GraphicsConfiguration graphics;
    public EditorViewports(GraphicsConfiguration graphics)=>this.graphics=graphics;
    public ApplicationViewport Create(RuntimeHost instance)=>new(graphics,instance);
    internal static EditorViewports WindowsDx()
    {
        var backend=WindowsDxBackend.Plugin();
        return new(new(new(1,"editor.graphics","1",[new(backend.Id,backend.Version,backend.Api,JsonSerializer.SerializeToElement(new{}))]),[backend]));
    }
}
