using System.Text.Json;
using BoardGame.Kernel;

namespace BoardGame.Runtime;

/// <summary>Content metadata is available to headless hosts; only a graphical installation binds the implementation.</summary>
public static class BoardRenderContract
{
    public const string Id="board.render";
    public static RuntimePluginDefinition Description=>new(Id,"1",1,[Id],["board.visual-source","render.target"],
        _=>throw new InvalidOperationException("board.render: graphical host must bind its installed renderer"))
        {Execution=new(HostMode.Graphical,RuntimeScope.Viewport,[new("draw",ApplicationPhase.Render)])};
    public static RuntimePluginSelection Selection=>new(Id,"1",1,JsonSerializer.SerializeToElement(new{}));
    public static RuntimePluginSelection Require(RuntimeStartup startup)
    {
        var selected=startup.Plugins.SingleOrDefault(p=>p.Id==Id)
            ?? throw new InvalidDataException("game.start.json.plugins: missing board.render; explicitly migrate the board presentation declaration before graphical play");
        if(selected.Version!="1" || selected.Api!=1)throw new InvalidDataException("board.render: incompatible renderer API/version");
        RuntimeDiscovery.CheckObject(selected.Configuration,[],"board.render.configuration");return selected;
    }
}
