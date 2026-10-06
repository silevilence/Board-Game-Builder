using Avalonia;
using Avalonia.Controls;
using BoardGame.Modules;
namespace BoardGame.Editor;

public sealed class AuthorPanel : UserControl
{
    private readonly EditorPluginScope context;
    private readonly DocumentWorkspace workspace;
    private readonly ComboBox collection=new() {ItemsSource=new[]{"entities","prototypes"},SelectedIndex=0,Width=120};
    private readonly ComboBox entity=new() {Width=160};
    private readonly TextBox id=new() {PlaceholderText="新标识",Width=130};
    private readonly TextBox type=new() {Text="properties",Width=130};
    private readonly TextBox yaml=new() {AcceptsReturn=true,TextWrapping=Avalonia.Media.TextWrapping.NoWrap};
    private readonly TextBlock message=new() {TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private bool loading;
    private string? formKey;
    public AuthorPanel(EditorPluginScope context)
    {
        this.context=context;workspace=context.Service<DocumentWorkspace>("documents");
        type.Text=workspace.ViewState.GetValueOrDefault("component.type","properties");
        yaml.PropertyChanged+=(_,e)=> {if(e.Property==TextBox.TextProperty && !loading && formKey!=null) workspace.EditForm(formKey,yaml.Text ?? "");};
        var buttons=new WrapPanel();foreach(var c in new Control[]{collection,entity,id,type}) buttons.Children.Add(c);
        Button("创建原型",()=>Authoring.CreatePrototype(Document,id.Text ?? "",entity.SelectedItem as string));
        Button("实例化原型",()=>Authoring.Instantiate(Document,entity.SelectedItem as string ?? "",id.Text ?? ""));
        Button("读取组件 / 放弃此草稿",()=> {if(formKey!=null) workspace.DiscardForm(formKey);LoadComponent();});
        Button("应用组件覆盖",()=>
        {
            var data=AuthorConfiguration.FromYaml(yaml.Text ?? "");
            workspace.ApplyForm(formKey ?? throw new InvalidOperationException("请选择组件"),doc=>Authoring.SetComponent(doc,Collection,Identity,type.Text ?? "",data));
        });
        Button("校验配置",()=> {_=Document.Snapshot();});
        var panel=new DockPanel {Margin=new Thickness(10)};DockPanel.SetDock(buttons,Dock.Top);DockPanel.SetDock(message,Dock.Bottom);
        panel.Children.Add(buttons);panel.Children.Add(message);panel.Children.Add(new ScrollViewer {Content=yaml});Content=panel;
        collection.SelectionChanged+=(_,_)=> {if(!loading) Refresh();};
        entity.SelectionChanged+=(_,_)=> {if(!loading && entity.SelectedItem is string selected) {workspace.Select(Collection,selected);LoadComponent();}};
        type.PropertyChanged+=(_,e)=> {if(e.Property==TextBox.TextProperty) {workspace.ViewState["component.type"]=type.Text ?? "";LoadComponent();}};
        void Selected()
        {
            if(workspace.Selection is not {} selected || loading) return;
            loading=true;collection.SelectedItem=selected.Collection;loading=false;Refresh();
        }
        workspace.SelectionChanged+=Selected;context.Own(new Cleanup(()=>workspace.SelectionChanged-=Selected));context.OnDocumentChanged(Refresh);Selected();Refresh();
        void Button(string label,Action action)
        {
            var button=new Button {Content=label,Margin=new Thickness(3)};
            button.Click+=(_,_)=> {try {action();message.Text="已应用；实例只覆盖所填字段，原型的其他字段更新会传播。可撤销。";} catch(Exception e) {message.Text=e.Message;}};buttons.Children.Add(button);
        }
    }
    private ModuleDocument Document=>context.Document ?? throw new InvalidOperationException("请打开模块");
    private string Collection=>collection.SelectedItem as string ?? "entities";
    private string Identity=>entity.SelectedItem as string ?? throw new InvalidOperationException("请选择实体或原型");
    private void Refresh()
    {
        if(loading) return;loading=true;
        try
        {
            var selected=workspace.Selection is {} selection && selection.Collection==Collection?selection.Id:entity.SelectedItem as string;
            entity.ItemsSource=(context.Document?.Configuration[Collection] as System.Text.Json.Nodes.JsonArray ?? []).Select(e=>e!["id"]!.GetValue<string>()).ToArray();entity.SelectedItem=selected;
        }
        catch(Exception e) {message.Text=e.Message;}
        finally {loading=false;}
        LoadComponent();
    }
    private void LoadComponent()
    {
        if(loading) return;loading=true;
        try
        {
            formKey=null;
            if(context.Document==null || entity.SelectedItem==null) {yaml.Text="";yaml.IsEnabled=false;return;}
            formKey=$"组件 YAML/{Collection}/{Identity}/{type.Text}";
            var draft=workspace.Form(formKey,()=>
            {
                var config=AuthorConfiguration.Expand(Document.Configuration);
                var component=Authoring.Find(config,Collection,Identity)["components"]!.AsArray().FirstOrDefault(c=>c!["type"]!.GetValue<string>()==type.Text);
                return AuthorConfiguration.ToYaml(component?["data"]?.AsObject() ?? new());
            });
            yaml.IsEnabled=true;if(yaml.Text!=draft.Text) yaml.Text=draft.Text;
        }
        catch(Exception e) {message.Text=e.Message;}
        finally {loading=false;}
    }
}
