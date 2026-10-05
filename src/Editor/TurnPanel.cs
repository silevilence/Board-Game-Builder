using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using BoardGame.Kernel;
namespace BoardGame.Editor;

public sealed class TurnPanel : UserControl
{
    public TurnPanel(PluginContext context)
    {
        var seats=new TextBox {AcceptsReturn=true,MinHeight=160}; var phases=new TextBox {AcceptsReturn=true,MinHeight=160};
        var status=new TextBlock {TextWrapping=Avalonia.Media.TextWrapping.Wrap}; var apply=new Button {Content="应用席位顺序与阶段"};
        Content=new StackPanel {Margin=new Thickness(15),Spacing=10,Children={new TextBlock {Text="按行动顺序，每行填写：席位,队伍,human 或 cpu"},seats,new TextBlock {Text="阶段列表（每行一个，第一项为初始阶段）"},phases,apply,status}};
        void Refresh()
        {
            if(context.Document is not {} doc) { seats.Text=phases.Text=""; return; }
            seats.Text=string.Join("\n",doc.Configuration["seats"]!.AsArray().Select(s=>$"{s!["id"]},{s["team"]},{s["controller"] ?? JsonValue.Create("human")}"));
            phases.Text=string.Join("\n",doc.Configuration["phases"]!.AsArray().Select(p=>p!.GetValue<string>()));
        }
        apply.Click+=(_,_)=> {
            try
            {
                var values=(seats.Text ?? "").Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=> { var v=line.Split(',',StringSplitOptions.TrimEntries); if(v.Length!=3) throw new InvalidDataException("席位行应包含三个字段"); return new Seat(v[0],v[1],v[2]); }).ToArray();
                var stages=(phases.Text ?? "").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                context.Document?.EditConfiguration(c=> { c["seats"]=JsonNode.Parse(Wire.Json(values)); c["phases"]=JsonNode.Parse(Wire.Json(stages)); });
                _=context.Document?.Snapshot(); status.Text="已保存到文档；试玩重开后生效。";
            }
            catch(Exception e) {status.Text=e.Message;}
        };
        context.OnDocumentChanged(Refresh); Refresh();
    }
}
