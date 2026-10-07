using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BoardGame.Kernel;
namespace BoardGame.Player;

internal sealed record GameSetup(uint Seed,int Mode,string[] Humans)
{
    public static GameSetup Parse(LoadedModule module,string? seed,int mode,IEnumerable<string> humans)
    {
        if(!uint.TryParse(seed,out var value)) throw new InvalidDataException("种子必须是 0 至 4294967295 的整数。");
        var seats=humans.ToArray();
        if(mode is <0 or >2) throw new InvalidDataException("请选择有效的本地模式。");
        if(seats.Length==0 || seats.Distinct().Count()!=seats.Length || seats.Any(id=>!module.Initial.Seats.Any(s=>s.Id==id)))
            throw new InvalidDataException("至少选择一个有效的真人席位，其余席位由 CPU 控制。");
        if(mode==0 && seats.Length!=1) throw new InvalidDataException("单人模式请选择一名真人；多名真人请选择热座或同屏分视角。");
        return new(value,mode,seats);
    }
}

internal sealed class GameSetupWindow : Window
{
    internal readonly TextBox Seed=new();
    internal readonly ComboBox Mode=new() {ItemsSource=new[] {"单人 + CPU","本地热座","同屏分视角"},HorizontalAlignment=HorizontalAlignment.Stretch};
    internal readonly Dictionary<string,CheckBox> Humans=[];
    private readonly LoadedModule module;
    private readonly TextBlock error=new() {TextWrapping=TextWrapping.Wrap,Foreground=Brushes.IndianRed};
    public GameSetupWindow(LoadedModule module,GameSetup current)
    {
        this.module=module;
        Title="开局设置";Width=470;SizeToContent=SizeToContent.Height;CanResize=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Seed.Text=current.Seed.ToString();Mode.SelectedIndex=current.Mode;
        var panel=new StackPanel {Spacing=12,Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock {Text="开局设置",FontSize=24});
        panel.Children.Add(new TextBlock {Text="确认后开始新对局；取消保留当前对局。",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock {Text="种子"});panel.Children.Add(Seed);
        panel.Children.Add(new TextBlock {Text="本地模式"});panel.Children.Add(Mode);
        panel.Children.Add(new TextBlock {Text="真人席位（未勾选的席位由 CPU 控制）"});
        foreach(var seat in module.Initial.Seats)
        {
            var check=new CheckBox {Content=seat.Id,IsChecked=current.Humans.Contains(seat.Id)};
            Humans.Add(seat.Id,check);panel.Children.Add(check);
        }
        panel.Children.Add(new TextBlock {Text="热座交接时先移开视线，再由下一位玩家接手。同屏模式下，各玩家能旁观彼此画面。",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(error);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=10};
        var cancel=new Button {Content="取消"};cancel.Click+=(_,_)=>Close();
        var start=new Button {Content="开始新对局"};start.Click+=(_,_)=>Confirm();
        buttons.Children.Add(cancel);buttons.Children.Add(start);panel.Children.Add(buttons);
        Content=new ScrollViewer {Content=panel,MaxHeight=700};
    }
    internal bool TryRead(out GameSetup? setup)
    {
        try {setup=GameSetup.Parse(module,Seed.Text,Mode.SelectedIndex,Humans.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key));return true;}
        catch(InvalidDataException e) {error.Text=e.Message;setup=null;return false;}
    }
    internal void Confirm() {if(TryRead(out var setup)) Close(setup);}
}
