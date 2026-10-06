using BoardGame.View;
namespace BoardGame.Editor;

public sealed class EditorViewports
{
    public InteractiveTable Create(EditorPluginScope owner)
    {
        var table=new InteractiveTable();owner.Own(table);return table;
    }
}
