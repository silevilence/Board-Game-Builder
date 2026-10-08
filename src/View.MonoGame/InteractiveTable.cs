using Avalonia.Controls;
namespace BoardGame.View;

public sealed class InteractiveTable : UserControl, IDisposable
{
    private readonly DesktopViewport viewport=new();
    private readonly TableInteraction input=new();
    private TableScene scene=new([],BoardGame.Kernel.ResourcePool.Empty);
    private readonly StackPanel controls=new() {Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=6};
    public string? Selected=>input.Selected;
    public bool InputEnabled { get=>input.Enabled; set { if(value!=input.Enabled) viewport.ResetClickSequence(); input.Enabled=value; } }
    public int RenderedFrames=>viewport.Frames;
    public int CachedImages=>viewport.CachedImages;
    public event Action<TableGesture>? Gesture;
    public event Action? SelectionChanged;
    public event Action<string>? Shortcut;
    public event Action<string>? RenderingError;
    public InteractiveTable()
    {
        var zoomIn=new Button {Content="放大 +"}; var zoomOut=new Button {Content="缩小 −"};
        zoomIn.Click+=(_,_)=>viewport.ChangeZoom(1.1f); zoomOut.Click+=(_,_)=>viewport.ChangeZoom(1/1.1f);
        controls.Children.Add(zoomIn); controls.Children.Add(zoomOut);
        var panel=new DockPanel(); DockPanel.SetDock(controls,Dock.Top); panel.Children.Add(controls); panel.Children.Add(viewport); Content=panel;
        input.Gesture+=g=> { viewport.ResetClickSequence(); Gesture?.Invoke(g); }; input.SelectionChanged+=()=>SelectionChanged?.Invoke();
        viewport.CanPeek=(x,y)=>InputEnabled && input.CanPeek(x,y);
        viewport.Paint=r=>
        {
            ScenePainter.Draw(r,scene,Selected,input.Target,input.Peeked);
            if(input.Dragging is {} dragging && scene.Items.FirstOrDefault(i=>i.Entity==dragging) is {} item)
            {
                var b=item.Bounds;
                if(scene.ClassicCards)
                {
                    var group=scene.Items.Where(i=>i.Entity==dragging || item.Container!=null && i.Container==item.Container && i.Bounds.Y>b.Y);
                    foreach(var card in group) ScenePainter.DrawCard(r,scene,card with {Bounds=card.Bounds with {X=card.Bounds.X+input.DeltaX,Y=card.Bounds.Y+input.DeltaY}},true);
                    return;
                }
                r.Box(new((int)(b.X+input.DeltaX),(int)(b.Y+input.DeltaY),(int)b.Width,(int)b.Height),new Microsoft.Xna.Framework.Color(225,197,137,190));
                r.Text(item.Label,(int)(b.X+input.DeltaX+4),(int)(b.Y+input.DeltaY+4),Microsoft.Xna.Framework.Color.Black);
            }
        };
        viewport.Input+=(kind,x,y)=>
        {
            if(kind=="error") { InputEnabled=false; RenderingError?.Invoke(viewport.Error ?? "Unknown graphics device error"); return; }
            input.Pointer(kind,x,y);
        };
        viewport.KeyPressed+=HandleShortcut;
    }
    public void HandleShortcut(string key)
    {
        if(key=="cancel") { input.Cancel(true); return; }
        if(key is "new-game" or "restart" or "choose-seed") { Shortcut?.Invoke(key); return; }
        if(!InputEnabled) return;
        if(int.TryParse(key,out var number) && number is >=1 and <=8) input.RegionKey(number);
        else Shortcut?.Invoke(key);
    }
    public void Hint(string? entity,string? target)=>input.Select(entity,target);
    public void Fit()=>viewport.Fit();
    public void SaveImage(string path)=>viewport.SaveImage(path);
    public void ChangeZoom(float factor)=>viewport.ChangeZoom(factor);
    public void CancelInput()=>viewport.CancelInput();
    public void Show(TableScene value) { scene=value; input.Show(value); viewport.Canvas(value); controls.IsVisible=!value.ClassicCards; }
    public void Dispose() { viewport.Dispose(); Gesture=null; SelectionChanged=null; Shortcut=null; RenderingError=null; }
}
