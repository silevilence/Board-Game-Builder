using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace BoardGame.Player;

public sealed partial class PlayerWindow
{
    private Control ClassicMenu()
    {
        MenuItem Item(string title,Action action)
        {var item=new MenuItem {Header=title}; item.Click+=(_,_)=>action(); return item;}
        var game=new MenuItem {Header="游戏 (_G)",ItemsSource=new object[] {
            Item("新游戏                 F2",()=>_=HostKey("new-game")),
            Item("重新开始             Ctrl+R",()=>_=HostKey("restart")),
            Item("选择牌局             F3",()=>_=HostKey("choose-seed")),
            new Separator(),Item("撤销一步             Ctrl+Z / F10",()=>board?.HandleShortcut("undo")),
            Item("提示                     H",()=>board?.HandleShortcut("hint")),
            new Separator(),Item("模块设置",()=> {top.IsVisible=!top.IsVisible; message.IsVisible=top.IsVisible;}),Item("退出",Close)}};
        var view=new MenuItem {Header="查看 (_V)",ItemsSource=new object[] {
            Item("适应窗口",()=>board?.Fit()),Item("显示 / 隐藏可用动作",()=>board?.ToggleActions()),Item("开启 / 关闭音效",()=>board?.ToggleSound())}};
        var help=new MenuItem {Header="帮助 (_H)",ItemsSource=new[] {Item("玩法与快捷键",ShowClassicHelp)}};
        return new Menu {ItemsSource=new[]{game,view,help},Background=new SolidColorBrush(Color.Parse("#d4d0c8")),Padding=new Thickness(5,2),FontSize=14};
    }
    private void ShowClassicHelp()
    {
        var text=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(22),Text=
            "空当接龙\n\n将全部牌按同花色 A → K 移入右上方收牌位。牌列按红黑交替、点数递减排列，左上四个空当各暂存一张牌。\n\n点牌再点目标，或直接拖动。双击优先收牌，否则进入空当；按住右键查看被遮挡的完整牌。\n\n1～8 选列 / 移到列 · 0 进空当 · 9 收牌\nCtrl+Z / F10 / U 撤销一步 · H 提示 · Esc 取消\nF2 新局 · F3 选择种子 · Ctrl+R 重开\nEnter 洗牌 / 发牌\n\n滚轮缩放；中键或空白处右拖平移；查看 → 适应窗口恢复全桌。查看 → 可用动作可展开按钮面板。\n\nKenney Playing Cards Pack · CC0\n本引擎种子不对应微软牌局编号。"};
        var close=new Button {Content="确定",HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(12)};
        var panel=new DockPanel(); DockPanel.SetDock(close,Dock.Bottom); panel.Children.Add(close); panel.Children.Add(text);
        var dialog=new Window {Title="空当接龙 · 帮助",Width=510,Height=530,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=panel,RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light};
        close.Click+=(_,_)=>dialog.Close(); _=dialog.ShowDialog(this);
    }
}
