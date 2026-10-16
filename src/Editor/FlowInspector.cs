using Avalonia.Controls;
using Avalonia.Media;
using BoardGame.Authoring;
using BoardGame.Kernel;

namespace BoardGame.Editor;

public sealed class FlowInspector : UserControl
{
    public FlowInspector(EditorPluginScope scope, FlowWorkspace flow)
    {
        var documents = scope.Service<DocumentWorkspace>("documents");
        var title = new TextBlock { Text = "选择节点或连线", TextWrapping = TextWrapping.Wrap };
        var help = new TextBlock { Text = FlowScriptTemplates.Help, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var properties = new TextBox { AcceptsReturn = true, MinHeight = 250, FontFamily = FontFamily.Parse("Consolas"), TextWrapping = TextWrapping.NoWrap };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var apply = new Button { Content = "应用属性" }; var discard = new Button { Content = "放弃字段" };
        var path = new TextBox { PlaceholderText = "scripts/node.lua" };
        var role = new ComboBox { ItemsSource = new[] { "execute", "seat", "actions", "decide" }, SelectedIndex = 0 };
        var create = new Button { Content = "创建并关联模板" };
        var files = new ScriptFileEditor(scope, "flow.script");
        var body = new StackPanel { Spacing = 6, Children = { title, new TextBlock { Text = "共享契约属性（JSON；无效输入会保留）" }, properties,
            new WrapPanel { Children = { apply, discard } }, help, new TextBlock { Text = "节点 Lua 脚本 · 已有文件可在属性中设置 path/entry" }, path,
            new WrapPanel { Children = { role, create } }, status, files } };
        Content = new ScrollViewer { Content = body };
        bool loading = false; string? key = null; FlowGraphSnapshot? loaded = null; (string Collection, string Id)? selected = null;
        void Refresh()
        {
            if (loading) return; loading = true;
            try
            {
                selected = documents.Selection; loaded = null; key = null;
                if (selected is not { } selection || selection.Collection is not ("flow.node" or "flow.edge")) { properties.Text = ""; properties.IsEnabled = false; create.IsEnabled = false; return; }
                loaded = flow.Load();
                if (!(selection.Collection == "flow.node" ? loaded.Model.Definition.Nodes.Any(n => n.Id == selection.Id) : loaded.Model.Definition.Edges.Any(e => e.Id == selection.Id))) return;
                var draft = flow.Properties(loaded, selection.Collection, selection.Id);
                key = flow.FormKey(selection.Collection, selection.Id); title.Text = selection.Id;
                properties.IsEnabled = true; properties.Text = draft.Text;
                create.IsEnabled = selection.Collection == "flow.node" && !documents.HasPendingForms;
                status.Text = draft.Dirty ? "字段尚未应用；保存、试玩和导出前请应用或放弃。" : "";
                if (selection.Collection == "flow.node")
                {
                    var node = loaded.Model.Definition.Nodes.First(n => n.Id == selection.Id);
                    var script = node.Script ?? node.ActionsScript ?? node.Seat?.Script ?? node.DecideScript;
                    if (script != null) files.Open(script.Path);
                }
            }
            catch (Exception error) { status.Text = error.Message; }
            finally { loading = false; }
        }
        properties.PropertyChanged += (_, e) =>
        {
            if (!loading && key != null && e.Property == TextBox.TextProperty)
            {
                loading = true;
                try { documents.EditForm(key, properties.Text ?? ""); status.Text = "字段尚未应用；保存、试玩和导出前请应用或放弃。"; create.IsEnabled = false; }
                finally { loading = false; }
            }
        };
        apply.Click += (_, _) =>
        {
            try { if (loaded != null && selected is {} selection) flow.ApplyProperties(loaded, selection.Collection, selection.Id); }
            catch (Exception error) { status.Text = error.Message; }
        };
        discard.Click += (_, _) => { if (key != null) documents.DiscardForm(key); };
        create.Click += async (_, _) =>
        {
            try
            {
                if (loaded != null && selected is {} selection && selection.Collection == "flow.node")
                { await flow.CreateScript(loaded, selection.Id, (string)role.SelectedItem!, path.Text ?? "", scope.Cancellation); files.Open(path.Text!); }
            }
            catch (Exception error) { if (!scope.Cancellation.IsCancellationRequested) status.Text = error.Message; }
        };
        documents.SelectionChanged += Refresh; scope.Own(new Cleanup(() => documents.SelectionChanged -= Refresh));
        scope.OnDocumentChanged(Refresh); Refresh();
    }
}
