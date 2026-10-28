using BoardGame.View;
namespace BoardGame.Editor;

public sealed class BoardViewports
{
    public InteractiveTable Create(EditorPluginScope owner)
    {
        var table=new InteractiveTable();owner.Own(table);return table;
    }
}
