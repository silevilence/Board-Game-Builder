using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
namespace BoardGame.Player;

public sealed class PlayerWindow : Window
{
    private readonly TextBox path=new() { Width=340, Watermark="模块目录或 .bgm 路径" };
    private readonly TextBox seed=new() { Width=90, Text="42", Watermark="种子" };
    private readonly ComboBox seat=new() { Width=130 };
    private readonly TextBlock message=new() { Text="加载模块，选择真人席位，其余席位由 CPU 控制。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12) };
    private readonly ContentControl play=new();
    private readonly Button start=new() { Content="开始 / 重新开局",IsEnabled=false };
    private LoadedModule? module;
    private PlayBoard? board;
    private int generation;
    public PlayerWindow(string? initial=null,string? smoke=null)
    {
        Title="Board Game Builder · 单人 Player"; Width=1200; Height=880; MinWidth=800; MinHeight=600;
        path.Text=initial ?? (File.Exists(Path.Combine(AppContext.BaseDirectory,"game.bgm"))?Path.Combine(AppContext.BaseDirectory,"game.bgm"):"samples/quick-cards");
        var load=new Button { Content="加载" }; load.Click+=async (_,_)=>await Load();
        var browse=new Button { Content="选择文件" }; browse.Click+=async (_,_)=>
        {
            var files=await StorageProvider.OpenFilePickerAsync(new() { Title="选择模块包或 module.json",AllowMultiple=false });
            if(files.Count==1 && files[0].TryGetLocalPath() is {} file) { path.Text=Path.GetFileName(file)=="module.json"?Path.GetDirectoryName(file):file; await Load(); }
        };
        start.Click+=async (_,_)=>await Start();
        var stop=new Button { Content="结束对局" }; stop.Click+=(_,_)=> { generation++; board?.Dispose(); board=null; play.Content=null; message.Text="对局已结束，可重新开局。"; start.IsEnabled=module!=null; };
        var top=new WrapPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(10) };
        foreach(var control in new Control[] {path,browse,load,new TextBlock {Text=" 真人：",VerticalAlignment=VerticalAlignment.Center},seat,new TextBlock {Text=" 种子：",VerticalAlignment=VerticalAlignment.Center},seed,start,stop}) { control.Margin=new Thickness(3); top.Children.Add(control); }
        var root=new DockPanel(); DockPanel.SetDock(top,Dock.Top); DockPanel.SetDock(message,Dock.Top); root.Children.Add(top); root.Children.Add(message); root.Children.Add(play); Content=root;
        Closed+=(_,_)=> { generation++; board?.Dispose(); };
        Opened+=async (_,_)=>
        {
            await Load();
            if(smoke!=null) await Smoke(smoke);
        };
    }
    private async Task Load()
    {
        var current=++generation; board?.Dispose(); board=null; play.Content=null; module=null; start.IsEnabled=false;
        var location=path.Text ?? ""; message.Text="正在加载模块…";
        try
        {
            var loaded=await Task.Run(()=>ModulePackage.Load(location));
            if(current!=generation) return;
            module=loaded; seat.ItemsSource=loaded.Initial.Seats.Select(s=>s.Id).ToArray(); seat.SelectedIndex=0;
            message.Text=$"{loaded.Id} · {loaded.Version} | 选择真人席位，其余席位使用 CPU。"; start.IsEnabled=true;
        }
        catch(Exception e) { if(current==generation) message.Text="模块加载失败："+e.Message; }
    }
    private async Task Start()
    {
        if(module==null || seat.SelectedItem is not string human) return;
        if(!uint.TryParse(seed.Text,out var value)) { message.Text="种子必须是 0 至 4294967295 的整数。"; return; }
        var current=++generation; var loaded=module; board?.Dispose(); board=null; play.Content=null; start.IsEnabled=false; message.Text="正在开始新对局…";
        try
        {
            var session=await PlayerSession.CreateAsync(loaded,value,human);
            if(current!=generation) { session.Stop(); return; }
            board=new(session,loaded); play.Content=board; message.Text="选择手牌并拖到目标区域，或使用右侧规则动作。右键平移，滚轮缩放。";
            await board.PumpCpu();
        }
        catch(Exception e) { if(current==generation) message.Text="无法开始："+e.Message; }
        finally { if(current==generation) start.IsEnabled=true; }
    }
    private async Task Smoke(string output)
    {
        try
        {
            var results=new List<string>();
            for(var game=0;game<2;game++)
            {
                await Start();
                for(var i=0;i<40 && board!=null && !board.Frame.View.Ended && board.Frame.Fault==null;i++)
                {
                    await Task.Delay(120);
                    if(board.Frame.Cpu) await board.PumpCpu();
                    else if(board.Frame.Actions.Count>0) await board.Act(new("action",Action:board.Frame.Actions[0].Id),board.Frame.Revision);
                    else break;
                }
                if(board?.Frame.View.Ended!=true) throw new InvalidDataException(board?.Frame.Fault?.Detail ?? message.Text);
                results.Add(Wire.Json(board.Frame.View));
            }
            if(results[0]!=results[1]) throw new InvalidDataException("Restart produced different state");
            File.WriteAllText(output,Wire.Json(new { success=true,games=2,view=board!.Frame.View }));
        }
        catch(Exception e) { File.WriteAllText(output,Wire.Json(new { success=false,error=e.ToString() })); }
        finally { Close(); }
    }
}
