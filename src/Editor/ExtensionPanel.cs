using Avalonia.Controls;
using BoardGame.Kernel;
using System.Text.Json.Nodes;
namespace BoardGame.Editor;

// Optional author UI; module capabilities belong to the shared runtime.
public sealed class ExtensionPanel : UserControl
{
    public ExtensionPanel(EditorPluginScope scope)
    {
        const string key="WASM 媒介";
        var documents=scope.Service<DocumentWorkspace>("documents");
        var text=new TextBox {AcceptsReturn=true,MinHeight=220};var report=new TextBlock {TextWrapping=Avalonia.Media.TextWrapping.Wrap};
        var apply=new Button {Content="应用 WASM 组件配置"};var discard=new Button {Content="放弃草稿"};
        Content=new StackPanel {Spacing=10,Children={new TextBlock {Text="修改已声明的 WASM 组件；插件启停不改变运行时模块。"},text,apply,discard,report}};
        var loading=false;
        void Refresh()
        {
            loading=true;
            try
            {
                if(documents.Current is not {} doc) {text.Text="";return;}
                text.Text=documents.Form(key,()=>
                {
                    var values=new JsonArray();
                    foreach(var entity in doc.Configuration["entities"]!.AsArray())
                    foreach(var component in entity!["components"]?.AsArray() ?? [])
                        if(component!["type"]!.GetValue<string>().StartsWith("wasm.",StringComparison.Ordinal))
                            values.Add(new JsonObject {["entity"]=entity["id"]!.DeepClone(),["type"]=component["type"]!.DeepClone(),["data"]=component["data"]!.DeepClone()});
                    return values.ToJsonString(new() {WriteIndented=true});
                }).Text;
            }
            finally {loading=false;}
        }
        text.PropertyChanged+=(_,e)=> {if(!loading && e.Property==TextBox.TextProperty && documents.Current!=null) documents.EditForm(key,text.Text ?? "");};
        apply.Click+=(_,_)=>
        {
            try
            {
                var values=JsonNode.Parse(text.Text ?? "")!.AsArray();
                documents.ApplyForm(key,doc=>doc.EditConfiguration(root=>
                {
                    foreach(var value in values)
                    {
                        var entity=Authoring.Find(root,"entities",value!["entity"]!.GetValue<string>());
                        var type=value["type"]!.GetValue<string>();
                        if(!type.StartsWith("wasm.",StringComparison.Ordinal)) throw new InvalidDataException("只能编辑 WASM 组件");
                        var component=entity["components"]!.AsArray().Single(c=>c!["type"]!.GetValue<string>()==type)!;
                        component["data"]=value["data"]!.DeepClone();
                    }
                }));
                report.Text="已应用，可撤销；试玩时校验声明与范围。";
            }
            catch(Exception error) {report.Text=error.Message;}
        };
        discard.Click+=(_,_)=>documents.DiscardForm(key);
        scope.OnDocumentChanged(Refresh);Refresh();
    }
}
