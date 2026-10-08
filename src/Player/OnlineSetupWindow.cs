using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BoardGame.Kernel;
using BoardGame.Networking;
using BoardGame.Runtime;

namespace BoardGame.Player;

internal sealed class OnlineSetupWindow : Window
{
    private readonly TextBox server=new() {Text="http://127.0.0.1:5080/"};
    private readonly TextBox key=new() {PasswordChar='●',PlaceholderText="房间密钥"};
    private readonly TextBox certificate=new() {PlaceholderText="可选：测试证书 SHA-256 指纹"};
    private readonly ComboBox preset=new() {HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ComboBox seat=new() {HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly TextBlock status=new() {TextWrapping=TextWrapping.Wrap};
    private readonly CheckBox fresh=new() {Content="使用新身份（服务器重开后选择，放弃本机旧恢复凭据）"};
    private readonly LoadedModule original;
    private readonly string location;
    private bool busy,closed;
    public OnlineSetupWindow(LoadedModule module,string location)
    {
        original=module;this.location=location;
        Title="连接权威服务器";Width=510;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel {Margin=new Thickness(24),Spacing=12};
        panel.Children.Add(new TextBlock {Text="联机 / 公共观战",FontSize=24});
        void Field(string label,Control control) {panel.Children.Add(new TextBlock {Text=label});panel.Children.Add(control);}
        Field("服务器地址",server);Field("房间密钥",key);Field("证书指纹（系统信任的 HTTPS 证书可留空）",certificate);
        var choices=GamePresets.Choices(module);
        preset.ItemsSource=choices.Keys.ToArray();preset.SelectedItem=GamePresets.Selected(module);
        if(choices.Count>0) Field("与服务器一致的游戏配置 / 人数",preset);
        seat.ItemsSource=new[] {"自动选择","公共观战"}.Concat(module.Initial.Seats.Select(s=>s.Id)).ToArray();seat.SelectedIndex=0;
        Field("席位",seat);
        var inspect=new Button {Content="读取房间与可用席位"};inspect.Click+=async (_,_)=>await Run(async ()=>
        {
            using var client=Connection().Open();var room=await client.DescribeAsync();
            var loaded=await LoadModule();
            if(closed) return;
            status.Text=(room.Identity==OnlineIdentity.For(loaded)?"版本和配置一致":"版本或配置不一致，请核对游戏包和人数")+"\n"+
                string.Join("\n",room.Seats.Select(s=>$"{s.Id} · {(s.Controller=="cpu"?"CPU":s.Occupied?"真人（已占用）":"真人（可加入）")}"));
            seat.ItemsSource=new[] {"自动选择","公共观战"}.Concat(room.Seats.Where(s=>s.Controller=="human").Select(s=>s.Id)).ToArray();seat.SelectedIndex=0;
        });
        panel.Children.Add(inspect);
        panel.Children.Add(new TextBlock {Text="服务器控制人数、种子和 CPU。关闭联机窗口视为暂时断线，可重新连接恢复；主动离开会按服务器策略结束对局。",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(status);
        panel.Children.Add(fresh);
        var connect=new Button {Content="加入 / 恢复连接",HorizontalAlignment=HorizontalAlignment.Stretch};
        connect.Click+=async (_,_)=>await Run(async ()=>
        {
            var connection=Connection();var loaded=await LoadModule();if(closed) return;
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BoardGameBuilder","connections");
            var identityPath=Path.Combine(folder,Wire.Hash(connection.Server+loaded.Fingerprint+(connection.Audience?"audience":connection.Seat ?? "auto"))+".json");
            if(fresh.IsChecked==true)File.Delete(identityPath);
            var session=new OnlinePlayerSession(loaded,connection,identityPath);
            try {await session.ConnectAsync();if(closed) {session.Dispose();return;}}
            catch {session.Dispose();throw;}
            var window=new OnlineWindow(loaded,session);window.Show();Close();
        });
        panel.Children.Add(connect);Content=new ScrollViewer {Content=panel,MaxHeight=800};Closed+=(_,_)=>closed=true;
    }
    private OnlineConnection Connection()=>new(server.Text?.Trim() ?? "",key.Text ?? "",seat.SelectedIndex<2?null:seat.SelectedItem as string,seat.SelectedIndex==1,certificate.Text?.Trim());
    private Task<LoadedModule> LoadModule()
    {var selected=preset.SelectedItem as string;return selected==GamePresets.Selected(original)?Task.FromResult(original):Task.Run(()=>GameRuntime.Load(location,selected));}
    private async Task Run(Func<Task> action)
    {
        if(busy || closed)return;busy=true;status.Text="正在连接…";
        try {await action();}catch(Exception e) {if(!closed)status.Text="连接未完成："+e.Message;}
        finally {busy=false;}
    }
}
