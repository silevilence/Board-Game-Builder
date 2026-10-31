using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BoardGame.Runtime;
namespace BoardGame.Player;

public sealed partial class PlayerWindow
{
    private Menu PlayerMenu()
    {
        MenuItem Item(string title,Action action)
        {var item=new MenuItem {Header=title};item.Click+=(_,_)=>action();return item;}
        var file=new MenuItem {Header="文件 (_F)",ItemsSource=new object[] {
            Item("加载模块…",()=>_=Browse()),
            Item("联机 / 公共观战…",()=> {if(module!=null)new OnlineSetupWindow(module,path.Text!).Show(this);}),
            Item("显示 / 隐藏加载工具栏",()=>SetShell(shell==PlayerShell.Game?PlayerShell.Tools:PlayerShell.Game)),
            new Separator(),Item("退出",Close)}};
        gameMenu.ItemsSource=new object[] {
            Item("开局设置…                 F3",()=>_=ShowSettings()),
            Item("新游戏                       F2",()=>_=HostKey("new-game")),
            Item("重新开始                 Ctrl+R",()=>_=HostKey("restart")),
            new Separator(),Item("结束对局",Stop)};
        boardControls=[Item("撤销一步                 Ctrl+Z",()=>board?.HandleShortcut("undo")),
            Item("提示                             H",()=>board?.HandleShortcut("hint")),
            Item("开启 / 关闭音效",()=>board?.ToggleSound())];
        viewMenu.ItemsSource=new object[] {
            Item("适应窗口",()=> {board?.Fit();localBoard?.Fit();}),
            Item("显示 / 隐藏可用动作",()=> {board?.ToggleActions();localBoard?.ToggleActions();}),
            new Separator(),boardControls[0],boardControls[1],boardControls[2]};
        var help=new MenuItem {Header="帮助 (_H)",ItemsSource=new[] {Item("玩法与操作",ShowHelp)}};
        return new Menu {ItemsSource=new[] {file,gameMenu,viewMenu,help},Padding=new Thickness(5,2),FontSize=14};
    }
    private void SetShell(PlayerShell value)
    {
        shell=value;top.IsVisible=shell==PlayerShell.Tools;message.IsVisible=top.IsVisible || module==null || activeSeed==null;
    }
    private void ApplyTheme()
    {
        RequestedThemeVariant=classic?Avalonia.Styling.ThemeVariant.Light:Avalonia.Styling.ThemeVariant.Default;
        if(classic) Background=new SolidColorBrush(Color.Parse("#d4d0c8"));
        else ClearValue(BackgroundProperty);
    }
    private void UpdateTitle()
    {
        Title=(presentation.Title ?? module?.Id ?? "Board Game Builder")+(activeSeed is {} value?$" · 对局 #{value}":" · 本地 Player");
        foreach(var item in boardControls) item.IsEnabled=board!=null;
    }
    private async Task Browse()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new() {Title="选择游戏包或 game.start.json",AllowMultiple=false});
        if(files.Count==1 && files[0].TryGetLocalPath() is {} file)
        {path.Text=file;await Load();}
    }
    private void Stop()
    {
        generation++;_=ReleaseBoards();activeSeed=null;
        message.Text="对局已结束。可从游戏菜单重新开始或调整开局设置。";message.IsVisible=true;start.IsEnabled=module!=null;UpdateTitle();
    }
    private GameSetup ReadSetup()=>GameSetup.Parse(module!,seed.Text,mode.SelectedIndex,mode.SelectedIndex==0
        ?new[] {(string)seat.SelectedItem!}:(localHumans.Text ?? "").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries));
    private void ApplySetup(GameSetup setup)
    {
        seed.Text=setup.Seed.ToString();mode.SelectedIndex=setup.Mode;seat.SelectedItem=setup.Humans.FirstOrDefault() ?? module!.Initial.Seats[0].Id;localHumans.Text=string.Join(',',setup.Humans);
    }
    private async Task ShowSettings()
    {
        if(module==null || !start.IsEnabled || settingsOpen) return;
        var current=generation;var loaded=module;
        var humans=mode.SelectedIndex==0?new[] {(string)seat.SelectedItem!}:(localHumans.Text ?? "").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        var draft=new GameSetup(uint.TryParse(seed.Text,out var value)?value:42,mode.SelectedIndex,humans);
        settingsOpen=true;
        try
        {
            var location=path.Text!;
            settingsDialog=new GameSetupWindow(loaded,draft,preset=>Task.Run(()=>GameRuntime.Load(location,preset)));
            var chosen=await settingsDialog.ShowDialog<GameSetup?>(this);
            if(chosen!=null && current==generation)
            {
                module=settingsDialog.SelectedModule;presentation=PlayerPresentation.Read(module);
                classic=module.Initial.Entities.Values.Any(e=>e.Properties.GetValueOrDefault("viewTheme")==1);ApplyTheme();
                seat.ItemsSource=module.Initial.Seats.Select(s=>s.Id).ToArray();ApplySetup(chosen);await Start();
            }
        }
        finally {settingsOpen=false;settingsDialog=null;}
    }
    private void ShowHelp()
    {
        var common="F2 新游戏 · F3 开局设置 · Ctrl+R 重开\n查看菜单可适应窗口、显示或隐藏动作栏。\n文件菜单可加载其他模块或显示工具栏。";
        var text=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(22),Text=(presentation.Help ?? "通过棋盘操作或可用动作完成对局；滚轮缩放，中键平移。")+"\n\n"+common};
        var close=new Button {Content="确定",HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(12)};
        var panel=new DockPanel();DockPanel.SetDock(close,Dock.Bottom);panel.Children.Add(close);panel.Children.Add(new ScrollViewer {Content=text});
        var dialog=new Window {Title=(presentation.Title ?? "Player")+" · 帮助",Width=540,Height=550,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=panel};
        close.Click+=(_,_)=>dialog.Close();_=dialog.ShowDialog(this);
    }
}
