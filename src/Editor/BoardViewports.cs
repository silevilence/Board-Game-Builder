using BoardGame.View;
namespace BoardGame.Editor;

public sealed class BoardViewports(EditorViewports views)
{
    public InteractiveTable Create(EditorPluginScope owner)
    {
        var table=views.CreateBoard();owner.Own(table);return table;
    }
}
