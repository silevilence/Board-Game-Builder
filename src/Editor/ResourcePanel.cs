using Avalonia.Controls;
using System.Text.Json.Nodes;

namespace BoardGame.Editor;

public sealed class ResourcePanel : UserControl
{
    public ResourcePanel(EditorPluginScope scope)
    {
        var tree=new TreeView();Content=tree;
        var documents=scope.Service<DocumentWorkspace>("documents");
        void Refresh()
        {
            tree.Items.Clear();if(documents.Current is not {} doc) return;
            var files=new TreeViewItem {Header="模块文件",IsExpanded=true};
            foreach(var path in doc.Paths) files.Items.Add(new TreeViewItem {Header=path});tree.Items.Add(files);
            if(!doc.SupportsAuthorCapability(BoardGame.Authoring.GameAuthorFormats.BoardModel)) return;
            foreach(var group in new[]{"resources","entities","prototypes"})
            {
                var branch=new TreeViewItem {Header=group,IsExpanded=true};
                if(doc.Configuration[group] is JsonArray array) foreach(var item in array)
                {var id=item!["id"]!.GetValue<string>();branch.Items.Add(new TreeViewItem {Header=id,Tag=(group,id)});}
                if(doc.Configuration[group] is JsonObject map) foreach(var item in map) branch.Items.Add(new TreeViewItem {Header=item.Key+" → "+item.Value});
                tree.Items.Add(branch);
            }
        }
        tree.SelectionChanged+=(_,_)=> {if(tree.SelectedItem is TreeViewItem {Tag:ValueTuple<string,string> selected}) documents.Select(selected.Item1,selected.Item2);};
        scope.OnDocumentChanged(Refresh);Refresh();
    }
}
