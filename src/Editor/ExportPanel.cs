using Avalonia;
using Avalonia.Controls;
namespace BoardGame.Editor;

public sealed class ExportPanel : UserControl,IDisposable
{
    private readonly ExportWorkflow workflow;
    private readonly TextBlock progress=new() {TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly Button preflight=new() {Content="依赖预检"};
    private readonly Button export=new() {Content="导出当前草稿的独立游戏"};
    private readonly ProgressBar bar=new() {IsIndeterminate=true,IsVisible=false};
    public ExportPanel(EditorPluginScope scope)
    {
        workflow=scope.Service<ExportWorkflow>("exports");
        var release=new TextBox {Text=workflow.ReleasePath,PlaceholderText="官方 Player 发行目录"};
        var destination=new TextBox {Text=workflow.Destination,PlaceholderText="新的输出目录"};
        Content=new StackPanel {Margin=new Thickness(20),Spacing=12,Children={new TextBlock {Text="预编译 Player（导出不调用 SDK）"},release,new TextBlock {Text="输出目录（必须尚不存在）"},destination,preflight,export,bar,progress}};
        release.PropertyChanged+=(_,e)=> {if(e.Property==TextBox.TextProperty) workflow.ReleasePath=release.Text ?? "";};
        destination.PropertyChanged+=(_,e)=> {if(e.Property==TextBox.TextProperty) workflow.Destination=destination.Text ?? "";};
        preflight.Click+=async (_,_)=>await workflow.Run(false);export.Click+=async (_,_)=>await workflow.Run(true);
        workflow.Changed+=Refresh;Refresh();
    }
    private void Refresh() {preflight.IsEnabled=export.IsEnabled=!workflow.Running;bar.IsVisible=workflow.Running;progress.Text=workflow.Progress;}
    public void Dispose()=>workflow.Changed-=Refresh;
}
