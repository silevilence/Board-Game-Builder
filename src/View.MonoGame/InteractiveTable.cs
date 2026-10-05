using Avalonia.Controls;
namespace BoardGame.View;

public sealed record TableGesture(string Kind,string? Entity,string? Target,double X,double Y);
public sealed class InteractiveTable : UserControl, IDisposable
{
    private readonly DesktopViewport viewport=new();
    private TableScene scene=new([],new Dictionary<string,byte[]>());
    private string? dragging,target;
    private float startX,startY;
    private float pointerX,pointerY;
    public string? Selected { get; private set; }
    public bool InputEnabled { get; set; }=true;
    public int RenderedFrames => viewport.Frames;
    public event Action<TableGesture>? Gesture;
    public event Action<string>? RenderingError;
    public InteractiveTable()
    {
        var controls=new StackPanel { Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=6 };
        var zoomIn=new Button { Content="放大 +" }; var zoomOut=new Button { Content="缩小 −" };
        zoomIn.Click+=(_,_)=>viewport.ChangeZoom(1.1f); zoomOut.Click+=(_,_)=>viewport.ChangeZoom(1/1.1f);
        controls.Children.Add(zoomIn); controls.Children.Add(zoomOut);
        var panel=new DockPanel(); DockPanel.SetDock(controls,Dock.Top); panel.Children.Add(controls); panel.Children.Add(viewport); Content=panel;
        viewport.Paint=r=>
        {
            ScenePainter.Draw(r,scene,Selected,target);
            if(dragging!=null && scene.Items.FirstOrDefault(i=>i.Entity==dragging) is {} item && Math.Abs(pointerX-startX)+Math.Abs(pointerY-startY)>8)
            {
                var b=item.Bounds;
                r.Box(new((int)(b.X+pointerX-startX),(int)(b.Y+pointerY-startY),(int)b.Width,(int)b.Height),new Microsoft.Xna.Framework.Color(225,197,137,190));
                r.Text(item.Label,(int)(b.X+pointerX-startX+4),(int)(b.Y+pointerY-startY+4),Microsoft.Xna.Framework.Color.Black);
            }
        };
        viewport.Input+=(kind,x,y)=>
        {
            if(kind=="error") { InputEnabled=false; RenderingError?.Invoke(viewport.Error ?? "Unknown graphics device error"); return; }
            if(kind=="cancel") { dragging=null; target=null; return; }
            if(!InputEnabled) return;
            if(kind=="down") { Selected=scene.Hit(x,y)?.Entity; dragging=Selected; startX=pointerX=x; startY=pointerY=y; }
            if(kind=="move" && dragging!=null) { pointerX=x; pointerY=y; target=scene.Region(x,y)?.Entity; }
            if(kind=="up")
            {
                var entity=dragging; dragging=null; target=null;
                if(entity!=null && Math.Abs(x-startX)+Math.Abs(y-startY)>8)
                {
                    var item=scene.Items.First(i=>i.Entity==entity); var position=item.MoveBy(x-startX,y-startY);
                    Gesture?.Invoke(new("drag",entity,scene.Region(x,y)?.Entity,position.X,position.Y));
                }
            }
        };
    }
    public void Show(TableScene value) { scene=value; dragging=null; target=null; if(!scene.Items.Any(i=>i.Entity==Selected)) Selected=null; }
    public void Dispose() { viewport.Dispose(); Gesture=null; RenderingError=null; }
}
