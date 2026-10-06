namespace BoardGame.View;

public sealed record TableGesture(string Kind,string? Entity,string? Target,double X=0,double Y=0);

// Pure pointer state shared by all hosts. No game rules and no access to authority data.
public sealed class TableInteraction
{
    private TableScene scene=new([],new Dictionary<string,byte[]>());
    private string? pressed, selectionAtDown;
    private bool down, moved, enabled=true;
    private float startX,startY;
    public string? Selected { get; private set; }
    public string? Target { get; private set; }
    public string? Peeked { get; private set; }
    public string? Dragging => down && moved?pressed:null;
    public float DeltaX { get; private set; }
    public float DeltaY { get; private set; }
    public bool Enabled { get=>enabled; set { enabled=value; if(!value) Cancel(); } }
    public event Action<TableGesture>? Gesture;
    public event Action? SelectionChanged;
    public void Show(TableScene value)
    {
        scene=value; Cancel();
        if(!scene.Items.Any(i=>i.Entity==Selected)) Select(null);
    }
    public bool CanPeek(float x,float y)=>scene.Hit(x,y) is {Kind:"card",Entity:not null};
    public void Cancel(bool clearSelection=false)
    {
        down=false; moved=false; pressed=null; selectionAtDown=null; Peeked=null; Target=null;
        if(clearSelection) Select(null);
    }
    public void Select(string? entity,string? target=null) { Selected=entity; Target=target; SelectionChanged?.Invoke(); }
    public void Pointer(string kind,float x,float y)
    {
        if(kind=="cancel") { Cancel(true); return; }
        if(kind=="right-up") { Peeked=null; return; }
        if(!enabled) return;
        var hit=scene.Hit(x,y);
        if(kind=="right-down") { Cancel(); Peeked=hit is {Kind:"card"}?hit.Entity:null; return; }
        if(kind=="double-click")
        {
            Cancel(true);
            if(hit is {Kind:not "region",Entity:not null}) Gesture?.Invoke(new("double-click",hit.Entity,null));
            return;
        }
        if(Peeked!=null) return;
        if(kind=="down")
        {
            Cancel(); selectionAtDown=Selected; pressed=hit?.Entity; down=true; startX=x; startY=y;
            if(Selected==null && hit is {Kind:not "region"}) Select(pressed);
        }
        if(kind=="move" && down)
        {
            DeltaX=x-startX; DeltaY=y-startY;
            moved |= Math.Abs(DeltaX)+Math.Abs(DeltaY)>8;
            if(moved) { if(Selected!=pressed) Select(pressed); Target=scene.Region(x,y)?.Entity; }
        }
        if(kind=="up" && down)
        {
            var entity=pressed; var previous=selectionAtDown;
            var wasMoved=moved || Math.Abs(x-startX)+Math.Abs(y-startY)>8;
            Cancel();
            if(wasMoved && entity!=null)
            {
                var item=scene.Items.FirstOrDefault(i=>i.Entity==entity);
                if(item==null) return;
                var position=item.MoveBy(x-startX,y-startY);
                Select(null); Gesture?.Invoke(new("drag",entity,scene.Region(x,y)?.Entity,position.X,position.Y));
            }
            else if(previous!=null && previous!=hit?.Entity && scene.Region(x,y) is {} region)
            {
                // Clicking another card in the source pile changes selection; it does not move to itself.
                if(scene.Items.FirstOrDefault(i=>i.Entity==previous)?.Container==region.Entity)
                    Select(hit?.Kind=="region"?null:hit?.Entity);
                else { Select(null); Gesture?.Invoke(new("click",previous,region.Entity)); }
            }
            else Select(previous==hit?.Entity || hit?.Kind=="region"?null:hit?.Entity);
        }
    }
    public void RegionKey(int key)
    {
        if(!enabled) return;
        var region=scene.Items.FirstOrDefault(i=>i.Kind=="region" && i.Hotkey==key);
        if(region==null) return;
        if(Selected!=null)
        {
            if(scene.Items.FirstOrDefault(i=>i.Entity==Selected)?.Container==region.Entity) { Select(null); return; }
            var from=Selected; Select(null); Gesture?.Invoke(new("click",from,region.Entity));
        }
        else Select(scene.Items.LastOrDefault(i=>i.Container==region.Entity)?.Entity);
    }
}
