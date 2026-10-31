using Avalonia.Controls;
using BoardGame.Runtime;
using BoardGame.Rendering;
using System.Text.Json;
namespace BoardGame.View;

public class InteractiveBoard : UserControl, IDisposable
{
    private readonly BoardViewport viewport;
    public PlatformViewport Surface=>viewport;
    private readonly TableInteraction input=new();
    private TableScene scene=new([],BoardGame.Kernel.ResourcePool.Empty);
    private readonly SceneAnimationPlayer animation=new();
    private readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
    private bool requestedInput=true;
    public bool AnimationBusy=>animation.Busy;
    public TableScene CurrentScene=>scene;
    public event Action? AnimationChanged;
    private readonly StackPanel controls=new() {Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=6};
    public string? Selected=>input.Selected;
    public bool InputEnabled { get=>input.Enabled; set { requestedInput=value; UpdateInput(); } }
    private void UpdateInput() {var value=requestedInput && !animation.Busy;if(value!=input.Enabled)viewport.ResetClickSequence();input.Enabled=value;}
    public Task Shutdown=>viewport.Shutdown;
    public nint NativeHandle=>viewport.NativeHandle;
    public int RenderedFrames=>viewport.Frames;
    public int CachedImages=>viewport.CachedImages;
    public event Action<TableGesture>? Gesture;
    public event Action? SelectionChanged;
    public event Action<string>? Shortcut;
    public event Action<string>? RenderingError;
    public InteractiveBoard(GraphicsConfiguration graphics,BoardGame.Kernel.RuntimePluginSelection? renderer=null,ViewportComposition? approved=null)
    {
        RuntimePluginDefinition[] plugins=[BoardRenderPlugin.Input(new VisualSource(_=>ReadVisual())),BoardRenderPlugin.Create()];
        viewport=new(graphics,approved ?? new ViewportComposition(plugins.Select(p=>p.Id==BoardRenderContract.Id?renderer ?? BoardRenderContract.Selection:new BoardGame.Kernel.RuntimePluginSelection(p.Id,p.Version,p.Api,BoardGame.Kernel.RuntimeConfiguration.Empty)).ToArray(),plugins))
            {ConfigureScope=approved==null?null:scope=>scope.Service<BoardVisualChannel>(BoardRenderPlugin.Source).ReadFrame=_=>ReadVisual()};
        var zoomIn=new Button {Content="放大 +"}; var zoomOut=new Button {Content="缩小 −"};
        zoomIn.Click+=(_,_)=>viewport.ChangeZoom(1.1f); zoomOut.Click+=(_,_)=>viewport.ChangeZoom(1/1.1f);
        controls.Children.Add(zoomIn); controls.Children.Add(zoomOut);
        var panel=new DockPanel(); DockPanel.SetDock(controls,Dock.Top); panel.Children.Add(controls); panel.Children.Add(viewport); Content=panel;
        input.Gesture+=g=> { viewport.ResetClickSequence(); Gesture?.Invoke(g); }; input.SelectionChanged+=()=>SelectionChanged?.Invoke();
        viewport.UsesSecondaryPointer=(x,y)=>InputEnabled && input.CanPeek(x,y);
        viewport.Input+=(kind,x,y)=>
        {
            if(kind=="error") { InputEnabled=false; RenderingError?.Invoke(viewport.Error ?? "Unknown graphics device error"); return; }
            Pointer(kind,x,y);
        };
        viewport.KeyPressed+=HandleShortcut;
    }
    private sealed class VisualSource(Func<RenderFrame,BoardVisual> read) : IBoardVisualSource
    {public BoardVisual Read(RenderFrame frame)=>read(frame);}
    private BoardVisual ReadVisual()
    {
        var wasAnimating=animation.Busy;
        var frame=animation.Frame(clock.Elapsed.TotalMilliseconds);
        if(wasAnimating!=animation.Busy) {UpdateInput();AnimationChanged?.Invoke();}
        TableItem[] dragged=[];
        if(input.Dragging is {} dragging && scene.Items.FirstOrDefault(i=>i.Entity==dragging) is {} item)
        {
            var group=scene.ClassicCards?scene.Items.Where(i=>i.Entity==dragging || item.Container!=null && i.Container==item.Container && i.Bounds.Y>item.Bounds.Y):[item];
            dragged=group.Select(card=>card with {Bounds=card.Bounds with {X=card.Bounds.X+input.DeltaX,Y=card.Bounds.Y+input.DeltaY}}).ToArray();
        }
        return new(frame,Selected,input.Target,input.Peeked,dragged);
    }
    public void HandleShortcut(string key)
    {
        if(key=="cancel") { input.Cancel(true);if(scene.Items.Any(i=>i.Action!=null))Shortcut?.Invoke(key); return; }
        if(key is "new-game" or "restart" or "choose-seed") { Shortcut?.Invoke(key); return; }
        if(!InputEnabled) return;
        if(int.TryParse(key,out var number) && number is >=1 and <=8) input.RegionKey(number);
        else Shortcut?.Invoke(key);
    }
    public void Hint(string? entity,string? target)=>input.Select(entity,target);
    public void Pointer(string kind,float x,float y)=>input.Pointer(kind,x,y);
    public void Fit()=>viewport.Fit();
    public void SaveImage(string path)=>viewport.SaveImage(path);
    public void ChangeZoom(float factor)=>viewport.ChangeZoom(factor);
    public void CancelInput()=>viewport.CancelInput();
    public void Show(TableScene value) {var busy=animation.Busy;scene=value;animation.Show(value,clock.Elapsed.TotalMilliseconds);input.Show(value);UpdateInput();viewport.Canvas(value);controls.IsVisible=!value.ClassicCards;if(busy!=animation.Busy)AnimationChanged?.Invoke();}
    public void Dispose() {animation.Reset();viewport.Dispose();Gesture=null;SelectionChanged=null;Shortcut=null;RenderingError=null;AnimationChanged=null;}
}
