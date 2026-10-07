using Avalonia.Controls;
using BoardGame.Kernel;
namespace BoardGame.Editor;

public sealed class TtsImportPanel : UserControl
{
    public TtsImportPanel(EditorPluginScope scope)
    {
        const string key="TTS 导入";var documents=scope.Service<DocumentWorkspace>("documents");var commands=scope.Service<EditorCommands>("commands");
        var path=new TextBox {PlaceholderText="TTS JSON 存档路径"};var prefix=new TextBox {PlaceholderText="对象名称前缀"};
        var report=new TextBox {IsReadOnly=true,AcceptsReturn=true,MinHeight=350};ImportPreview? preview=null;var loading=false;
        void Refresh()
        {
            loading=true;
            try {if(documents.Current!=null) {var values=Wire.Read<string[]>(documents.Form(key,()=>Wire.Json(new[] {"","tts"})).Text);path.Text=values[0];prefix.Text=values[1];}}
            finally {loading=false;}
        }
        foreach(var field in new[] {path,prefix}) field.PropertyChanged+=(_,e)=> {if(!loading && e.Property==TextBox.TextProperty && documents.Current!=null) documents.EditForm(key,Wire.Json(new[] {path.Text ?? "",prefix.Text ?? ""}));};
        commands.Register(scope,new("tts.preview","预览 TTS 转换",()=>
        {
            preview=null;
            try {preview=TtsImport.Preview(documents.Current!,path.Text ?? "",prefix.Text ?? "");report.Text=string.Join("\n",preview.Report)+"\n\n新增文件：\n"+string.Join("\n",preview.Files.Keys);}
            catch(Exception error) {report.Text=error.Message;}
            return Task.CompletedTask;
        },()=>documents.Current!=null));
        commands.Register(scope,new("tts.apply","应用 TTS 转换",()=>
        {
            try {var selected=preview ?? throw new InvalidOperationException("请先预览");documents.ApplyForm(key,selected.Apply);preview=null;report.Text+="\n转换已应用，可一次撤销。";}
            catch(Exception error) {report.Text=error.Message;}
            return Task.CompletedTask;
        },()=>documents.Current!=null && preview!=null));
        var show=new Button {Content="生成转换预览"};var apply=new Button {Content="确认应用所列转换"};var discard=new Button {Content="放弃输入"};
        show.Click+=async (_,_)=>await commands.Execute("tts.preview");apply.Click+=async (_,_)=>await commands.Execute("tts.apply");discard.Click+=(_,_)=> {preview=null;documents.DiscardForm(key);};
        Content=new StackPanel {Spacing=12,Children={new TextBlock {Text="首版支持卡牌、牌堆、本地卡图与桌布及位置。来源 Lua 不执行；不支持项保留在原始输入并列于报告。"},path,prefix,new WrapPanel {Children={show,apply,discard}},report}};
        scope.OnDocumentChanged(Refresh);Refresh();
    }
}
