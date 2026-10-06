using Avalonia;
using Avalonia.Controls;
using BoardGame.Modules;
namespace BoardGame.Editor;

public sealed class ExportPanel : UserControl
{
    public ExportPanel(Func<ModuleDocument?> document,PluginHost plugins)
    {
        var release=new TextBox {Text=EditorPaths.InWorkspace("artifacts/player-release-m2"),PlaceholderText="官方 Player 发行目录"};
        var destination=new TextBox {Text=EditorPaths.InWorkspace("artifacts/export-m2"),PlaceholderText="新的输出目录"};
        var progress=new TextBlock {TextWrapping=Avalonia.Media.TextWrapping.Wrap};
        var preflight=new Button {Content="依赖预检"}; var export=new Button {Content="保存并导出独立游戏"}; var bar=new ProgressBar {IsIndeterminate=true,IsVisible=false};
        Content=new StackPanel {Margin=new Thickness(20),Spacing=12,Children={new TextBlock {Text="预编译 Player（导出不调用 SDK）"},release,new TextBlock {Text="输出目录（必须尚不存在）"},destination,preflight,export,bar,progress}};
        preflight.Click+=async (_,_)=>await Run(false); export.Click+=async (_,_)=>await Run(true);
        async Task Run(bool write)
        {
            preflight.IsEnabled=export.IsEnabled=false; bar.IsVisible=true;
            var temp=Path.Combine(Path.GetTempPath(),"bgb-export-"+Guid.NewGuid());
            try
            {
                var doc=document() ?? throw new InvalidOperationException("请打开并保存模块");
                if(doc.DirectoryPath==null) throw new InvalidOperationException("请先在模块配置页设置目录并保存");
                doc.Save(); var saved=ModuleDocument.Open(doc.DirectoryPath); saved.Save(temp);
                var player=release.Text ?? ""; var output=destination.Text ?? "";
                var steps=plugins.Extensions.Where(e=>e.Kind==ExtensionKind.ExportStep).ToArray();
                var reporter=new Progress<string>(text=>progress.Text=text);
                await Task.Run(()=> {
                    if(write) PlayerDistribution.Export(player,temp,output,staging=> Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=> {
                        foreach(var step in steps)
                        {
                            if(!plugins.Extensions.Any(current=>ReferenceEquals(current,step))) throw new InvalidOperationException("导出插件已停用或重载："+step.Plugin);
                            if(step.Value is Action<ExportContext> action) action(new(temp,staging));
                            else throw new InvalidDataException("Invalid export step: "+step.Id);
                        }
                    }).GetAwaiter().GetResult(),reporter);
                    else _=PlayerDistribution.Preflight(player,temp);
                });
                progress.Text=write?"导出完成。双击输出目录中的 Play.exe。":"依赖预检通过。";
            }
            catch(Exception e) {progress.Text="导出失败："+e.Message;}
            finally {if(Directory.Exists(temp)) Directory.Delete(temp,true); preflight.IsEnabled=export.IsEnabled=true; bar.IsVisible=false;}
        }
    }
}
