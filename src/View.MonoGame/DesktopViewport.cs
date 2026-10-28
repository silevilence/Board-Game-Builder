using BoardGame.Rendering;
using BoardGame.Kernel;
using System.Text.Json;
using Microsoft.Xna.Framework;
namespace BoardGame.View;

// Compatibility composition. The platform viewport itself knows no table layout or theme.
public sealed class DesktopViewport : PlatformViewport
{
    private bool classic,sprites;
    private Action<TableRenderer>? paint;
    public static int LiveDevices=>WindowsDxBackend.LiveDevices;
    public new Action<TableRenderer>? Paint {get=>paint;set=>paint=value;}
    public Func<float,float,bool>? CanPeek {get=>UsesSecondaryPointer;set=>UsesSecondaryPointer=value;}
    private static GraphicsConfiguration Configuration()
    {
        var plugin=WindowsDxBackend.Plugin();
        return new(new RuntimeStartup(1,"desktop.graphics","1",[new(plugin.Id,plugin.Version,plugin.Api,JsonSerializer.SerializeToElement(new{}))]),[plugin]);
    }
    public DesktopViewport():this(null) { }
    public DesktopViewport(ViewportComposition? presentation):base(Configuration(),presentation)
    {
        MapKey=TableKeys.Map;
        base.Paint=draw=>
        {
            var renderer=new TableRenderer(draw,()=>Metrics);
            if(paint!=null) {paint(renderer);return;}
            renderer.Box(new(40,50,600,340),new(26,67,70));
            renderer.Text("MONOGAME / TEST TABLE",65,70,Color.White);
            for(var i=0;i<5;i++)renderer.Box(new(70+i*100,150,80,120),new(226,197,140));
            renderer.Text("Wheel: zoom   Right drag: pan   Click: select",65,315,Color.White);
        };
    }
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
    public override void Dispose() {paint=null;base.Dispose();}
}
