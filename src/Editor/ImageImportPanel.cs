using Avalonia.Controls;
using Avalonia.Media.Imaging;
using BoardGame.Kernel;

namespace BoardGame.Editor;

public sealed class ImageImportPanel : UserControl,IDisposable
{
    private readonly List<Bitmap> bitmaps=[];
    public ImageImportPanel(EditorPluginScope scope)
    {
        const string key="图片导入";
        var documents=scope.Service<DocumentWorkspace>("documents");var commands=scope.Service<EditorCommands>("commands");
        var path=new TextBox {PlaceholderText="图片文件路径"};var prefix=new TextBox {PlaceholderText="资源名称前缀"};
        var rows=new TextBox {Width=90};var columns=new TextBox {Width=90};var cloth=new CheckBox {Content="桌布（不切分）"};
        var report=new TextBlock {TextWrapping=Avalonia.Media.TextWrapping.Wrap};var images=new WrapPanel();
        ImportPreview? preview=null;ModuleDocument? previewDocument=null;string? previewFields=null;var loading=false;
        string Fields()=>Wire.Json(new[] {path.Text ?? "",prefix.Text ?? "",rows.Text ?? "",columns.Text ?? "",cloth.IsChecked==true?"true":"false"});
        void Refresh()
        {
            loading=true;
            try
            {
                if(documents.Current==null) return;
                var fields=Wire.Read<string[]>(documents.Form(key,()=>Wire.Json(new[] {"","cards","1","1","false"})).Text);
                path.Text=fields[0];prefix.Text=fields[1];rows.Text=fields[2];columns.Text=fields[3];cloth.IsChecked=fields[4]=="true";
            }
            finally {loading=false;}
        }
        void Edited() {if(!loading && documents.Current!=null) documents.EditForm(key,Wire.Json(new[] {path.Text ?? "",prefix.Text ?? "",rows.Text ?? "",columns.Text ?? "",cloth.IsChecked==true?"true":"false"}));}
        foreach(var field in new[] {path,prefix,rows,columns}) field.PropertyChanged+=(_,e)=> {if(e.Property==TextBox.TextProperty) Edited();};
        cloth.IsCheckedChanged+=(_,_)=>Edited();
        void Register(string id,string title,Action action,Func<bool> enabled)
        {
            commands.Register(scope,new(id,title,()=> {try {action();} catch(Exception error) {report.Text=error.Message;}return Task.CompletedTask;},enabled));
        }
        Register("images.preview","预览图片／卡图导入",()=>
        {
            preview=null;Dispose();images.Children.Clear();
            preview=ImageImport.Preview(documents.Current!,path.Text ?? "",prefix.Text ?? "",int.Parse(rows.Text ?? ""),int.Parse(columns.Text ?? ""),cloth.IsChecked==true);
            previewDocument=documents.Current;previewFields=Fields();
            report.Text=string.Join("\n",preview.Report)+"\n确认后点击应用；命名冲突不会覆盖现有内容。";
            foreach(var file in preview.Files.Take(200))
            {
                var bitmap=new Bitmap(new MemoryStream(file.Value));bitmaps.Add(bitmap);
                images.Children.Add(new StackPanel {Children={new Image {Source=bitmap,Width=90,Height=120},new TextBlock {Text=Path.GetFileName(file.Key)}}});
            }
        },()=>documents.Current!=null);
        Register("images.apply","应用预览中的图片导入",()=>
        {
            var selected=preview ?? throw new InvalidOperationException("请先预览");
            if(!ReferenceEquals(previewDocument,documents.Current) || previewFields!=Fields()) throw new InvalidOperationException("输入或文档已改变，请重新预览；草稿已保留。");
            documents.ApplyForm(key,selected.Apply);preview=null;report.Text="已导入；一次撤销可移除所有新增资源与卡牌。";
        },()=>preview!=null && documents.Current!=null);
        var show=new Button {Content="预览切分"};var apply=new Button {Content="应用导入"};var discard=new Button {Content="放弃输入"};
        show.Click+=async (_,_)=>await commands.Execute("images.preview");apply.Click+=async (_,_)=>await commands.Execute("images.apply");
        discard.Click+=(_,_)=> {preview=null;documents.DiscardForm(key);};
        Content=new ScrollViewer {Content=new StackPanel {Spacing=10,Children={path,prefix,new WrapPanel {Children={new TextBlock {Text="行"},rows,new TextBlock {Text="列"},columns,cloth}},new WrapPanel {Children={show,apply,discard}},report,images}}};
        scope.OnDocumentChanged(Refresh);Refresh();
    }
    public void Dispose() {foreach(var bitmap in bitmaps) bitmap.Dispose();bitmaps.Clear();}
}
