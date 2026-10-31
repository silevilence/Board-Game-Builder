using BoardGame.Kernel;
using BoardGame.Rendering;
using BoardGame.Runtime;

namespace BoardGame.View;

public sealed record BoardVisual(TableScene Scene,string? Selected=null,string? Target=null,string? Peeked=null,IReadOnlyList<TableItem>? Dragged=null);
public interface IBoardVisualSource
{
    BoardVisual Read(RenderFrame frame);
}
public sealed class BoardVisualChannel : IBoardVisualSource,IDisposable
{
    public Func<RenderFrame,BoardVisual>? ReadFrame {get;set;}
    public BoardVisual Read(RenderFrame frame)=>(ReadFrame ?? throw new InvalidOperationException("Board viewport is not bound"))(frame);
    public void Dispose()=>ReadFrame=null;
}
public sealed class ProjectedBoardVisual(ISeatProjection projection,ResourcePool resources) : IBoardVisualSource
{
    public BoardVisual Read(RenderFrame frame)=>new(TableScene.From(projection.View(),resources));
}

public static class BoardRenderPlugin
{
    public static BoardGame.AppRuntime.ApprovedApplicationPlugin Renderer()=>new(_=>Create());
    public static BoardGame.AppRuntime.ApprovedApplicationPlugin VisualInput()=>new(_=>new("board.visual-input","1",1,[Source],[],
        scope=>scope.Provide(Source,scope.Own(new BoardVisualChannel()))) {Execution=new(HostMode.Graphical,RuntimeScope.Viewport)});
    public const string Source="board.visual-source";
    public static RuntimePluginDefinition Input(IBoardVisualSource source)=>new("board.visual-input","1",1,[Source],[],scope=>scope.Provide(Source,source))
        {Execution=new(HostMode.Graphical,RuntimeScope.Viewport)};
    public static RuntimePluginDefinition Create()=>BoardRenderContract.Description with {Activate=scope=>
    {
        var source=scope.Service<IBoardVisualSource>(Source);var target=scope.Service<IDrawingTarget>("render.target");
        scope.Provide("board.render",new object());
        scope.Render("draw",frame=>
        {
            var visual=source.Read(frame);
            target.Draw(draw=>
            {
                ScenePainter.Draw(draw,visual.Scene,visual.Selected,visual.Target,visual.Peeked);
                foreach(var item in visual.Dragged ?? [])
                    if(visual.Scene.ClassicCards)ScenePainter.DrawCard(draw,visual.Scene,item,true);
                    else
                    {
                        var b=item.Bounds;draw.Box(new((int)b.X,(int)b.Y,(int)b.Width,(int)b.Height),new(225,197,137,190));
                        draw.Text(item.Label,(int)b.X+4,(int)b.Y+4,DrawColor.Black);
                    }
            });
        });
    }};
}
