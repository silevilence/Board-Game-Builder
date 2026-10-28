using BoardGame.Runtime;

namespace BoardGame.Rendering;

public sealed record ApplicationViewDescription(string Title,float Width,float Height,DrawColor Background);
public sealed record ViewInput(string Kind,ReadOnlyMemory<byte> Data);
public interface IApplicationView
{
    ApplicationViewDescription Description {get;}
    ViewInput? Translate(ViewportInput input);
    void Draw(IDraw2D drawing,RenderFrame frame);
}

public static class ApplicationViewPlugin
{
    public const string Capability="application.view";
    public static RuntimePluginDefinition Create(IApplicationView view)=>new("application.viewport","1",1,["application.render"],["render.target"],scope=>
    {
        var target=scope.Service<IDrawingTarget>("render.target");scope.Provide("application.render",view);
        scope.Render("draw",frame=>target.Draw(draw=>view.Draw(draw,frame)));
    }) {Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
}
