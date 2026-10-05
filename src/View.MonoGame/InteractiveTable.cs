using Avalonia.Controls;
namespace BoardGame.View;

public sealed record TableGesture(string Kind,string? Entity,string? Target,float X,float Y);
public sealed class InteractiveTable : UserControl, IDisposable
{
    private readonly DesktopViewport viewport=new();
    private TableScene scene=new([],new Dictionary<string,byte[]>());
    private string? dragging,target;
    private float startX,startY;
    public string? Selected { get; private set; }
    public bool InputEnabled { get; set; }=true;
    public event Action<TableGesture>? Gesture;
    public InteractiveTable()
    {
        Content=viewport;
        viewport.Paint=r=>ScenePainter.Draw(r,scene,Selected,target);
        viewport.Input+=(kind,x,y)=>
        {
            if(kind=="cancel") { dragging=null; target=null; return; }
            if(!InputEnabled) return;
            if(kind=="down") { Selected=scene.Hit(x,y)?.Entity; dragging=Selected; startX=x; startY=y; }
            if(kind=="move" && dragging!=null) target=scene.Region(x,y)?.Entity;
            if(kind=="up")
            {
                var entity=dragging; dragging=null; target=null;
                if(entity!=null && Math.Abs(x-startX)+Math.Abs(y-startY)>8)
                    Gesture?.Invoke(new("drag",entity,scene.Region(x,y)?.Entity,x,y));
            }
        };
    }
    public void Show(TableScene value) { scene=value; dragging=null; target=null; if(!scene.Items.Any(i=>i.Entity==Selected)) Selected=null; }
    public void Dispose() { viewport.Dispose(); Gesture=null; }
}
