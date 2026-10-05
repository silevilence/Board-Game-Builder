using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using BoardGame.Modules;
namespace BoardGame.Editor;

// The form edits the same component data as YAML, through one undo transaction.
public sealed class MediaInspector : UserControl
{
    private readonly PluginContext context;
    private readonly ComboBox collection=new() {ItemsSource=new[]{"entities","prototypes"},SelectedIndex=0};
    private readonly ComboBox entities=new() {MinWidth=180};
    private readonly ComboBox types=new() {ItemsSource=Defaults.Keys.ToArray(),SelectedIndex=0};
    private readonly StackPanel fields=new() {Spacing=8};
    private readonly TextBlock status=new() {TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly TextBox newId=new() {Watermark="新实体标识"};
    private readonly List<Action<JsonObject>> readers=[];
    private JsonObject? displayed;
    public static readonly Dictionary<string,string> Defaults=new() {
        ["dice"]="{\"values\":[1,2,3,4,5,6]}", ["counter"]="{\"property\":\"value\",\"min\":0,\"max\":10}",
        ["position"]="{\"x\":0,\"y\":0}", ["owner"]="{\"seat\":\"p1\"}", ["visibility"]="{\"mode\":\"public\"}",
        ["container"]="{\"capacity\":12,\"ordered\":false,\"contents\":[]}",
        ["faces"]="{\"names\":[\"正面\",\"背面\"],\"resources\":[],\"up\":true,\"index\":0}", ["properties"]="{\"value\":0}"
    };
    public MediaInspector(PluginContext context)
    {
        this.context=context;
        var top=new WrapPanel();
        foreach(var control in new Control[]{collection,entities,types,newId}) { control.Margin=new Thickness(4); top.Children.Add(control); }
        Button("添加空实体",()=>Document.EditConfiguration(c=> { var id=newId.Text; if(string.IsNullOrWhiteSpace(id) || c["entities"]!.AsArray().Any(e=>e!["id"]!.GetValue<string>()==id)) throw new InvalidDataException("实体标识为空或重复"); c["entities"]!.AsArray().Add(new JsonObject {["id"]=id,["components"]=new JsonArray()}); }));
        Button("读取 / 添加组件",LoadFields);
        Button("应用字段",()=> { var data=new JsonObject(); foreach(var read in readers) read(data); Authoring.ApplyFields(Document,Collection,Identity,Component,displayed,data); _=Document.Snapshot(); LoadFields(); });
        Button("移除覆盖 / 组件",()=>Document.EditConfiguration(c=> { var array=Authoring.Find(c,Collection,Identity)["components"]!.AsArray(); var item=array.FirstOrDefault(c=>c!["type"]!.GetValue<string>()==Component); if(item!=null) array.Remove(item); }));
        var property=new TextBox {Watermark="新数值属性名"}; top.Children.Add(property);
        Button("添加数值字段",()=> { if(string.IsNullOrWhiteSpace(property.Text)) throw new InvalidDataException("属性名不能为空"); Field(property.Text,JsonValue.Create(0)); });
        var panel=new DockPanel {Margin=new Thickness(10)}; DockPanel.SetDock(top,Dock.Top); DockPanel.SetDock(status,Dock.Bottom);
        panel.Children.Add(top); panel.Children.Add(status); panel.Children.Add(new ScrollViewer {Content=fields}); Content=panel;
        collection.SelectionChanged+=(_,_)=>Refresh(); entities.SelectionChanged+=(_,_)=>TryLoad(); types.SelectionChanged+=(_,_)=>TryLoad();
        context.OnDocumentChanged(Refresh); Refresh();
        void Button(string label,Action action)
        {
            var button=new Button {Content=label,Margin=new Thickness(4)};
            button.Click+=(_,_)=> { try { action(); status.Text="已应用。错误配置可撤销；抽牌数量与展示时机由 Lua 决定。"; } catch(Exception e) { status.Text=e.Message; } }; top.Children.Add(button);
        }
    }
    private ModuleDocument Document=>context.Document ?? throw new InvalidOperationException("请打开模块");
    private string Collection=>collection.SelectedItem as string ?? "entities";
    private string Identity=>entities.SelectedItem as string ?? throw new InvalidOperationException("请选择实体或原型");
    private string Component=>types.SelectedItem as string ?? "position";
    private void Refresh()
    {
        var selected=entities.SelectedItem;
        entities.ItemsSource=(context.Document?.Configuration[Collection] as JsonArray ?? []).Select(e=>e!["id"]!.GetValue<string>()).ToArray(); entities.SelectedItem=selected;
    }
    private void TryLoad() { try { LoadFields(); } catch(Exception e) { status.Text=e.Message; } }
    private void LoadFields()
    {
        var config=AuthorConfiguration.Expand(Document.Configuration);
        var item=Authoring.Find(config,Collection,Identity)["components"]!.AsArray().FirstOrDefault(c=>c!["type"]!.GetValue<string>()==Component);
        var data=item?["data"]?.AsObject() ?? JsonNode.Parse(Defaults[Component])!.AsObject();
        displayed=item?["data"]?.DeepClone().AsObject();
        fields.Children.Clear(); readers.Clear(); foreach(var field in data) Field(field.Key,field.Value);
    }
    private void Field(string name,JsonNode? value)
    {
        fields.Children.Add(new TextBlock {Text=name});
        if(value is JsonArray array)
        {
            var text=new TextBox {Text=string.Join("\n",array.Select(v=>v!.ToString())),AcceptsReturn=true,MinHeight=60}; fields.Children.Add(text);
            var numeric=array.FirstOrDefault() is JsonValue n && n.TryGetValue<int>(out _);
            readers.Add(data=>data[name]=new JsonArray((text.Text ?? "").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(v=>numeric?(JsonNode?)JsonValue.Create(int.Parse(v,CultureInfo.InvariantCulture)):JsonValue.Create(v)).ToArray()));
        }
        else if(value is JsonValue boolean && boolean.TryGetValue<bool>(out var state))
        { var box=new CheckBox {IsChecked=state}; fields.Children.Add(box); readers.Add(data=>data[name]=box.IsChecked==true); }
        else
        {
            var text=new TextBox {Text=value?.ToString() ?? ""}; fields.Children.Add(text);
            var numeric=value is JsonValue scalar && scalar.GetValueKind()==System.Text.Json.JsonValueKind.Number;
            readers.Add(data=>data[name]=numeric?JsonNode.Parse(text.Text ?? "0"):JsonValue.Create(text.Text ?? ""));
        }
    }
}
