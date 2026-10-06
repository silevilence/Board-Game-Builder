using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using BoardGame.Modules;
namespace BoardGame.Editor;

// Unparsed field text and its baseline are document-owned, including invalid intermediate input.
public sealed class MediaInspector : UserControl
{
    private readonly EditorPluginScope context;
    private readonly DocumentWorkspace workspace;
    private readonly ComboBox collection=new() {ItemsSource=new[]{"entities","prototypes"},SelectedIndex=0};
    private readonly ComboBox entities=new() {MinWidth=180};
    private readonly ComboBox types=new() {ItemsSource=Defaults.Keys.ToArray(),SelectedIndex=0};
    private readonly StackPanel fields=new() {Spacing=8};
    private readonly TextBlock status=new() {TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly TextBox newId=new() {PlaceholderText="新实体标识"};
    private readonly List<Action<JsonObject>> readers=[];
    private JsonObject? form;
    private string? formKey,loadedText;
    private bool loading;
    public static readonly Dictionary<string,string> Defaults=new() {
        ["dice"]="{\"values\":[1,2,3,4,5,6]}", ["counter"]="{\"property\":\"value\",\"min\":0,\"max\":10}",
        ["position"]="{\"x\":0,\"y\":0}", ["owner"]="{\"seat\":\"p1\"}", ["visibility"]="{\"mode\":\"public\"}",
        ["container"]="{\"capacity\":12,\"ordered\":false,\"contents\":[]}",
        ["faces"]="{\"names\":[\"正面\",\"背面\"],\"resources\":[],\"up\":true,\"index\":0}", ["properties"]="{\"value\":0}"
    };
    public MediaInspector(EditorPluginScope context)
    {
        this.context=context;workspace=context.Service<DocumentWorkspace>("documents");
        types.SelectedItem=workspace.ViewState.GetValueOrDefault("media.type","dice");
        var top=new WrapPanel();foreach(var control in new Control[]{collection,entities,types,newId}) {control.Margin=new Thickness(4);top.Children.Add(control);}
        Button("添加空实体",()=>Document.EditConfiguration(c=> {var id=newId.Text;if(string.IsNullOrWhiteSpace(id) || c["entities"]!.AsArray().Any(e=>e!["id"]!.GetValue<string>()==id)) throw new InvalidDataException("实体标识为空或重复");c["entities"]!.AsArray().Add(new JsonObject {["id"]=id,["components"]=new JsonArray()});}));
        Button("读取 / 放弃字段草稿",()=> {if(formKey!=null) workspace.DiscardForm(formKey);LoadFields();});
        Button("应用字段",()=>
        {
            var data=new JsonObject();foreach(var read in readers) read(data);
            workspace.ApplyForm(formKey ?? throw new InvalidOperationException("请选择组件"),doc=>Authoring.ApplyFields(doc,Collection,Identity,Component,form?["displayed"]?.AsObject(),data));
            _=Document.Snapshot();LoadFields();
        });
        Button("移除覆盖 / 组件",()=>Document.EditConfiguration(c=> {var array=Authoring.Find(c,Collection,Identity)["components"]!.AsArray();var item=array.FirstOrDefault(c=>c!["type"]!.GetValue<string>()==Component);if(item!=null) array.Remove(item);}));
        var property=new TextBox {PlaceholderText="新数值属性名"};top.Children.Add(property);
        Button("添加数值字段",()=>
        {
            if(string.IsNullOrWhiteSpace(property.Text) || form==null) throw new InvalidDataException("请选择组件并填写属性名");
            var items=form["fields"]!.AsArray();if(items.Any(f=>f!["name"]!.GetValue<string>()==property.Text)) throw new InvalidDataException("字段已存在");
            var field=Describe(property.Text,JsonValue.Create(0));items.Add(field);Field(field);Store();
        });
        var panel=new DockPanel {Margin=new Thickness(10)};DockPanel.SetDock(top,Dock.Top);DockPanel.SetDock(status,Dock.Bottom);
        panel.Children.Add(top);panel.Children.Add(status);panel.Children.Add(new ScrollViewer {Content=fields});Content=panel;
        collection.SelectionChanged+=(_,_)=> {if(!loading) Refresh();};
        entities.SelectionChanged+=(_,_)=> {if(!loading && entities.SelectedItem is string selected) {workspace.Select(Collection,selected);TryLoad();}};
        types.SelectionChanged+=(_,_)=> {workspace.ViewState["media.type"]=Component;TryLoad();};
        void Selected() {if(workspace.Selection is not {} selected || loading) return;loading=true;collection.SelectedItem=selected.Collection;loading=false;Refresh();}
        workspace.SelectionChanged+=Selected;context.Own(new Cleanup(()=>workspace.SelectionChanged-=Selected));context.OnDocumentChanged(Refresh);Selected();Refresh();
        void Button(string label,Action action)
        {
            var button=new Button {Content=label,Margin=new Thickness(4)};
            button.Click+=(_,_)=> {try {action();status.Text="已应用。错误配置可撤销；抽牌数量与展示时机由 Lua 决定。";} catch(Exception e) {status.Text=e.Message;}};top.Children.Add(button);
        }
    }
    private ModuleDocument Document=>context.Document ?? throw new InvalidOperationException("请打开模块");
    private string Collection=>collection.SelectedItem as string ?? "entities";
    private string Identity=>entities.SelectedItem as string ?? throw new InvalidOperationException("请选择实体或原型");
    private string Component=>types.SelectedItem as string ?? "position";
    private void Refresh()
    {
        if(loading) return;loading=true;
        try
        {
            var selected=workspace.Selection is {} selection && selection.Collection==Collection?selection.Id:entities.SelectedItem as string;
            entities.ItemsSource=(context.Document?.Configuration[Collection] as JsonArray ?? []).Select(e=>e!["id"]!.GetValue<string>()).ToArray();entities.SelectedItem=selected;
        }
        finally {loading=false;}
        TryLoad();
    }
    private void TryLoad() {if(loading) return;try {LoadFields();} catch(Exception e) {status.Text=e.Message;}}
    private void LoadFields()
    {
        if(context.Document==null || entities.SelectedItem==null) {formKey=null;form=null;fields.Children.Clear();readers.Clear();return;}
        var key=$"媒介字段/{Collection}/{Identity}/{Component}";
        var draft=workspace.Form(key,()=>
        {
            var item=Authoring.Find(AuthorConfiguration.Expand(Document.Configuration),Collection,Identity)["components"]!.AsArray().FirstOrDefault(c=>c!["type"]!.GetValue<string>()==Component);
            var data=item?["data"]?.AsObject() ?? JsonNode.Parse(Defaults[Component])!.AsObject();
            return new JsonObject {["displayed"]=item?["data"]?.DeepClone(),["fields"]=new JsonArray(data.Select(f=>(JsonNode)Describe(f.Key,f.Value)).ToArray())}.ToJsonString();
        });
        if(formKey==key && loadedText==draft.Text) return;
        formKey=key;loadedText=draft.Text;form=JsonNode.Parse(draft.Text)!.AsObject();
        fields.Children.Clear();readers.Clear();foreach(var field in form["fields"]!.AsArray()) Field(field!.AsObject());
    }
    private static JsonObject Describe(string name,JsonNode? value)
    {
        var kind=value is JsonArray array ? (array.FirstOrDefault() is JsonValue n && n.TryGetValue<int>(out _)?"integers":"strings")
            : value is JsonValue scalar && scalar.GetValueKind()==System.Text.Json.JsonValueKind.Number?"number"
            : value is JsonValue boolean && boolean.TryGetValue<bool>(out _)?"boolean":"text";
        return new() {["name"]=name,["kind"]=kind,["text"]=value is JsonArray values?string.Join("\n",values.Select(v=>v!.ToString())):value?.ToString() ?? ""};
    }
    private void Store() {if(form==null || formKey==null) return;loadedText=form.ToJsonString();workspace.EditForm(formKey,loadedText);}
    private void Field(JsonObject field)
    {
        var name=field["name"]!.GetValue<string>();var kind=field["kind"]!.GetValue<string>();var value=field["text"]!.GetValue<string>();
        fields.Children.Add(new TextBlock {Text=name});
        if(kind=="boolean")
        {
            var box=new CheckBox {IsChecked=bool.Parse(value)};fields.Children.Add(box);
            box.IsCheckedChanged+=(_,_)=> {field["text"]=(box.IsChecked==true).ToString();Store();};
            readers.Add(data=>data[name]=bool.Parse(field["text"]!.GetValue<string>()));
        }
        else
        {
            var array=kind is "strings" or "integers";
            var text=new TextBox {Text=value,AcceptsReturn=array,MinHeight=array?60:0};fields.Children.Add(text);
            text.PropertyChanged+=(_,e)=> {if(e.Property==TextBox.TextProperty) {field["text"]=text.Text ?? "";Store();}};
            readers.Add(data=>
            {
                var raw=field["text"]!.GetValue<string>();
                data[name]=array?new JsonArray(raw.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(v=>kind=="integers"?(JsonNode?)JsonValue.Create(int.Parse(v,CultureInfo.InvariantCulture)):JsonValue.Create(v)).ToArray()):kind=="number"?JsonNode.Parse(raw):JsonValue.Create(raw);
            });
        }
    }
}
