using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BoardGame.Kernel;
using BoardGame.Modules;
using BoardGame.Runtime;
using BoardGame.View;
namespace BoardGame.Player;

public sealed partial class PlayerWindow : Window
{
    private readonly TextBox path=new() { Width=340, PlaceholderText="模块目录或 .bgm 路径" };
    private readonly TextBox seed=new() { Width=90, Text="42", PlaceholderText="种子" };
    private readonly ComboBox seat=new() { Width=130 };
    private readonly TextBlock message=new() { Text="加载模块，选择真人席位，其余席位由 CPU 控制。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12) };
    private readonly ContentControl play=new();
    private readonly Button start=new() { Content="开始 / 重新开局",IsEnabled=false };
    private LoadedModule? module;
    private PlayBoard? board;
    private LocalBoard? localBoard;
    private readonly ComboBox mode=new() {Width=130,ItemsSource=new[] {"单人 + CPU","本地热座","同屏分视角"},SelectedIndex=0};
    private readonly TextBox localHumans=new() {Width=140,PlaceholderText="真人席位，如 p1,p2"};
    private int generation;
    private uint? activeSeed;
    private bool classic;
    private readonly Border classicBar=new() {IsVisible=false};
    private readonly WrapPanel top=new() {Orientation=Orientation.Horizontal,Margin=new Thickness(10)};
    public PlayerWindow(string? initial=null,string? smoke=null,string? smokeActions=null,string? verifyBackground=null)
    {
        Title="Board Game Builder · 本地 Player"; Width=1200; Height=880; MinWidth=800; MinHeight=600;
        path.Text=initial ?? (File.Exists(Path.Combine(AppContext.BaseDirectory,"game.bgm"))?Path.Combine(AppContext.BaseDirectory,"game.bgm"):"samples/quick-cards");
        var load=new Button { Content="加载" }; load.Click+=async (_,_)=>await Load();
        var browse=new Button { Content="选择文件" }; browse.Click+=async (_,_)=>
        {
            var files=await StorageProvider.OpenFilePickerAsync(new() { Title="选择模块包或 module.json",AllowMultiple=false });
            if(files.Count==1 && files[0].TryGetLocalPath() is {} file) { path.Text=Path.GetFileName(file)=="module.json"?Path.GetDirectoryName(file):file; await Load(); }
        };
        start.Click+=async (_,_)=>await Start();
        var stop=new Button { Content="结束对局" }; stop.Click+=(_,_)=> { generation++; localBoard?.Dispose();localBoard=null; board?.Dispose(); board=null; play.Content=null; message.Text="对局已结束，可重新开局。"; start.IsEnabled=module!=null; };
        foreach(var control in new Control[] {path,browse,load,new TextBlock {Text=" 真人：",VerticalAlignment=VerticalAlignment.Center},seat,new TextBlock {Text=" 种子：",VerticalAlignment=VerticalAlignment.Center},seed,start,stop}) { control.Margin=new Thickness(3); top.Children.Add(control); }
        top.Children.Add(mode);top.Children.Add(localHumans);
        classicBar.Child=ClassicMenu();
        var root=new DockPanel(); DockPanel.SetDock(classicBar,Dock.Top); DockPanel.SetDock(top,Dock.Top); DockPanel.SetDock(message,Dock.Top); root.Children.Add(classicBar); root.Children.Add(top); root.Children.Add(message); root.Children.Add(play); Content=root;
        Closed+=(_,_)=> { generation++; board?.Dispose();localBoard?.Dispose(); };
        TableKeyInput.Attach(this,key=>
        {
            if(key is "new-game" or "restart" or "choose-seed" || board==null && key=="confirm") _=HostKey(key);
            else board?.HandleShortcut(key);
        });
        seed.KeyDown+=async (_,e)=> { if(e.Key==Avalonia.Input.Key.Enter) { e.Handled=true; await Start(); } };
        Opened+=async (_,_)=>
        {
            await Load();
            if(verifyBackground!=null) { await VerifyBackground(verifyBackground); return; }
            if(smoke!=null) await Smoke(smoke,smokeActions);
        };
    }
    private async Task HostKey(string key)
    {
        if(key=="choose-seed") { top.IsVisible=true; message.IsVisible=true; seed.Focus(); seed.SelectAll(); message.Text="输入种子后按 Enter 开始该牌局。"; return; }
        if(!start.IsEnabled) return;
        if(key=="new-game") seed.Text=System.Security.Cryptography.RandomNumberGenerator.GetInt32(1,int.MaxValue).ToString();
        if(key=="restart" && activeSeed is {} previous) seed.Text=previous.ToString();
        await Start();
    }
    private async Task Load()
    {
        var current=++generation; board?.Dispose(); board=null; play.Content=null; module=null; activeSeed=null; start.IsEnabled=false;
        localBoard?.Dispose();localBoard=null;
        var location=path.Text ?? ""; message.Text="正在加载模块…";
        try
        {
            var loaded=await Task.Run(()=>ModulePackage.Load(location));
            if(current!=generation) return;
            module=loaded; seat.ItemsSource=loaded.Initial.Seats.Select(s=>s.Id).ToArray(); seat.SelectedIndex=0;
            var defaultHumans=loaded.Initial.Seats.Where(s=>s.Controller=="human").Select(s=>s.Id).ToArray();
            localHumans.Text=string.Join(",",defaultHumans.Length>0?defaultHumans:[loaded.Initial.Seats[0].Id]);
            mode.SelectedIndex=defaultHumans.Length>1?1:0;
            classic=loaded.Initial.Entities.Values.Any(e=>e.Properties.GetValueOrDefault("viewTheme")==1);
            classicBar.IsVisible=classic; top.IsVisible=!classic; message.IsVisible=!classic;
            RequestedThemeVariant=classic?Avalonia.Styling.ThemeVariant.Light:Avalonia.Styling.ThemeVariant.Default;
            if(classic) Background=new SolidColorBrush(Color.Parse("#d4d0c8"));
            else ClearValue(BackgroundProperty); // Restore the theme brush; a local null suppresses it.
            message.Text=$"{loaded.Id} · {loaded.Version} | 选择真人席位，其余席位使用 CPU。"; start.IsEnabled=true;
            if(classic) await Start();
        }
        catch(Exception e) { if(current==generation) {message.IsVisible=true; top.IsVisible=true; message.Text="模块加载失败："+e.Message;} }
    }
    private async Task Start()
    {
        if(module==null || seat.SelectedItem is not string human) return;
        if(!uint.TryParse(seed.Text,out var value)) { message.Text="种子必须是 0 至 4294967295 的整数。"; return; }
        var current=++generation; var loaded=module; board?.Dispose(); board=null; play.Content=null; start.IsEnabled=false; message.Text="正在开始新对局…";
        localBoard?.Dispose();localBoard=null;
        try
        {
            if(mode.SelectedIndex>0 && !classic)
            {
                var humans=(localHumans.Text ?? "").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
                if(humans.Length==0 || humans.Any(id=>!loaded.Initial.Seats.Any(s=>s.Id==id))) throw new InvalidDataException("请输入有效的真人席位，以逗号分隔；其余席位由 CPU 控制。");
                var created=await LocalBoard.Create(loaded,value,loaded.Initial.Seats.ToDictionary(s=>s.Id,s=>humans.Contains(s.Id)?"human":"cpu"),mode.SelectedIndex==1?LocalTableMode.HotSeat:LocalTableMode.SplitScreen);
                if(current!=generation) {created.Dispose();return;}localBoard=created;
                play.Content=localBoard;activeSeed=value;message.Text="热座交接请移开视线；同屏各视口显示其控制席位。";await localBoard.PumpCpu();return;
            }
            var session=await PlayerSession.CreateAsync(loaded,value,human);
            if(current!=generation) { session.Stop(); return; }
            activeSeed=value; board=new(session,loaded); board.HostShortcut+=key=>_=HostKey(key); play.Content=board;
            if(classic) {Title="空当接龙 · 牌局 #"+value; top.IsVisible=false; message.IsVisible=false;}
            message.Text=classic?"点牌再点目标 / 拖动 · 双击快速移牌 · 按住右键看牌 · 中键或空白处右拖平移 · H 提示 · F2 新局 / F3 种子 / Ctrl+R 重开"
                :"双击对象执行可用动作，也可使用右侧动作栏 · 滚轮缩放 · 中键平移 · F2 新局 / F3 种子 / Ctrl+R 重开";
            await board.PumpCpu();
        }
        catch(Exception e) { if(current==generation) {message.IsVisible=true; message.Text="无法开始："+e.Message;} }
        finally { if(current==generation) start.IsEnabled=true; }
    }
    private async Task Smoke(string output,string? actionsFile=null)
    {
        try
        {
            var results=new List<string>();
            var interactionMs=new List<double>();
            var inputCounts=new Dictionary<string,int>();
            var commands=actionsFile==null?null:Wire.Read<List<Command>>(File.ReadAllText(actionsFile));
            if(commands!=null) seed.Text="1";
            for(var game=0;game<2;game++)
            {
                await Start();
                for(var i=0;i<(commands?.Count ?? 40) && board!=null && !board.Frame.View.Ended && board.Frame.Fault==null;i++)
                {
                    await Task.Delay(120);
                    if(board.Frame.Cpu) await board.PumpCpu();
                    else if(board.Frame.Actions.Count>0)
                    {
                        var clock=System.Diagnostics.Stopwatch.StartNew();
                        var offer=commands==null?board.Frame.Actions[0]:board.Frame.Actions.Single(a=>Wire.Json(a.Command)==Wire.Json(commands[i]));
                        var intent=commands!=null && offer.Shortcuts?.Contains("double-click")==true
                            ?new InteractionIntent("double-click",offer.Command.Entity)
                            :commands!=null && offer.Gesture=="drag"
                                ?new InteractionIntent("click",offer.Command.Entity,offer.Target)
                                :new InteractionIntent("action",Action:offer.Id);
                        inputCounts[intent.Kind]=inputCounts.GetValueOrDefault(intent.Kind)+1;
                        await board.Act(intent,board.Frame.Revision);
                        interactionMs.Add(clock.Elapsed.TotalMilliseconds);
                    }
                    else break;
                }
                if(board?.Frame.View.Ended!=true) throw new InvalidDataException(board?.Frame.Fault?.Detail ?? message.Text);
                if(commands!=null && !board.Frame.View.Winners.Contains("p1")) throw new InvalidDataException("Scripted smoke did not win");
                results.Add(Wire.Json(board.Frame.View));
            }
            if(results[0]!=results[1]) throw new InvalidDataException("Restart produced different state");
            File.WriteAllText(output,Wire.Json(new { success=true,games=2,inputCounts,interactionMs,view=board!.Frame.View }));
        }
        catch(Exception e) { File.WriteAllText(output,Wire.Json(new { success=false,error=e.ToString() })); }
        finally { Close(); }
    }
}
