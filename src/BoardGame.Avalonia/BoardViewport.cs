using BoardGame.Rendering;
namespace BoardGame.View;

/// <summary>Board camera policy over an explicitly selected generic backend.</summary>
public sealed class BoardViewport : PlatformViewport
{
    private bool classic,sprites;
    public BoardViewport(GraphicsConfiguration graphics,ViewportComposition presentation):base(graphics,presentation)
    {MapKey=TableKeys.Map;}
    public void Canvas(TableScene scene)
    {
        var nextSprites=scene.Items.Any(i=>i.Kind=="sprite" || i.Resource is {} resource && scene.Resources.GetImage(resource).IsRegion);
        var changed=classic!=scene.ClassicCards || sprites!=nextSprites;classic=scene.ClassicCards;sprites=nextSprites;
        var width=920f;var height=640f;
        if((classic || sprites) && scene.Items.Count>0)
        {width=Math.Max(400,scene.Items.Max(i=>i.Bounds.X+i.Bounds.Width)+20);height=Math.Max(560,scene.Items.Max(i=>i.Bounds.Y+i.Bounds.Height)+20);}
        Background=classic?new(0,128,43):new(16,33,40);
        base.Canvas(width,height,changed?classic || sprites:null);
    }
}
