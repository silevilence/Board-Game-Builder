using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BoardGame.Modules;
namespace BoardGame.Editor;

public sealed class AuthorPanel : UserControl
{
    private readonly PluginContext context;
    private readonly ComboBox collection=new() {ItemsSource=new[]{"entities","prototypes"},SelectedIndex=0,Width=120};
    private readonly ComboBox entity=new() {Width=160};
    private readonly TextBox id=new() {PlaceholderText="新标识",Width=130};
    private readonly TextBox type=new() {Text="properties",Width=130};
    private readonly TextBox yaml=new() {AcceptsReturn=true,TextWrapping=Avalonia.Media.TextWrapping.NoWrap};
    private readonly TextBlock message=new() {TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    public AuthorPanel(PluginContext context)
    {
        this.context=context;
        var buttons=new WrapPanel();
        foreach(var c in new Control[]{collection,entity,id,type}) buttons.Children.Add(c);
        Button("创建原型",()=>Authoring.CreatePrototype(Document,id.Text ?? "",entity.SelectedItem as string));
        Button("实例化原型",()=>Authoring.Instantiate(Document,entity.SelectedItem as string ?? "",id.Text ?? ""));
        Button("读取组件",()=> { var config=AuthorConfiguration.Expand(Document.Configuration); var item=Authoring.Find(config,collection.SelectedItem as string ?? "entities",entity.SelectedItem as string ?? ""); var component=item["components"]!.AsArray().First(c=>c!["type"]!.GetValue<string>()==type.Text); yaml.Text=AuthorConfiguration.ToYaml(component!["data"]!.AsObject()); });
        Button("应用组件覆盖",()=>Authoring.SetComponent(Document,collection.SelectedItem as string ?? "entities",entity.SelectedItem as string ?? "",type.Text ?? "",AuthorConfiguration.FromYaml(yaml.Text ?? "")));
        Button("模块 → YAML",()=>yaml.Text=AuthorConfiguration.ToYaml(Document.Configuration));
        Button("YAML → 模块",()=>Authoring.ApplyYaml(Document,yaml.Text ?? ""));
        Button("校验配置",()=> { _=Document.Snapshot(); });
        var panel=new DockPanel {Margin=new Thickness(10)}; DockPanel.SetDock(buttons,Dock.Top); DockPanel.SetDock(message,Dock.Bottom);
        panel.Children.Add(buttons); panel.Children.Add(message); panel.Children.Add(new ScrollViewer {Content=yaml}); Content=panel;
        collection.SelectionChanged+=(_,_)=>Refresh(); context.OnDocumentChanged(Refresh); Refresh();
        void Button(string label,Action action)
        {
            var button=new Button {Content=label,Margin=new Thickness(3)};
            button.Click+=(_,_)=> { try { action(); message.Text="已应用；实例只覆盖所填字段，原型的其他字段更新会传播。可撤销。"; } catch(Exception e) { message.Text=e.Message; } }; buttons.Children.Add(button);
        }
    }
    private ModuleDocument Document=>context.Document ?? throw new InvalidOperationException("请打开模块");
    private void Refresh()
    {
        var selected=entity.SelectedItem as string;
        try { entity.ItemsSource=(context.Document?.Configuration[collection.SelectedItem as string ?? "entities"] as System.Text.Json.Nodes.JsonArray ?? []).Select(e=>e!["id"]!.GetValue<string>()).ToArray(); entity.SelectedItem=selected; }
        catch(Exception e) { message.Text=e.Message; }
    }
}
