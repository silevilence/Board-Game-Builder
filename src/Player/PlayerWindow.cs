using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly ComboBox mode=new() {Width=130,ItemsSource=new[] {"单人 + CPU","本地热座","同屏分视角","全 CPU 观战"},SelectedIndex=0};
    private readonly TextBox localHumans=new() {Width=140,PlaceholderText="真人席位，如 p1,p2"};
    private int generation;
    private uint? activeSeed;
    private bool classic;
    private PlayerShell shell;
    private PlayerPresentation presentation=new();
    private readonly Border menuBar=new();
    private readonly MenuItem gameMenu=new() {Header="游戏 (_G)"};
    private readonly MenuItem viewMenu=new() {Header="查看 (_V)"};
    private MenuItem[] boardControls=[];
    private bool settingsOpen;
    private GameSetupWindow? settingsDialog;
    private readonly WrapPanel top=new() {Orientation=Orientation.Horizontal,Margin=new Thickness(10)};
    public PlayerWindow(string? initial=null,string? smoke=null,string? smokeActions=null,string? verifyBackground=null,PlayerShell? shell=null,string? verifyShell=null)
    {
        Title="Board Game Builder · 本地 Player"; Width=1200; Height=880; MinWidth=800; MinHeight=600;
        var bundled=Path.Combine(AppContext.BaseDirectory,"game.bgm");
        this.shell=shell ?? (initial!=null || File.Exists(bundled)?PlayerShell.Game:PlayerShell.Tools);
        path.Text=initial ?? (File.Exists(bundled)?bundled:"samples/quick-cards");
        var load=new Button { Content="加载" }; load.Click+=async (_,_)=>await Load();
        var browse=new Button { Content="选择文件" }; browse.Click+=async (_,_)=>await Browse();
        start.Click+=async (_,_)=>await Start();
        var stop=new Button { Content="结束对局" }; stop.Click+=(_,_)=>Stop();
        foreach(var control in new Control[] {path,browse,load,new TextBlock {Text=" 真人：",VerticalAlignment=VerticalAlignment.Center},seat,new TextBlock {Text=" 种子：",VerticalAlignment=VerticalAlignment.Center},seed,start,stop}) { control.Margin=new Thickness(3); top.Children.Add(control); }
        top.Children.Add(mode);top.Children.Add(localHumans);
        menuBar.Child=PlayerMenu();
        var root=new DockPanel(); DockPanel.SetDock(menuBar,Dock.Top); DockPanel.SetDock(top,Dock.Top); DockPanel.SetDock(message,Dock.Top); root.Children.Add(menuBar); root.Children.Add(top); root.Children.Add(message); root.Children.Add(play); Content=root;
        SetShell(this.shell);
        Closed+=(_,_)=> { generation++; board?.Dispose();localBoard?.Dispose(); };
        TableKeyInput.Attach(this,key=>
        {
            if(key is "new-game" or "restart" or "choose-seed" || board==null && localBoard==null && key=="confirm") _=HostKey(key);
            else board?.HandleShortcut(key);
        });
        seed.KeyDown+=async (_,e)=> { if(e.Key==Avalonia.Input.Key.Enter) { e.Handled=true; await Start(); } };
        Opened+=async (_,_)=>
        {
            await Load();
            if(verifyShell!=null) {await VerifyShell(verifyShell);return;}
            if(verifyBackground!=null) { await VerifyBackground(verifyBackground); return; }
            if(smoke!=null) await Smoke(smoke,smokeActions);
        };
    }
    private async Task HostKey(string key)
    {
        if(key=="choose-seed") {await ShowSettings();return;}
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
        gameMenu.IsEnabled=false;viewMenu.IsEnabled=false;
        presentation=new();classic=false;ApplyTheme();Title="Board Game Builder · 本地 Player";
        try
        {
            var loaded=await Task.Run(()=>GameRuntime.Load(location));
            if(current!=generation) return;
            presentation=PlayerPresentation.Read(loaded);
            if(presentation.Width is {} preferredWidth)Width=Math.Min(preferredWidth,(Screens.Primary?.WorkingArea.Width ?? preferredWidth)/RenderScaling);
            if(presentation.Height is {} preferredHeight)Height=Math.Min(preferredHeight,(Screens.Primary?.WorkingArea.Height ?? preferredHeight)/RenderScaling);
            module=loaded; seat.ItemsSource=loaded.Initial.Seats.Select(s=>s.Id).ToArray();
            var defaultHumans=loaded.Initial.Seats.Where(s=>s.Controller=="human").Select(s=>s.Id).ToArray();
            localHumans.Text=string.Join(",",defaultHumans.Length>0?defaultHumans:[loaded.Initial.Seats[0].Id]);
            seat.SelectedItem=defaultHumans.FirstOrDefault() ?? loaded.Initial.Seats[0].Id;
            mode.SelectedIndex=defaultHumans.Length==0?3:defaultHumans.Length>1?1:0;
            classic=loaded.Initial.Entities.Values.Any(e=>e.Properties.GetValueOrDefault("viewTheme")==1);
            ApplyTheme();SetShell(shell);UpdateTitle();
            message.Text=$"{loaded.Id} · {loaded.Version} | 选择真人席位，其余席位使用 CPU。"; start.IsEnabled=true;
            gameMenu.IsEnabled=true;viewMenu.IsEnabled=true;
            if(shell==PlayerShell.Game) await Start();
        }
        catch(Exception e) { if(current==generation) {message.IsVisible=true;message.Text="模块加载失败："+e.Message+"\n可从文件菜单重新选择模块。";} }
    }
    private async Task Start()
    {
        if(module==null || seat.SelectedItem is not string human) return;
        GameSetup setup;
        try {setup=ReadSetup();}
        catch(InvalidDataException e) {message.IsVisible=true;message.Text=e.Message;return;}
        var value=setup.Seed;
        var current=++generation; var loaded=module; board?.Dispose(); board=null; play.Content=null; activeSeed=null;UpdateTitle();start.IsEnabled=false; message.Text="正在开始新对局…";
        localBoard?.Dispose();localBoard=null;
        try
        {
            if(setup.Mode>0)
            {
                var created=await LocalBoard.Create(loaded,value,loaded.Initial.Seats.ToDictionary(s=>s.Id,s=>setup.Humans.Contains(s.Id)?"human":"cpu"),setup.Mode==2?LocalTableMode.SplitScreen:LocalTableMode.HotSeat,presentation.ShowActions);
                if(current!=generation) {created.Dispose();return;}localBoard=created;
                play.Content=localBoard;activeSeed=value;SetShell(shell);UpdateTitle();message.Text="热座交接请移开视线；同屏各视口显示其控制席位。";await localBoard.PumpCpu();return;
            }
            var session=await PlayerSession.CreateAsync(loaded,value,human);
            if(current!=generation) { session.Stop(); return; }
            activeSeed=value; board=new(session,loaded,presentation.ShowActions); board.HostShortcut+=key=>_=HostKey(key); play.Content=board;
            SetShell(shell);UpdateTitle();
            message.Text=classic?"点牌再点目标 / 拖动 · 双击快速移牌 · 按住右键看牌 · 中键或空白处右拖平移 · H 提示 · F2 新局 / F3 开局设置 / Ctrl+R 重开"
                :"双击对象或点击可用动作 · 滚轮缩放 · 中键平移 · F2 新局 / F3 种子 / Ctrl+R 重开";
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
                    for(var waiting=0;board.PresentationBusy && waiting<400;waiting++)await Task.Delay(30);
                    if(board.PresentationBusy)throw new InvalidDataException("Presentation did not complete");
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
