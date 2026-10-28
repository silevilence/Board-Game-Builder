namespace BoardGame.Rendering;

/// <summary>Provides the current device context to independent render contributions.</summary>
public sealed class DrawingTarget : IDrawingTarget
{
    private IDraw2D? drawing;
    private int thread;
    public void Present(IDraw2D context,Action render)
    {
        if(drawing!=null)throw new InvalidOperationException("Drawing target reentry");
        drawing=context;thread=Environment.CurrentManagedThreadId;
        try {render();}finally {drawing=null;}
    }
    public void Draw(Action<IDraw2D> draw)
    {
        if(drawing==null || thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Drawing target is only available in its render frame");
        draw(drawing);
    }
}
