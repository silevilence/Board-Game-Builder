using BoardGame.Authoring;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BoardGame.Kernel;
using System.Globalization;
namespace BoardGame.Editor;

public sealed class PrintPanel : UserControl,IDisposable
{
    private readonly List<Bitmap> images=[];
    public PrintPanel(EditorPluginScope scope)
    {
        const string key="打印拼版";var documents=scope.Service<DocumentWorkspace>("documents");var author=new AuthorImportWorkspace(documents);var commands=scope.Service<EditorCommands>("commands");
        var selected=new TextBox {AcceptsReturn=true,Height=90,PlaceholderText="所选卡牌 ID，每行一个"};
        var width=new TextBox {Width=75};var height=new TextBox {Width=75};var margin=new TextBox {Width=75};var gap=new TextBox {Width=75};
        var duplex=new ComboBox {ItemsSource=new[] {"none","long-edge","short-edge"},Width=140};var destination=new TextBox {PlaceholderText="新 PDF 文件路径"};
        var pages=new StackPanel {Spacing=20};var status=new TextBlock {TextWrapping=TextWrapping.Wrap};
        PrintPlan? plan=null;string? revision=null;string? previewFields=null;var loading=false;
        string Fields()=>Wire.Json(new[] {selected.Text ?? "",width.Text ?? "",height.Text ?? "",margin.Text ?? "",gap.Text ?? "",duplex.SelectedItem as string ?? "long-edge",destination.Text ?? ""});
        void Refresh()
        {
            loading=true;
            try
            {
                if(documents.Current is not {} doc) return;
                var fields=Wire.Read<string[]>(documents.Form(key,()=>Wire.Json(new[] {string.Join("\n",BoardPrintSource.Load(author.View()).Initial.Entities.Values.Where(e=>!e.IsContainer && e.Faces.Count>0).Select(e=>e.Id)),"63","88","10","3","long-edge",""})).Text);
                selected.Text=fields[0];width.Text=fields[1];height.Text=fields[2];margin.Text=fields[3];gap.Text=fields[4];duplex.SelectedItem=fields[5];destination.Text=fields[6];
            }
            finally {loading=false;}
        }
        void Edited() {if(!loading && documents.Current!=null) documents.EditForm(key,Fields());}
        foreach(var input in new[] {selected,width,height,margin,gap,destination}) input.PropertyChanged+=(_,e)=> {if(e.Property==TextBox.TextProperty) Edited();};
        duplex.SelectionChanged+=(_,_)=>Edited();
        commands.Register(scope,new("print.preview","预览打印拼版",()=>
        {
            plan=null;pages.Children.Clear();Dispose();
            try
            {
                double Parse(TextBox field)=>double.Parse(field.Text ?? "",CultureInfo.InvariantCulture);
                plan=PrintLayout.Create(BoardPrintSource.Load(author.View()),(selected.Text ?? "").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries),new(Parse(width),Parse(height),Parse(margin),Parse(gap),duplex.SelectedItem as string ?? "long-edge"));
                revision=author.View().Source.Version;previewFields=Fields();var number=0;
                foreach(var page in plan.Pages)
                {
                    pages.Children.Add(new TextBlock {Text=$"第 {++number} 页 · {(page[0].Back?"背面":"正面")}"});
                    var canvas=new Canvas {Width=plan.Options.PageWidth*2,Height=plan.Options.PageHeight*2,Background=Brushes.White};pages.Children.Add(canvas);
                    foreach(var placement in page)
                    {
                        var bitmap=new Bitmap(new MemoryStream(placement.Image));images.Add(bitmap);
                        var card=new Border {Width=placement.Width*2,Height=placement.Height*2,BorderThickness=new Thickness(0.5),BorderBrush=Brushes.Black,Child=new Image {Source=bitmap,Stretch=Stretch.Fill}};
                        ToolTip.SetTip(card,placement.Entity+(placement.Back?" 背面":" 正面"));Canvas.SetLeft(card,placement.X*2);Canvas.SetTop(card,placement.Y*2);canvas.Children.Add(card);
                    }
                }
                status.Text=$"{plan.Pages.Count} 页 · {plan.Options.CardWidth}×{plan.Options.CardHeight} mm · A4；打印选择实际尺寸 / 100%，按所选长边或短边翻页。";
            }
            catch(Exception error) {status.Text=error.Message;}
            return Task.CompletedTask;
        },()=>documents.Current!=null));
        commands.Register(scope,new("print.export","导出拼版 PDF",()=>
        {
            try
            {
                if(plan==null || revision!=author.View().Source.Version || previewFields!=Fields()) throw new InvalidOperationException("配置或文档已改变，请重新预览");
                PrintLayout.Export(plan,destination.Text ?? "");status.Text="PDF 已导出："+destination.Text;documents.DiscardForm(key);plan=null;
            }
            catch(Exception error) {status.Text=error.Message;}
            return Task.CompletedTask;
        },()=>plan!=null && documents.Current!=null));
        var show=new Button {Content="预览拼版"};var export=new Button {Content="导出 PDF"};var discard=new Button {Content="放弃输入"};
        show.Click+=async (_,_)=>await commands.Execute("print.preview");export.Click+=async (_,_)=>await commands.Execute("print.export");discard.Click+=(_,_)=> {plan=null;documents.DiscardForm(key);};
        Content=new ScrollViewer {Content=new StackPanel {Spacing=10,Children={selected,new WrapPanel {Children={new TextBlock {Text="宽 mm"},width,new TextBlock {Text="高 mm"},height,new TextBlock {Text="页边距 mm"},margin,new TextBlock {Text="间距 mm"},gap,duplex}},destination,new WrapPanel {Children={show,export,discard}},status,pages}}};
        scope.OnDocumentChanged(Refresh);Refresh();
    }
    public void Dispose() {foreach(var image in images) image.Dispose();images.Clear();}
}
