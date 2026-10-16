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
        var properties = new TextBox { Name = "flowProperties", AcceptsReturn = true, Height = 280, FontFamily = FontFamily.Parse("Consolas"), TextWrapping = TextWrapping.NoWrap };
        ScrollViewer.SetVerticalScrollBarVisibility(properties, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(properties, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        var name = new TextBox { Name = "flowName", PlaceholderText = "显示名称（不改变稳定 ID）" }; var rename = new Button { Content = "应用名称" }; var discardName = new Button { Content = "放弃名称" };
        var naming = new StackPanel { Children = { name, new WrapPanel { Children = { rename, discardName } } } };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var apply = new Button { Content = "应用属性" }; var discard = new Button { Content = "放弃字段" };
        var path = new TextBox { Name = "flowScriptPath", PlaceholderText = "scripts/node.lua" };
        var role = new ComboBox { ItemsSource = new[] { "execute", "seat", "actions", "decide" }, SelectedIndex = 0 };
        var create = new Button { Content = "创建并关联模板" };
        var discardScript = new Button { Content = "放弃脚本创建" };
        var files = new ScriptFileEditor(scope, "flow.script");
        var body = new StackPanel { Spacing = 6, Children = { title, naming, new TextBlock { Text = "共享契约属性（JSON；无效输入会保留）" }, properties,
            new WrapPanel { Children = { apply, discard } }, help, new TextBlock { Text = "节点 Lua 脚本 · 已有文件可在属性中设置 path/entry" }, path,
            new WrapPanel { Children = { role, create, discardScript } }, status, files } };
        Content = new ScrollViewer { Content = body };
        bool loading = false; string? key = null; FlowGraphSnapshot? loaded = null; (string Collection, string Id)? selected = null;
        void Refresh()
        {
            if (loading) return; loading = true;
            try
            {
                var previous = selected; selected = documents.Selection; loaded = null; key = null; naming.IsVisible = false;
                if (selected is not { } selection || selection.Collection is not ("flow.node" or "flow.edge")) { properties.Text = ""; properties.IsEnabled = false; create.IsEnabled = false; return; }
                loaded = flow.Load();
                if (!(selection.Collection == "flow.node" ? loaded.Model.Definition.Nodes.Any(n => n.Id == selection.Id) : loaded.Model.Definition.Edges.Any(e => e.Id == selection.Id))) return;
                var draft = flow.Properties(loaded, selection.Collection, selection.Id);
                key = flow.FormKey(selection.Collection, selection.Id); title.Text = selection.Id;
                properties.IsEnabled = true; properties.Text = draft.Text;
                if (previous != selected) properties.CaretIndex = 0;
                create.IsEnabled = selection.Collection == "flow.node";
                status.Text = draft.Dirty ? "字段尚未应用；保存、试玩和导出前请应用或放弃。" : "";
                if (selection.Collection == "flow.node")
                {
                    naming.IsVisible = true; name.Text = flow.Name(loaded, selection.Id).Text;
                    var scriptFields = Wire.Read<string[]>(flow.ScriptDraft(loaded, selection.Id).Text);
                    path.Text = scriptFields[0]; role.SelectedItem = scriptFields[1];
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
        name.PropertyChanged += (_, e) =>
        {
            if (!loading && selected is { Collection: "flow.node" } selection && e.Property == TextBox.TextProperty)
            { loading = true; try { documents.EditForm(flow.NameKey(selection.Id), name.Text ?? ""); } finally { loading = false; } }
        };
        rename.Click += (_, _) =>
        {
            try { if (loaded != null && selected is { Collection: "flow.node" } selection) flow.ApplyName(loaded, selection.Id); }
            catch (Exception error) { status.Text = error.Message; }
        };
        discardName.Click += (_, _) => { if (selected is { Collection: "flow.node" } selection) documents.DiscardForm(flow.NameKey(selection.Id)); };
        apply.Click += (_, _) =>
        {
            try { if (loaded != null && selected is {} selection) flow.ApplyProperties(loaded, selection.Collection, selection.Id); }
            catch (Exception error) { status.Text = error.Message; }
        };
        discard.Click += (_, _) => { if (key != null) documents.DiscardForm(key); };
        void EditScript()
        {
            if (loading || selected is not { Collection: "flow.node" } selection) return;
            loading = true;
            try { documents.EditForm(flow.ScriptKey(selection.Id), Wire.Json(new[] { path.Text ?? "", role.SelectedItem as string ?? "execute" })); }
            finally { loading = false; }
        }
        path.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) EditScript(); };
        role.SelectionChanged += (_, _) => EditScript();
        discardScript.Click += (_, _) => { if (selected is { Collection: "flow.node" } selection) documents.DiscardForm(flow.ScriptKey(selection.Id)); };
        create.Click += (_, _) =>
        {
            try
            {
                if (loaded != null && selected is {} selection && selection.Collection == "flow.node")
                { var target = path.Text!; flow.ApplyScript(loaded, selection.Id, scope.Cancellation); files.Open(target); }
            }
            catch (Exception error) { if (!scope.Cancellation.IsCancellationRequested) status.Text = error.Message; }
        };
        documents.SelectionChanged += Refresh; scope.Own(new Cleanup(() => documents.SelectionChanged -= Refresh));
        scope.OnDocumentChanged(Refresh); Refresh();
    }
}
