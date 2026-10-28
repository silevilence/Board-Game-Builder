using Avalonia.Controls;
using BoardGame.Kernel;
using BoardGame.View;
namespace BoardGame.Editor;

public sealed class LayoutPanel : UserControl,IDisposable
{
    private readonly InteractiveTable table;
    internal InteractiveTable Table=>table;
    private readonly TextBlock message=new();
    public LayoutPanel(EditorPluginScope context)
    {
        table=context.Service<BoardViewports>("board.viewports").Create(context);
        var kind=new ComboBox {ItemsSource=new[]{"free","square","hex"},SelectedIndex=0};
        var size=new NumericUpDown {Minimum=8,Maximum=1000,Value=70};
        var apply=new Button {Content="应用布局"};
        var top=new StackPanel {Orientation=Avalonia.Layout.Orientation.Horizontal,Children={kind,size,apply,message}};
        var root=new DockPanel(); DockPanel.SetDock(top,Dock.Top); root.Children.Add(top); root.Children.Add(table); Content=root;
        apply.Click+=(_,_)=> { try { context.Document?.EditConfiguration(c=>c["layout"]=System.Text.Json.Nodes.JsonNode.Parse(Wire.Json(new BoardLayout(kind.SelectedItem as string ?? "free",(double)(size.Value ?? 70))))); } catch(Exception e) {message.Text=e.Message;} };
        table.Gesture+=g=> {
            try { if(g.Entity!=null && context.Document is {} doc) Authoring.SetComponent(doc,"entities",g.Entity,"position",new() {["x"]=g.X,["y"]=g.Y}); }
            catch(Exception e) {message.Text=e.Message;}
        };
        table.RenderingError+=e=>message.Text=e;
        void Refresh()
        {
            try
            {
                if(context.Document==null) { table.Show(new([],BoardGame.Kernel.ResourcePool.Empty)); return; }
                var module=context.Document.Snapshot();
                // Author preview shows the initial configuration, never a mutable PlaySession state.
                var state=Wire.Copy(module.Initial); foreach(var entity in state.Entities.Values) { entity.Visibility="public"; entity.FaceUp=true; }
                var view=new BoardGame.Kernel.GameEngine(state).Project(state.Seats[0].Id); table.Show(TableScene.From(view,module.Resources));
                kind.SelectedItem=state.Layout?.Kind ?? "free"; size.Value=(decimal)(state.Layout?.Size ?? 70); message.Text="作者初始局面 · 拖动吸附；六角使用 q/r 坐标";
            }
            catch(Exception e) {message.Text=e.Message;}
        }
        context.OnDocumentChanged(Refresh); Refresh();
    }
    public void Dispose()=>table.Dispose();
}
